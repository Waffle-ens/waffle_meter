using System.Text;

namespace WaffleMeter.Services;

/// <summary>
/// Settings store, ported verbatim from Kotlin <c>config.PropertyHandler</c>: a Java-format
/// <c>settings.properties</c> under <c>%APPDATA%\waffle_meter.v1.4</c>, with one-time copy-forward
/// from legacy app dirs, and the EUC-KR re-decode quirk on every read.
///
/// The quirk: Java's <c>Properties.load</c> reads the file as ISO-8859-1, so Korean stored as raw
/// EUC-KR bytes comes back as Latin-1 chars; <see cref="EncodeToEucKr"/> reverses that (Latin-1
/// bytes re-decoded as EUC-KR). For ASCII values it is a no-op, so the behaviour is identical for
/// the booleans/numbers/hotkey codes that make up real settings. Kept exactly so existing users'
/// files behave the same byte-for-byte.
/// </summary>
public sealed class PropertyHandler
{
    private const string AppName = "waffle_meter.v1.4";
    private static readonly string[] LegacyAppNames = { "waffle_meter.v1.3", "waffle_meter.v1.2" };
    private const string SettingFileName = "settings.properties";

    private static readonly Encoding EucKr;

    static PropertyHandler()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        EucKr = Encoding.GetEncoding(51949); // EUC-KR
    }

    private readonly JavaProperties _props = new();
    private readonly string _settingFilePath;
    private readonly object _gate = new();

    /// <param name="appDataOverride">Overrides the %APPDATA% base (used by tests).</param>
    public PropertyHandler(string? appDataOverride = null)
    {
        string appData = appDataOverride
            ?? Environment.GetEnvironmentVariable("APPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(appData, AppName);
        Directory.CreateDirectory(dir);
        _settingFilePath = Path.Combine(dir, SettingFileName);

        if (!File.Exists(_settingFilePath))
        {
            foreach (string legacy in LegacyAppNames)
            {
                string legacyPath = Path.Combine(appData, legacy, SettingFileName);
                if (File.Exists(legacyPath))
                {
                    try
                    {
                        File.Copy(legacyPath, _settingFilePath, overwrite: false);
                    }
                    catch
                    {
                        // 이전 설정파일 복사에 실패했습니다.
                    }

                    break;
                }
            }
        }

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingFilePath))
            {
                using FileStream fs = File.OpenRead(_settingFilePath);
                _props.Load(fs);
            }
            else
            {
                File.Create(_settingFilePath).Dispose();
            }
        }
        catch (IOException)
        {
            // 설정파일 읽기에 실패했습니다.
        }
    }

    /// <summary>Merge an additional properties resource (Kotlin loaded /version.properties too).</summary>
    public void MergeResource(Stream stream) => _props.Load(stream);

    public string AppDirectory() => Path.GetDirectoryName(_settingFilePath)!;

    // Reads take the same gate as writes: the underlying JavaProperties dictionary isn't thread-safe, and
    // settings are now written off the UI thread too (e.g. the stats upload queue caching a character grant),
    // so an unlocked read could race a concurrent write and throw/tear. EncodeToEucKr is pure (no _props
    // access), so holding the lock across it is brief and deadlock-free.
    public string? GetProperty(string key)
    {
        lock (_gate)
        {
            return EncodeToEucKr(_props.GetProperty(key));
        }
    }

    public string? GetProperty(string key, string defaultValue)
    {
        lock (_gate)
        {
            return EncodeToEucKr(_props.GetProperty(key, defaultValue));
        }
    }

    public void SetProperty(string key, string value)
    {
        lock (_gate)
        {
            _props.SetProperty(key, value);
            if (_batchDepth == 0)
            {
                Save();
            }
            else
            {
                _batchDirty = true;
            }
        }
    }

    private int _batchDepth;
    private bool _batchDirty;

    /// <summary>
    /// Run several writes as one save. Every <see cref="SetProperty"/> normally rewrites the WHOLE file, so a
    /// settings import touching ~70 keys would rewrite it ~70 times.
    /// <para>Deliberately a callback and not an <c>IDisposable</c> scope: a missed <c>Dispose</c> would leave the
    /// process in a state where every later setting write lands in memory only, with no symptom at all until the
    /// next restart. There is no way to forget to close this one.</para>
    /// </summary>
    public void RunBatched(Action body)
    {
        lock (_gate)
        {
            _batchDepth++;
            try
            {
                body();
            }
            finally
            {
                _batchDepth--;
                if (_batchDepth == 0 && _batchDirty)
                {
                    _batchDirty = false;
                    Save();
                }
            }
        }
    }

    /// <summary>
    /// The stored values EXACTLY as they sit in the file, bypassing <see cref="GetProperty"/>'s EUC-KR
    /// re-decode. Needed for keys no live model owns (theme JSON, hotkeys, skill visibility) — reading those
    /// through the normal path and writing them back would round-trip non-ASCII through Latin-1 and lose it.
    /// <para>⚠ Do NOT use this for keys a model does own. Those are held in memory in the decoded
    /// representation, so mixing the two sources in one bundle produces values that render correctly this
    /// session and break on restart.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> RawEntries()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_props.Entries, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Atomic replace: write a temp file, then swap. <c>File.Create</c> truncates in place, so a crash (or a
    /// full disk) part-way through left a truncated settings file — every setting gone. The batch window above
    /// widens that gap, so it is closed here in the same change.
    /// </summary>
    private void Save()
    {
        string dir = Path.GetDirectoryName(_settingFilePath)!;
        string temp = Path.Combine(dir, Path.GetFileName(_settingFilePath) + ".tmp");
        try
        {
            using (FileStream fs = File.Create(temp))
            {
                _props.Store(fs, "settings");
            }

            File.Move(temp, _settingFilePath, overwrite: true);
        }
        catch
        {
            // Fall back to the in-place write rather than losing the change entirely (e.g. a temp file blocked
            // by an AV scanner). Worst case is the old behaviour, not a worse one.
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // best effort
            }

            using FileStream fs = File.Create(_settingFilePath);
            _props.Store(fs, "settings");
        }
    }

    private static string? EncodeToEucKr(string? value)
    {
        if (value == null)
        {
            return null;
        }

        return EucKr.GetString(Encoding.Latin1.GetBytes(value));
    }
}
