using WaffleMeter.App.Core;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

/// <summary>Outcome of an import, for the status line.</summary>
/// <param name="Applied">How many keys actually changed.</param>
/// <param name="BackupPath">Where the pre-import snapshot went, or null if it could not be written.</param>
/// <param name="RestartHint">True when something that only reads at startup was among the changes.</param>
public sealed record SettingsImportResult(int Applied, string? BackupPath, bool RestartHint);

/// <summary>
/// Writes a decoded settings code into the live app.
/// <para><b>Raw in, then reload.</b> Values are written straight to the properties file and every model is then
/// told to re-read. Assigning the model properties instead would put the raw string in memory while the file
/// holds the same string — correct this session, different after a restart, because <c>GetProperty</c> re-decodes
/// on the way out. See <see cref="MeterSettings.Reload"/>.</para>
/// <para><b>One save.</b> The whole write happens inside <see cref="PropertyHandler.RunBatched"/>; otherwise
/// ~70 keys mean ~70 full-file rewrites.</para>
/// </summary>
public sealed class SettingsBundleApplier
{
    private readonly MeterServices _services;
    private readonly MeterSettings _settings;
    private readonly MeterColorTheme _theme;
    private readonly SkinManager _skin;
    private readonly OverlayController _controller;
    private readonly HotkeyHandler _hotkeys;
    private readonly BuffPresetManager _presets;
    private readonly SkillVisibility _skills;

    public SettingsBundleApplier(
        MeterServices services,
        MeterSettings settings,
        MeterColorTheme theme,
        SkinManager skin,
        OverlayController controller,
        HotkeyHandler hotkeys,
        BuffPresetManager presets,
        SkillVisibility skills)
    {
        _services = services;
        _settings = settings;
        _theme = theme;
        _skin = skin;
        _controller = controller;
        _hotkeys = hotkeys;
        _presets = presets;
        _skills = skills;
    }

    /// <summary>Keys nothing re-reads at runtime. Changing one is honest about needing a restart rather than
    /// silently doing nothing — "가져왔는데 안 바뀐다" is the complaint that produces.</summary>
    private static readonly string[] RestartOnly = { "captureBackend", "vrrCompatMode" };

    public SettingsImportResult Apply(SettingsBundlePlan plan, string appVersion, DateTimeOffset now)
    {
        PropertyHandler props = _services.Props;

        // Before anything is written. The settings window's Cancel restores 19 values captured when the window
        // opened, so it cannot undo this — the snapshot is the only way back.
        string? backup = SettingsBackupStore.Save(props, appVersion, now);

        int applied = 0;
        props.RunBatched(() =>
        {
            foreach ((string key, string value) in plan.Bundle.Data)
            {
                if (!SettingsKeyCatalog.IsKnown(key))
                {
                    continue; // a key from a newer build, or one we retracted
                }

                props.SetProperty(key, value);
                applied++;
            }
        });

        // Now make the live objects catch up, in dependency order: settings first (presets read from it),
        // then everything that owns its own key.
        _settings.Reload();
        _theme.Reload();
        _skin.Apply(props.GetProperty("skin") ?? _skin.Current);
        _controller.SetAutoHide(props.GetProperty("isAutoHide") != "false");
        _controller.SetKeepOverlayWhenHidden(props.GetProperty("keepOverlayWhenMeterHidden") == "true");
        _hotkeys.Reload();
        _presets.Reload();
        _skills.Reload();

        bool restart = plan.Bundle.Data.Keys.Any(k => RestartOnly.Contains(k, StringComparer.Ordinal));
        return new SettingsImportResult(applied, backup, restart);
    }
}
