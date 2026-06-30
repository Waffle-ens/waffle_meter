using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly DispatcherTimer _statusTimer;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => TryEnableDarkTitleBar();

        // Poll character-detection + upload status while open (React SettingsPanel 2.5s poll).
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2500) };
        _statusTimer.Tick += (_, _) =>
        {
            _viewModel.RefreshCharacterStatus();
            _viewModel.RefreshLogging();
        };
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer.Stop();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Commit(); // commit buffered hotkeys; other settings already applied live
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _viewModel.Revert();
        Close();
    }

    private void OnSaveServer(object sender, RoutedEventArgs e) => _viewModel.SaveServer();

    // ✕ next to a hotkey box → unassign that hotkey (Tag points at the box). The box's two-way Combo
    // binding propagates the null to the view model's pending combo; committed on Save like a rebind.
    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is HotkeyCaptureBox box)
        {
            box.Unassign();
        }
    }

    // Win10 1809+/Win11 immersive dark title bar so the OS chrome matches the dark client area.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TryEnableDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on older builds); try both.
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // older OS without dwmapi attribute — light title bar, harmless
        }
    }

    private void OnResetTheme(object sender, RoutedEventArgs e) => _viewModel.ResetTheme();

    private void OnResetDefaults(object sender, RoutedEventArgs e) => _viewModel.ResetDefaults();

    private void OnToggleLogging(object sender, RoutedEventArgs e) => _viewModel.ToggleLogging();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => _viewModel.OpenLogFolder();

    private void OnCheckUpdate(object sender, RoutedEventArgs e) => _viewModel.CheckForUpdate();

    private void OnTestAlarmSound(object sender, RoutedEventArgs e) => _viewModel.TestAlarmSound();

    private void OnAddCustomAlarm(object sender, RoutedEventArgs e) => _viewModel.AddCustomAlarm();

    private void OnDeleteCustomAlarm(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CustomAlarmRow row })
        {
            _viewModel.DeleteCustomAlarm(row.Id);
        }
    }

    private void OnToggleCustomAlarm(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { DataContext: CustomAlarmRow row } cb)
        {
            _viewModel.SetCustomAlarmEnabled(row.Id, cb.IsChecked == true);
        }
    }

    private void OnResetMeterPosition(object sender, RoutedEventArgs e) => _viewModel.ResetMeterPosition();

    private void OnResetJoinPosition(object sender, RoutedEventArgs e) => _viewModel.ResetJoinPosition();

    private void OnResetHistoryPosition(object sender, RoutedEventArgs e) => _viewModel.ResetHistoryPosition();

    private void OnApplyConsent(object sender, RoutedEventArgs e) => RunThenRefresh(_viewModel.ApplyConsent);

    private void OnRefreshConsent(object sender, RoutedEventArgs e) => RunThenRefresh(_viewModel.RefreshConsentFromServer);

    private void OnOpenMyStats(object sender, RoutedEventArgs e) => _viewModel.OpenMyStats();

    private void OnToggleCharacterPublic(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is ConsentCharacterRow row)
        {
            bool makePublic = cb.IsChecked == true;
            RunThenRefresh(() => _viewModel.SetCharacterPublic(row.IdentityHash, makePublic));
        }
    }

    private void OnRevokeCharacter(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ConsentCharacterRow row)
        {
            RunThenRefresh(() => _viewModel.RevokeConsentCharacter(row.IdentityHash));
        }
    }

    // Consent actions hit the backend (off the UI thread); the local-state re-read + list rebuild must run on
    // the UI thread (ObservableCollection mutation). RefreshConsentState also surfaces the rolled-back public
    // flag + the public_requires_ownership notice.
    private void RunThenRefresh(Action network) => Task.Run(() =>
    {
        try
        {
            network();
        }
        catch
        {
            // surfaced via ConsentStatus / the list on the refresh below
        }

        Dispatcher.Invoke(_viewModel.RefreshConsentState);
    });
}
