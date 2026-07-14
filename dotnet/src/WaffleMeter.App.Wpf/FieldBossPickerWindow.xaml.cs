using System.Windows;

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
        DarkTitleBar.Apply(this);
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

}
