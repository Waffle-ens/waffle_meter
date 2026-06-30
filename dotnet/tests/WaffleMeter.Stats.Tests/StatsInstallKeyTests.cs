using System.Security.Cryptography;
using System.Text;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsInstallKeyTests : IDisposable
{
    private readonly string _tempAppData;

    public StatsInstallKeyTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "wm_installkey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Sign_verifies_against_exported_public_key()
    {
        var key = new StatsInstallKey(new PropertyHandler(_tempAppData));
        const string canonical = "POST\n/api/v1/reports\ninstall-1\n1700000000000\nnonce\nYm9keQ==";

        string signatureB64 = key.Sign(canonical);

        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyB64()), out _);
        bool ok = verifier.VerifyData(
            Encoding.UTF8.GetBytes(canonical),
            Convert.FromBase64String(signatureB64),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        Assert.True(ok);

        // A tampered canonical must NOT verify under the same signature.
        bool tampered = verifier.VerifyData(
            Encoding.UTF8.GetBytes(canonical + "x"),
            Convert.FromBase64String(signatureB64),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        Assert.False(tampered);
    }

    [Fact]
    public void Public_key_is_a_p256_spki()
    {
        var key = new StatsInstallKey(new PropertyHandler(_tempAppData));
        using ECDsa imported = ECDsa.Create();
        imported.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyB64()), out _);
        Assert.Equal(256, imported.KeySize);
    }

    [Fact]
    public void Same_install_reloads_the_same_key()
    {
        // First instance generates + persists; a fresh instance over the same settings must reload it.
        string first = new StatsInstallKey(new PropertyHandler(_tempAppData)).PublicKeyB64();
        string second = new StatsInstallKey(new PropertyHandler(_tempAppData)).PublicKeyB64();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Distinct_installs_get_distinct_keys()
    {
        string a = new StatsInstallKey(new PropertyHandler(_tempAppData)).PublicKeyB64();
        string otherAppData = Path.Combine(Path.GetTempPath(), "wm_installkey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherAppData);
        try
        {
            string b = new StatsInstallKey(new PropertyHandler(otherAppData)).PublicKeyB64();
            Assert.NotEqual(a, b);
        }
        finally
        {
            Directory.Delete(otherAppData, recursive: true);
        }
    }
}
