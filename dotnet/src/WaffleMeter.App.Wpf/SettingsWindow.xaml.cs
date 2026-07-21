using System.Diagnostics;
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
        DarkTitleBar.Apply(this);

        // Poll character-detection + upload status while open (React SettingsPanel 2.5s poll).
        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(2500) };
        _statusTimer.Tick += (_, _) =>
        {
            _viewModel.RefreshCharacterStatus();
            _viewModel.RefreshLogging();
        };
        _statusTimer.Start();
        Closed += (_, _) => { _statusTimer.Stop(); _viewModel.DisposeBuffPicker(); };
    }

    // Reset the scroll to the top when switching category — the content is one shared ScrollViewer, so a
    // long section left it scrolled down and a shorter one would otherwise open into blank space.
    private void OnNavChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => ContentScroll?.ScrollToTop();

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

    private void OnResetTheme(object sender, RoutedEventArgs e) => _viewModel.ResetTheme();

    private void OnResetDefaults(object sender, RoutedEventArgs e) => _viewModel.ResetDefaults();

    private void OnToggleLogging(object sender, RoutedEventArgs e) => _viewModel.ToggleLogging();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => _viewModel.OpenLogFolder();

    private void OnOpenReplayFolder(object sender, RoutedEventArgs e) => _viewModel.OpenReplayFolder();

    private void OnPlayReplay(object sender, RoutedEventArgs e) => _viewModel.PlayReplay();

    private void OnResetDummyDps(object sender, RoutedEventArgs e) => _viewModel.ResetDummyDps();

    private void OnAddCustomFont(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "커스텀 폰트 추가",
            Filter = "폰트 파일 (*.ttf, *.otf)|*.ttf;*.otf",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        if (!_viewModel.AddCustomFont(dlg.FileName))
        {
            MessageBox.Show(
                this,
                "이 파일에서 폰트를 불러오지 못했습니다. 올바른 .ttf/.otf 폰트 파일인지 확인해 주세요.",
                "커스텀 폰트",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCheckUpdate(object sender, RoutedEventArgs e) => _viewModel.CheckForUpdate();

    private void OnTestAlarmSound(object sender, RoutedEventArgs e) => _viewModel.TestAlarmSound();

    private void OnTestTts(object sender, RoutedEventArgs e) => _viewModel.TestTts();

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

    // The preset-name box commits on focus loss (writing on every keystroke would rewrite settings.properties
    // per character); Enter is the other way a user expects a rename to stick.
    private void OnPresetNameKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is System.Windows.Controls.TextBox box)
        {
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }

    private void OnBuffGroupMode(object sender, RoutedEventArgs e)
    {
        // Tag = "group|mode": set every buff in a job group to a mode at once.
        if (sender is FrameworkElement { Tag: string tag } && tag.Split('|') is [_, var modeStr]
            && int.TryParse(modeStr, out int mode) && ((FrameworkElement)sender).DataContext is BuffJobGroup group)
        {
            _viewModel.BuffPicker.SetGroup(group, mode);
        }
    }

    // 맨 앞 고정 토글. 고정은 정렬 모드보다 우선해 오버레이 앞쪽에 온다.
    private void OnBuffPinToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuffPickerItem item })
        {
            _viewModel.BuffPicker.TogglePin(item);
        }
    }

    // 하단 바깥 링크 3종. 후원만 안내창을 한 번 거친다 — 무엇에 쓰이는지, 그리고 후원 없이도 전 기능을
    // 쓸 수 있다는 고지를 먼저 보여주고 나서 플랫폼으로 보낸다.
    private void OnOpenStatsWeb(object sender, RoutedEventArgs e) => OpenUrl(_viewModel.StatsWebUrl);

    private void OnOpenDiscord(object sender, RoutedEventArgs e) => OpenUrl(ExternalLinks.Discord);

    private void OnOpenDonate(object sender, RoutedEventArgs e)
    {
        new DonateDialog { Owner = this }.ShowDialog();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception)
        {
            // 기본 브라우저가 없거나 셸 연결이 깨진 환경 — 설정창이 죽을 이유는 아니다.
        }
    }

    private void OnOpenFieldBossPicker(object sender, RoutedEventArgs e)
    {
        FieldBossPickerWindow picker = _viewModel.CreateFieldBossPicker();
        picker.Owner = this;
        picker.ShowDialog();
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
