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

    public string? GetProperty(string key) => EncodeToEucKr(_props.GetProperty(key));

    public string? GetProperty(string key, string defaultValue) => EncodeToEucKr(_props.GetProperty(key, defaultValue));

    public void SetProperty(string key, string value)
    {
        lock (_gate)
        {
            _props.SetProperty(key, value);
            Save();
        }
    }

    private void Save()
    {
        using FileStream fs = File.Create(_settingFilePath);
        _props.Store(fs, "settings");
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
