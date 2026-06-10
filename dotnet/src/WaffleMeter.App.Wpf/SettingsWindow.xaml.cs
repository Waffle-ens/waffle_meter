using System.Windows;
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

    private void OnToggleLogging(object sender, RoutedEventArgs e) => _viewModel.ToggleLogging();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => _viewModel.OpenLogFolder();

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
