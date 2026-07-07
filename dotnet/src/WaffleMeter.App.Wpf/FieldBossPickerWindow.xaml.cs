using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WaffleMeter.App.Wpf;

/// <summary>The field-boss alarm selection dialog. Wraps <see cref="FieldBossPickerViewModel"/>; toggles persist live.</summary>
public partial class FieldBossPickerWindow : Window
{
    private readonly FieldBossPickerViewModel _viewModel;

    public FieldBossPickerWindow(FieldBossPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => TryEnableDarkTitleBar();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnEnableAll(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is FieldBossGroup group)
        {
            _viewModel.SetGroup(group, true);
        }
    }

    private void OnDisableAll(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is FieldBossGroup group)
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
