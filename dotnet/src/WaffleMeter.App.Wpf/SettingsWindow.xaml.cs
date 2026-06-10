using System.Windows;

namespace WaffleMeter.App.Wpf;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnApplyConsent(object sender, RoutedEventArgs e) => RunBackground(_viewModel.ApplyConsent);

    private void OnRefreshConsent(object sender, RoutedEventArgs e) => RunBackground(_viewModel.RefreshConsentFromServer);

    private void OnSaveServer(object sender, RoutedEventArgs e) => _viewModel.SaveServer();

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
