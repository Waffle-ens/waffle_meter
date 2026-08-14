using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
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
            _viewModel.RefreshTierStatus(); // local read of the cached artifact — no network
        };
        _statusTimer.Start();
        _viewModel.RefreshTierStatus(); // fill the line before the first tick so it never opens blank
        Closed += (_, _) => { _statusTimer.Stop(); _viewModel.DisposeBuffPicker(); _viewModel.StopNameFxPreview(); };

        VerifyNavContract();
    }

    /// <summary>
    /// The tab key table lives in three places — <see cref="SettingsViewModel.NavKeys"/>, the nav rail's
    /// <c>ListBoxItem.Tag</c>s, and each content panel's <c>ConverterParameter</c> — and nothing makes them
    /// agree. A typo in any one of them produces no error: <see cref="StringEqualsToVisibilityConverter"/>
    /// reads "no match" as a legitimate answer and collapses the panel, so the symptom is a blank right-hand
    /// side, or a tab that is simply unreachable. Neither the compiler nor a unit test sees it.
    /// <para>DEBUG-only: the dangerous half (a stored key that matches nothing) is already structurally
    /// impossible because <c>SelectedNav</c>'s setter absorbs unknown values. What is left is a panel or a
    /// nav item that no longer lines up, and that is a development-time mistake — assert loudly there and
    /// keep release builds free of the walk.</para>
    /// </summary>
    [Conditional("DEBUG")]
    private void VerifyNavContract()
    {
        string?[] navTags = NavRail.Items
            .OfType<System.Windows.Controls.ListBoxItem>()
            .Select(i => i.Tag as string)
            .ToArray();

        Debug.Assert(
            navTags.SequenceEqual(SettingsViewModel.NavKeys, StringComparer.Ordinal),
            $"설정창 nav Tag 가 SettingsViewModel.NavKeys 와 어긋납니다.\n" +
            $"  nav : {string.Join(", ", navTags)}\n" +
            $"  keys: {string.Join(", ", SettingsViewModel.NavKeys)}");

        if (ContentScroll?.Content is not System.Windows.Controls.Grid host)
        {
            Debug.Fail("ContentScroll 의 내용이 Grid 가 아닙니다 — 패널 계약을 검사할 수 없습니다.");
            return;
        }

        // Each panel declares its own key as the Visibility binding's ConverterParameter. Read it back out
        // rather than duplicating it into a Tag: the parameter IS the panel's identity, so checking the real
        // thing keeps this honest even if someone adds a panel and forgets everything else.
        string[] panelKeys = host.Children
            .OfType<FrameworkElement>()
            .Select(p => (BindingOperations.GetBinding(p, VisibilityProperty)?.ConverterParameter as string) ?? "<none>")
            .ToArray();

        Debug.Assert(
            panelKeys.Order(StringComparer.Ordinal).SequenceEqual(
                SettingsViewModel.NavKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            $"설정창 패널 키가 SettingsViewModel.NavKeys 와 어긋납니다.\n" +
            $"  panels: {string.Join(", ", panelKeys)}\n" +
            $"  keys  : {string.Join(", ", SettingsViewModel.NavKeys)}");
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

    private void OnApplyGameOpt(object sender, RoutedEventArgs e) => _viewModel.ApplyGameOpt();

    private void OnRevertGameOpt(object sender, RoutedEventArgs e) => _viewModel.RevertGameOpt();

    private void OnOpenFontsFolder(object sender, RoutedEventArgs e) => _viewModel.OpenFontsFolder();

    /// <summary>Brightness commits on drag-end, not per tick — see the slider's comment in the XAML.</summary>
    private void OnNameFxBrightnessCommitted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => _viewModel.CommitNameFxBrightness();

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

    private void OnResetAetherPosition(object sender, RoutedEventArgs e) => _viewModel.ResetAetherPosition();

    private void OnApplyConsent(object sender, RoutedEventArgs e) => RunThenRefresh(_viewModel.ApplyConsent);

    private void OnRefreshConsent(object sender, RoutedEventArgs e) => RunThenRefresh(_viewModel.RefreshConsentFromServer);

    private void OnOpenMyStats(object sender, RoutedEventArgs e) => _viewModel.OpenMyStats();

    /// <summary>티어 갱신. The fetch itself is the tier worker's job — this only nudges it and
    /// re-reads the status line, so the click never blocks the UI thread on a 15s HTTP timeout.</summary>
    private void OnRefreshTier(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.RequestTierRefresh())
        {
            return; // inside the 60s cooldown — silently ignore rather than pretending to work
        }

        _viewModel.RefreshTierStatus();
    }

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
