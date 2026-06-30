using System.Security.Cryptography;
using System.Text;
using WaffleMeter.Services;

namespace WaffleMeter.Stats;

/// <summary>
/// The signing seam the API client writes through. Abstracted so signed-write tests can inject a fake
/// (or a real key over a temp settings store) without taking a hard dependency on DPAPI/Windows.
/// </summary>
public interface IStatsSigner
{
    /// <summary>base64(DER SPKI) of the install's ECDSA P-256 public key — header <c>X-WM-Install-Key</c>.</summary>
    string PublicKeyB64();

    /// <summary>base64(DER ECDSA-P256-SHA256 signature) over the UTF-8 <c>canonical</c> string — header
    /// <c>X-WM-Signature</c> (DER fixed: .NET <see cref="DSASignatureFormat.Rfc3279DerSequence"/>, Node
    /// <c>dsaEncoding:'der'</c>).</summary>
    string Sign(string canonical);
}

/// <summary>
/// Per-install ECDSA P-256 signing identity (SHARED CONTRACT §2.1 / 설계도 §3.1). The keypair is created
/// once and reloaded on every run so the install keeps a stable public key — no global secret. The private
/// key is stored as PKCS#8 wrapped with DPAPI (CurrentUser) and base64-encoded into settings via
/// <see cref="PropertyHandler"/> (ASCII base64 round-trips the EUC-KR settings encoding untouched). Only
/// the SPKI public key and DER signatures cross the wire; how the private key is stored is a local concern
/// and is not part of the shared contract.
/// </summary>
public sealed class StatsInstallKey : IStatsSigner
{
    // base64(DPAPI(PKCS#8 private key)). Versioned so a future key-format change can coexist/migrate.
    private const string KeyPrivate = "statsInstallKeyPkcs8DpapiV1";

    private readonly PropertyHandler _props;
    private readonly object _gate = new();
    private ECDsa? _ecdsa;

    public StatsInstallKey(PropertyHandler props) => _props = props;

    public string PublicKeyB64()
    {
        // ECDsa instances aren't documented thread-safe; serialize every use of the shared key.
        lock (_gate)
        {
            return Convert.ToBase64String(EnsureKey().ExportSubjectPublicKeyInfo());
        }
    }

    public string Sign(string canonical)
    {
        byte[] data = Encoding.UTF8.GetBytes(canonical);
        lock (_gate)
        {
            return Convert.ToBase64String(
                EnsureKey().SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }
    }

    // Caller holds _gate.
    private ECDsa EnsureKey() => _ecdsa ??= LoadOrCreate();

    // DPAPI (ProtectedData) is flagged windows-only by CA1416, but this project is net10.0 (TFM-agnostic).
    // The whole meter is Windows-only (WinDivert/WPF/GameGuard), so the at-rest wrapping path runs only on
    // Windows — suppress the platform advisory here rather than ripple a -windows TFM through the graph.
#pragma warning disable CA1416
    private ECDsa LoadOrCreate()
    {
        string? saved = NonBlank(_props.GetProperty(KeyPrivate));
        if (saved != null)
        {
            try
            {
                byte[] pkcs8 = ProtectedData.Unprotect(
                    Convert.FromBase64String(saved), optionalEntropy: null, DataProtectionScope.CurrentUser);
                ECDsa loaded = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                loaded.ImportPkcs8PrivateKey(pkcs8, out _);
                CryptographicOperations.ZeroMemory(pkcs8);
                return loaded;
            }
            catch
            {
                // Unreadable (corrupt, or settings copied to another user/machine where DPAPI can't unwrap)
                // — fall through and regenerate. A new keypair just re-earns grants on the next upload.
            }
        }

        ECDsa created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] fresh = created.ExportPkcs8PrivateKey();
        try
        {
            byte[] wrapped = ProtectedData.Protect(fresh, optionalEntropy: null, DataProtectionScope.CurrentUser);
            _props.SetProperty(KeyPrivate, Convert.ToBase64String(wrapped));
        }
        catch
        {
            // Persisting the wrapped key failed (DPAPI hiccup, settings file locked, disk full). The freshly
            // generated key is still usable in memory this session, so sign with it now and just re-generate
            // next run — never fail a write because the key couldn't be saved (guardrail: writes always work).
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fresh);
        }

        return created;
    }
#pragma warning restore CA1416

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
