using System.ComponentModel;
using System.Runtime.CompilerServices;
using WaffleMeter.App.Core;
using WaffleMeter.Capture.Live;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Backs the settings window: wraps the ported services (stats consent, server config, hotkeys,
/// upload status) for binding. Service calls that hit the network (consent apply/refresh) are run
/// off the UI thread by the window; this view model just maps to/from the service surface.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly MeterServices _services;
    private readonly HotkeyHandler _hotkeys;

    public SettingsViewModel(MeterServices services, HotkeyHandler hotkeys)
    {
        _services = services;
        _hotkeys = hotkeys;
        Reload();
    }

    // ---- stats consent ----
    private bool _consentAccepted;
    public bool ConsentAccepted { get => _consentAccepted; set => Set(ref _consentAccepted, value); }

    private bool _uploadEnabled;
    public bool UploadEnabled { get => _uploadEnabled; set => Set(ref _uploadEnabled, value); }

    private bool _publicCharacter;
    public bool PublicCharacter { get => _publicCharacter; set => Set(ref _publicCharacter, value); }

    private string _consentStatus = string.Empty;
    public string ConsentStatus { get => _consentStatus; private set => Set(ref _consentStatus, value); }

    /// <summary>Applies the consent choice (hits the backend; call off the UI thread).</summary>
    public void ApplyConsent()
    {
        string state = ConsentAccepted ? "accepted" : "declined";
        ApplyInfo(_services.Consent.Set(state, UploadEnabled, PublicCharacter, _services.Version));
    }

    /// <summary>Re-syncs consent state from the backend (call off the UI thread).</summary>
    public void RefreshConsentFromServer() => ApplyInfo(_services.Consent.GetInfo(syncRemote: true, _services.Version));

    private void ApplyInfo(StatsConsentManager.Info info)
    {
        ConsentAccepted = info.State == "accepted";
        UploadEnabled = info.UploadEnabled;
        PublicCharacter = info.PublicCharacter;
        ConsentStatus = info.SyncError is { } error
            ? $"{info.State} · {info.SyncStatus} ({error})"
            : $"{info.State} · {info.SyncStatus}";
        RaiseUploadStatus();
    }

    // ---- server ----
    private string _serverIp = string.Empty;
    public string ServerIp { get => _serverIp; set => Set(ref _serverIp, value); }

    private string _serverPort = string.Empty;
    public string ServerPort { get => _serverPort; set => Set(ref _serverPort, value); }

    public void SaveServer()
    {
        _services.Props.SetProperty("server.ip", ServerIp);
        _services.Props.SetProperty("server.port", ServerPort);
    }

    // ---- hotkeys (display) ----
    public string ResetHotkey => Describe(_hotkeys.Reset);
    public string VisibilityHotkey => Describe(_hotkeys.Visibility);
    public string ClickThroughHotkey => Describe(_hotkeys.ClickThrough);

    // ---- upload status ----
    public string UploadStatus
    {
        get
        {
            StatsUploadStatus status = _services.UploadQueue.Status();
            return $"업로드 {status.Uploaded} · 대기 {status.Pending} · 건너뜀 {status.Skipped} · 실패 {status.Failed}";
        }
    }

    public void Reload()
    {
        ApplyInfo(_services.Consent.GetInfo(syncRemote: false, _services.Version));
        CaptureConfig config = _services.BuildCaptureConfig();
        ServerIp = config.ServerIp;
        ServerPort = config.ServerPort;
        OnPropertyChanged(nameof(ResetHotkey));
        OnPropertyChanged(nameof(VisibilityHotkey));
        OnPropertyChanged(nameof(ClickThroughHotkey));
    }

    public void RaiseUploadStatus() => OnPropertyChanged(nameof(UploadStatus));

    internal static string Describe(HotkeyCombo combo)
    {
        var parts = new List<string>();
        if ((combo.Modifiers & HotkeyHandler.ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((combo.Modifiers & HotkeyHandler.ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((combo.Modifiers & HotkeyHandler.ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((combo.Modifiers & HotkeyHandler.ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(combo.VkCode is >= 0x41 and <= 0x5A ? ((char)combo.VkCode).ToString() : $"0x{combo.VkCode:X2}");
        return string.Join("+", parts);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
