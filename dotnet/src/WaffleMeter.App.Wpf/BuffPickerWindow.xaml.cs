using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>The per-job buff picker dialog. Wraps <see cref="BuffPickerViewModel"/>; toggles persist live.</summary>
public partial class BuffPickerWindow : Window
{
    private readonly BuffPickerViewModel _viewModel;

    public BuffPickerWindow(BuffPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => TryEnableDarkTitleBar();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnShowAll(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is BuffJobGroup group)
        {
            _viewModel.SetGroup(group, true);
        }
    }

    private void OnHideAll(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is BuffJobGroup group)
        {
            _viewModel.SetGroup(group, false);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TryEnableDarkTitleBar()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // older OS — light title bar, harmless
        }
    }
}
