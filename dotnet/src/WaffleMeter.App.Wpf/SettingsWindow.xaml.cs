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

    private void OnResetMeterPosition(object sender, RoutedEventArgs e) => _viewModel.ResetMeterPosition();

    private void OnResetJoinPosition(object sender, RoutedEventArgs e) => _viewModel.ResetJoinPosition();

    private void OnResetHistoryPosition(object sender, RoutedEventArgs e) => _viewModel.ResetHistoryPosition();

    private void OnApplyConsent(object sender, RoutedEventArgs e) => RunBackground(_viewModel.ApplyConsent);

    private void OnRefreshConsent(object sender, RoutedEventArgs e) => RunBackground(_viewModel.RefreshConsentFromServer);

    // Consent apply/refresh hit the backend; keep them off the UI thread.
    private static void RunBackground(Action work) => Task.Run(() =>
    {
        try
        {
            work();
        }
        catch
        {
            // surfaced via ConsentStatus on the next sync
        }
    });
}
