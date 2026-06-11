using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The power-button close chooser (port of the React close-action Dialog). Asks whether to hide to the
/// tray (keep capture running) or fully exit. <see cref="Choice"/> holds the result after ShowDialog.
/// </summary>
public partial class CloseActionDialog : Window
{
    public enum CloseChoice { Cancel, Tray, Exit }

    public CloseChoice Choice { get; private set; } = CloseChoice.Cancel;

    public CloseActionDialog()
    {
        InitializeComponent();
    }

    private void OnTray(object sender, RoutedEventArgs e) => Pick(CloseChoice.Tray);

    private void OnExit(object sender, RoutedEventArgs e) => Pick(CloseChoice.Exit);

    private void OnCancel(object sender, RoutedEventArgs e) => Pick(CloseChoice.Cancel);

    private void Pick(CloseChoice choice)
    {
        Choice = choice;
        DialogResult = choice != CloseChoice.Cancel;
        Close();
    }
}
