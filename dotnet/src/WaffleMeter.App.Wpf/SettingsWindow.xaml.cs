using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            _viewModel.RefreshNameFxStatus();
            // 캐릭터가 바뀌었거나 명단이 새로 도착했을 때만 픽커를 다시 만든다. 창을 연 뒤에 인식되면
            // 예전에는 픽커가 죽은 채로 남아 있었고, '후원자 목록 갱신' 으로 자격을 받아도 반영되지 않았다.
            _viewModel.RefreshMyNameFxIfStale();
        };
        _statusTimer.Start();
        _viewModel.RefreshTierStatus(); // fill the line before the first tick so it never opens blank
        _viewModel.RefreshNameFxStatus();
        // 자격은 명단이 바뀌거나 캐릭터가 바뀌면 달라진다. 창을 열 때 한 번 읽고, 그 뒤엔 선택
        // 직후에만 다시 읽는다 — 2.5초 폴링에 얹으면 고르는 도중에 목록이 갈릴 수 있다.
        _viewModel.RefreshMyNameFx();
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

    private void OnOpenCooldownPicker(object sender, RoutedEventArgs e) => _viewModel.OpenCooldownPicker();

    private void OnApplyGameOpt(object sender, RoutedEventArgs e) => _viewModel.ApplyGameOpt();

    private void OnRevertGameOpt(object sender, RoutedEventArgs e) => _viewModel.RevertGameOpt();

    private void OnOpenFontsFolder(object sender, RoutedEventArgs e) => _viewModel.OpenFontsFolder();

    private void OnExportFull(object sender, RoutedEventArgs e) => _viewModel.ExportFull();

    private void OnExportDesign(object sender, RoutedEventArgs e) => _viewModel.ExportDesign();

    private void OnExportAlarms(object sender, RoutedEventArgs e) => _viewModel.ExportAlarms();

    private void OnOpenBackupFolder(object sender, RoutedEventArgs e) => _viewModel.OpenBackupFolder();

    private void OnSaveCodeToFile(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LastExportedCode.Length == 0)
        {
            MessageBox.Show(this, "먼저 위에서 어떤 범위로 코드를 만들지 골라 주세요.",
                "설정 파일로 저장", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _viewModel.SuggestedFileName(DateTimeOffset.Now),
            DefaultExt = ".wmset",
            Filter = "와플미터 설정 (*.wmset)|*.wmset|텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _viewModel.LastExportedCode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "파일을 저장하지 못했습니다. " + ex.Message,
                "설정 파일로 저장", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLoadCodeFromFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".wmset",
            Filter = "와플미터 설정 (*.wmset)|*.wmset|텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // 파일 전체를 그대로 넘긴다 — 코드를 잘라내는 건 파서의 일이고, 그래야 메모장에 메모를 곁들여
            // 저장해 둔 파일도 그대로 열린다.
            _viewModel.LoadCodeFromText(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "파일을 읽지 못했습니다. " + ex.Message,
                "설정 불러오기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPasteImport(object sender, RoutedEventArgs e)
    {
        string? code = _viewModel.ClipboardCode();
        if (code is null)
        {
            MessageBox.Show(this, "클립보드에 설정 코드가 없습니다. 'WM1.' 으로 시작하는 코드를 복사한 뒤 다시 눌러 주세요.",
                "설정 가져오기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _viewModel.ImportText = code;
        _viewModel.PreviewPastedCode();
    }

    private void OnPreviewImport(object sender, RoutedEventArgs e) => _viewModel.PreviewPastedCode();

    private void OnApplyImport(object sender, RoutedEventArgs e) => _viewModel.ApplyPreviewedCode();

    private void OnUndoImport(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "가장 최근 가져오기 직전의 설정으로 되돌립니다. 그 뒤에 바꾼 설정은 사라집니다. 계속할까요?",
                "설정 되돌리기", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            _viewModel.UndoLastImport();
        }
    }

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

    private void OnCopyStatSheet(object sender, RoutedEventArgs e)
    {
        // 클립보드는 다른 프로세스가 잡고 있으면 실패할 수 있다. 버튼 글자를 잠깐 바꿔 성공/실패를 알린다 —
        // 모달을 띄우면 "복사"라는 사소한 동작에 확인 클릭이 하나 더 붙는다.
        if (sender is not Button button) return;

        object? original = button.Content;
        button.Content = _viewModel.CopyStatSheet() ? "복사했습니다" : "복사 실패";
        // 우선순위를 명시한다 — 인자 없는 DispatcherTimer 는 Background 로 떨어져 바쁜 디스패처에서 굶고,
        // 그러면 버튼이 "복사했습니다" 상태로 남는다.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1.6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = original;
        };
        timer.Start();
    }

    private void OnOpenCalculator(object sender, RoutedEventArgs e) => _viewModel.OpenCalculator();

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

    /// <summary>후원자 목록 갱신. Nudges the worker and re-reads the status line — the click never blocks the
    /// UI thread on an HTTP timeout. Unlike 티어 갱신 above, a swallowed click says so — a button that looks
    /// dead is the thing someone reports, and this one is pressed right after donating.</summary>
    private void OnPickMyNameFx(object sender, RoutedEventArgs e) => PickMyNameFx(sender, gauge: false);

    private void OnPickMyGauge(object sender, RoutedEventArgs e) => PickMyNameFx(sender, gauge: true);

    /// <summary>
    /// 내 연출을 서버에 반영한다.
    /// <para>네트워크 호출이라 UI 스레드에서 부르지 않는다 — 8초 연결 + 15초 읽기 타임아웃이라
    /// 그대로 부르면 설정창이 최대 23초 얼어붙는다.</para>
    /// </summary>
    private void PickMyNameFx(object sender, bool gauge)
    {
        if (((FrameworkElement)sender).Tag is not string id)
        {
            return;
        }

        // 게이지만 바꾸는 요청에도 닉네임 효과를 같이 실어야 한다(서버는 effectId 를 필수로 받는다).
        // ⚠ 예전의 `CurrentEffectId ?? id` 폴백은 **게이지 id 를 닉네임 효과 자리에 넣는다** — 서버는 두 계열을
        // 엄격히 가르므로 그 요청은 400 effect_not_allowed 가 확정이고, 사용자에게는 "게이지만 안 바뀐다" 로 보인다.
        // 현재 효과를 모르면 보내지 않고 왜 못 보내는지 말한다.
        string? currentEffect = _viewModel.CurrentEffectId;
        if (gauge && currentEffect is null)
        {
            _viewModel.SetMyFxNotice("닉네임 스킨을 먼저 고른 뒤에 게이지 스킨을 골라 주세요.");
            return;
        }

        // 결과는 픽커 **바로 아래** 줄에 남긴다. 예전에는 맨 아래 '후원자 목록 갱신' 줄로 보냈는데, 그 사이에
        // 미리보기 카드 9장과 슬라이더 네 개가 있어 누른 사람 화면에서는 사유가 스크롤 밖이었다 — 실패가
        // 조용해지면 "눌리기만 하고 아무 일도 안 난다" 로만 보인다.
        _viewModel.SetMyFxNotice("적용하는 중…");
        System.Threading.Tasks.Task.Run(() =>
        {
            string message = gauge
                ? _viewModel.SubmitMyNameFx(currentEffect!, id)
                : _viewModel.SubmitMyNameFx(id, null);

            Dispatcher.BeginInvoke(() =>
            {
                _viewModel.SetMyFxNotice(message);
                _viewModel.RefreshMyNameFx(keepNotice: true); // 방금 띄운 사유를 다시 계산해서 덮지 않는다
            });
        });
    }

    private void OnRefreshNameFx(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.RequestNameFxRefresh())
        {
            _viewModel.SetNameFxNotice("잠시 뒤에 다시 눌러 주세요 (1분에 한 번).");
            return;
        }

        _viewModel.SetNameFxNotice("후원자 목록을 받아 오는 중…");
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
