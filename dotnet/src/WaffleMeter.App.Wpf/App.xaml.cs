using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Wpf;

public partial class App : Application
{
    private MeterEngine? _engine;
    private MeterSettings? _settings;
    private MeterColorTheme? _theme;
    private SkinManager? _skin;
    private UpdateService? _updateService;
    private HotkeyHandler? _hotkeys;
    private OverlayController? _controller;
    private TrayIconController? _tray;
    private DpsReport? _lastReport;
    private DetailWindow? _detailWindow;
    private DetailsViewModel? _detailViewModel;
    private int _detailUid;
    private JoinRequestPanel? _joinPanel;
    private JoinRequestViewModel? _joinViewModel;
    private bool _joinPanelPositioned;
    private HistoryPanel? _historyPanel;
    private BattleHistoryViewModel? _historyViewModel;
    private bool _historyPanelPositioned;
    private bool _historyPanelVisible;
    private bool _viewingHistory;
    private long _historyBaselineBattleStart;
    private readonly HashSet<string> _consentPrompted = new();
    private bool _consentDialogOpen;

    public App()
    {
        // Surface UI-thread exceptions instead of hard-crashing, so a faulty window/binding is
        // diagnosable (and the app survives). Logs next to the exe too.
        DispatcherUnhandledException += (_, args) =>
        {
            TryLogCrash(args.Exception);
            System.Windows.MessageBox.Show(args.Exception.ToString(), "waffle_meter 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => TryLogCrash(args.ExceptionObject as Exception);
    }

    private static void TryLogCrash(Exception? ex)
    {
        if (ex == null)
        {
            return;
        }

        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"{DateTime.Now:o}\n{ex}\n\n");
        }
        catch
        {
            // best effort
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack lifecycle hooks run earlier, in Program.Main (before this App is constructed).
        base.OnStartup(e);

        // Decided overlay render mode: software rendering (no GPU compositing) keeps the overlay off
        // the game's GPU path; WS_EX_NOACTIVATE on the window keeps it from stealing foreground.
        // Mirrors the proven Kotlin prism-sw + NOACTIVATE approach.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var services = new MeterServices(new PropertyHandler());
        TryLoadCatalogs(services);

        // Apply the persisted skin (palette) into Application.Resources before any window is built.
        _skin = new SkinManager(services.Props);
        _skin.ApplyInitial();

        _settings = new MeterSettings(services.Props);
        _theme = new MeterColorTheme(services.Props);
        var viewModel = new OverlayViewModel(services.Version, _settings, _theme);
        var window = new OverlayWindow { DataContext = viewModel };
        LoadPosition(services.Props, window);
        window.Show();

        // Auto-hide / park-present + tray (Kotlin BrowserApp behavior).
        _controller = new OverlayController(window, services.Props);
        _controller.Start();
        if (_settings.TaskbarMode)
        {
            _controller.SetTaskbarMode(true); // restore persisted taskbar/alt-tab mode
        }
        _tray = new TrayIconController(window, _controller, () => Dispatcher.Invoke(ExitApp));
        window.PositionChanged += (left, top) => SavePosition(services.Props, left, top);

        // Global hotkeys (Ctrl+R reset / Ctrl+H visibility / Ctrl+T click-through). Callbacks fire on
        // the listener thread, so marshal window ops to the dispatcher.
        OverlayController controller = _controller;
        _hotkeys = new HotkeyHandler(services.Props)
        {
            OnReset = () => { _viewingHistory = false; _engine?.RequestReset(); }, // clears saved battles + live data (runs on consumer thread)
            OnVisibility = () => Dispatcher.Invoke(controller.ToggleVisibility),
            OnClickThrough = () => Dispatcher.Invoke(() => window.SetClickThrough(!window.ClickThrough)),
        };
        _hotkeys.Start();

        // Right-click overlay -> 설정 / 종료.
        HotkeyHandler hotkeys = _hotkeys;
        MeterSettings settings = _settings;
        MeterColorTheme theme = _theme;
        SkinManager skin = _skin;
        window.SettingsRequested += () =>
        {
            var settingsWindow = new SettingsWindow(new SettingsViewModel(services, settings, theme, skin, controller, hotkeys)) { Owner = window };
            settingsWindow.Show();
        };
        window.ExitRequested += () =>
        {
            // Honor the CloseAction setting (React closeAction): exit / tray-hide / ask-once.
            string action = settings.CloseAction;
            if (action == "tray") { controller.HideToTray(); return; }
            if (action == "exit") { ExitApp(); return; }

            var dlg = new CloseActionDialog { Owner = window };
            dlg.ShowDialog();
            if (dlg.Choice == CloseActionDialog.CloseChoice.Cancel) { return; }
            settings.CloseAction = dlg.Choice == CloseActionDialog.CloseChoice.Tray ? "tray" : "exit"; // remember the choice
            if (dlg.Choice == CloseActionDialog.CloseChoice.Tray) { controller.HideToTray(); }
            else { ExitApp(); }
        };
        window.ResetRequested += () => { _viewingHistory = false; _engine?.RequestReset(); };
        window.ThemeRequested += () => skin.Cycle(); // 테마 버튼: cycle dark → midnight → slate
        window.TaskbarToggleRequested += () =>
        {
            bool next = !controller.TaskbarMode;
            settings.TaskbarMode = next;
            controller.SetTaskbarMode(next);
        };

        // Row click -> open/close the detail window for that player.
        viewModel.SelectionToggled += uid => ToggleDetail(uid, services, window);

        // Party join-request panel (Kotlin JoinRequest family -> React JoinRequestPanel).
        WireJoinPanel(services, window);

        // Battle-history panel (React HistoryPanel): the 기록 header button toggles it.
        WireHistoryPanel(services, window, viewModel);

        // Capture runs in the elevated CaptureHost; the UI connects over the pipe (no admin here).
        // The connect timeout is generous so it tolerates the user answering the UAC prompt.
        // captureBackend setting: "windivert" (default, embedded) or "npcap" (needs Npcap installed).
        string backend = services.Props.GetProperty("captureBackend") ?? "windivert";
        _engine = new MeterEngine(services, new NamedPipeCaptureClient(backend, connectTimeoutMs: 30_000));
        _engine.ReportUpdated += report => Dispatcher.Invoke(() =>
        {
            _lastReport = report;
            // While viewing a saved battle, hold the overlay until a NEW battle begins (React resets the
            // selected history when isInCombat); the detail window still tracks live.
            if (_viewingHistory)
            {
                if (report.BattleStart > _historyBaselineBattleStart)
                {
                    _viewingHistory = false;
                }
                else
                {
                    _detailViewModel?.Refresh(report);
                    return;
                }
            }

            viewModel.Update(report);
            _detailViewModel?.Refresh(report); // live-refresh the open detail window
            MaybePromptConsent(services, window);
        });
        _engine.CaptureError += message => Dispatcher.Invoke(() => viewModel.Status = message);

        CaptureHostLaunch launch = CaptureHostLauncher.EnsureRunning();
        if (launch is CaptureHostLaunch.Declined or CaptureHostLaunch.NotFound or CaptureHostLaunch.Failed)
        {
            viewModel.Status = $"캡처 헬퍼 시작 실패: {launch}";
            return;
        }

        viewModel.Status = "캡처 헬퍼 연결 중…";
        // Start off the UI thread: the pipe connect blocks until the helper's pipe is up.
        Task.Run(() =>
        {
            try
            {
                _engine.Start();
                Dispatcher.Invoke(() => viewModel.Status = "캡처 중");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => viewModel.Status = $"캡처 시작 실패 ({ex.Message})");
            }
        });

        // Background auto-update check (no-op for dev / non-Velopack installs).
        _updateService = new UpdateService(prerelease: false);
        _ = _updateService.CheckAndDownloadAsync(msg => Dispatcher.Invoke(() => viewModel.Status = msg));
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    private static void LoadPosition(PropertyHandler props, Window window)
    {
        string? x = props.GetProperty("uiX") ?? props.GetProperty("windowX");
        string? y = props.GetProperty("uiY") ?? props.GetProperty("windowY");
        if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double left) &&
            double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
        {
            window.Left = left;
            window.Top = top;
        }
    }

    private static void SavePosition(PropertyHandler props, double left, double top)
    {
        props.SetProperty("uiX", left.ToString("0", CultureInfo.InvariantCulture));
        props.SetProperty("uiY", top.ToString("0", CultureInfo.InvariantCulture));
    }

    private void ToggleDetail(int uid, MeterServices services, Window owner)
    {
        if (_lastReport == null)
        {
            return;
        }

        if (_detailWindow != null && _detailUid == uid)
        {
            _detailWindow.Close(); // re-click same row -> close (toggle)
            return;
        }

        _detailWindow?.Close();

        string name = _lastReport.Contributors.FirstOrDefault(c => c.Id == uid)?.Nickname ?? uid.ToString();
        _detailViewModel = new DetailsViewModel(_lastReport, uid, services.Calculator, name, _theme!, _settings!.FontFamily);
        _detailUid = uid;
        _detailWindow = new DetailWindow
        {
            DataContext = _detailViewModel,
            Left = owner.Left + owner.ActualWidth + 8,
            Top = owner.Top,
        };
        _detailWindow.Closed += (_, _) =>
        {
            _detailWindow = null;
            _detailViewModel = null;
            _detailUid = 0;
        };
        _detailWindow.Show();
    }

    private void WireJoinPanel(MeterServices services, OverlayWindow overlay)
    {
        _joinViewModel = new JoinRequestViewModel(_settings!);
        _joinPanel = new JoinRequestPanel { DataContext = _joinViewModel };

        // Build the HWND + assert the overlay ex-style, then park (hidden) until a request arrives.
        _joinPanel.Show();
        _joinPanel.Park();

        // Restore a persisted position; otherwise dock under the meter overlay on first present.
        if (LoadPanelPosition(services.Props, _joinPanel, "joinPanelX", "joinPanelY"))
        {
            _joinPanelPositioned = true;
        }

        _joinPanel.PositionChanged += (left, top) =>
        {
            _joinPanelPositioned = true;
            services.Props.SetProperty("joinPanelX", left.ToString("0", CultureInfo.InvariantCulture));
            services.Props.SetProperty("joinPanelY", top.ToString("0", CultureInfo.InvariantCulture));
        };
        _joinPanel.CloseRequested += () => _joinPanel.Park();

        void PresentJoinPanel()
        {
            if (!_joinPanelPositioned)
            {
                _joinPanel.Left = overlay.Left;
                _joinPanel.Top = overlay.Top + overlay.ActualHeight + 8;
            }

            _joinPanel.Present(true);
        }

        // Auto-open on the empty -> non-empty transition (web isOpen behavior).
        _joinViewModel.RequestPresent += PresentJoinPanel;

        // 계정/파티 신청 header button: toggle the panel manually (Opacity tracks park/present).
        overlay.JoinRequested += () =>
        {
            if (_joinPanel.Opacity > 0)
            {
                _joinPanel.Park();
            }
            else
            {
                PresentJoinPanel();
            }
        };

        // Store events fire on the meter-consumer thread; marshal to the UI.
        services.JoinRequests.Changed += () => Dispatcher.Invoke(() => _joinViewModel.Reconcile(services.JoinRequests.Snapshot()));
        services.JoinRequests.Cleared += () => Dispatcher.Invoke(() =>
        {
            _joinViewModel.Clear();
            _joinPanel.Park();
        });
    }

    private void WireHistoryPanel(MeterServices services, OverlayWindow overlay, OverlayViewModel meterViewModel)
    {
        _historyViewModel = new BattleHistoryViewModel(_theme!, _settings!);
        _historyPanel = new HistoryPanel { DataContext = _historyViewModel };
        _historyPanel.Show();
        _historyPanel.Park();

        if (LoadPanelPosition(services.Props, _historyPanel, "historyPanelX", "historyPanelY"))
        {
            _historyPanelPositioned = true;
        }

        _historyPanel.PositionChanged += (left, top) =>
        {
            _historyPanelPositioned = true;
            services.Props.SetProperty("historyPanelX", left.ToString("0", CultureInfo.InvariantCulture));
            services.Props.SetProperty("historyPanelY", top.ToString("0", CultureInfo.InvariantCulture));
        };
        _historyPanel.CloseRequested += () =>
        {
            _historyPanelVisible = false;
            _historyPanel.Park();
        };

        // Saved-battle snapshots arrive on the consumer thread; cache them on the UI thread.
        services.BattleListChanged += battles => Dispatcher.Invoke(() => _historyViewModel.SetBattles(battles));

        // Clicking a saved battle replays it in the meter until the next live battle starts.
        _historyViewModel.BattleSelected += report =>
        {
            _viewingHistory = true;
            _historyBaselineBattleStart = _lastReport?.BattleStart ?? 0;
            meterViewModel.Update(report);
        };

        // The 기록 header button toggles the panel.
        overlay.HistoryRequested += () =>
        {
            if (_historyPanelVisible)
            {
                _historyPanelVisible = false;
                _historyPanel.Park();
                return;
            }

            if (!_historyPanelPositioned)
            {
                _historyPanel.Left = overlay.Left + overlay.ActualWidth + 8;
                _historyPanel.Top = overlay.Top;
            }

            _historyPanelVisible = true;
            _historyPanel.Present(true);
        };
    }

    /// <summary>Show the stats-consent modal once per detected character that has no decision yet
    /// (React StatsConsentModal). Runs on the UI thread from the report loop; remembers prompted hashes
    /// so it never re-pops in the same session.</summary>
    private void MaybePromptConsent(MeterServices services, Window owner)
    {
        if (_consentDialogOpen || !services.Consent.NeedsConsentPrompt())
        {
            return;
        }

        string? hash = services.Consent.CurrentCharacterHash();
        if (hash == null || !_consentPrompted.Add(hash))
        {
            return;
        }

        StatsOwnCharacter own = services.StatsBuilder.OwnCharacter();
        string label = !string.IsNullOrEmpty(own.Nickname)
            ? own.Nickname + (string.IsNullOrEmpty(own.Job) ? string.Empty : $" · {own.Job}")
            : "내 캐릭터";

        _consentDialogOpen = true;
        try
        {
            var dlg = new StatsConsentModal(label) { Owner = owner };
            dlg.ShowDialog();
            if (dlg.Accepted)
            {
                services.Consent.Set("accepted", uploadEnabled: true, publicCharacter: dlg.PublicCharacter, services.Version);
            }
            else
            {
                services.Consent.Set("declined", uploadEnabled: false, publicCharacter: false, services.Version);
            }
        }
        finally
        {
            _consentDialogOpen = false;
        }
    }

    private static bool LoadPanelPosition(PropertyHandler props, Window panel, string xKey, string yKey)
    {
        if (double.TryParse(props.GetProperty(xKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double left) &&
            double.TryParse(props.GetProperty(yKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
        {
            panel.Left = left;
            panel.Top = top;
            return true;
        }

        return false;
    }

    private static void TryLoadCatalogs(MeterServices services)
    {
        string jsonDir = Path.Combine(AppContext.BaseDirectory, "json");
        if (!Directory.Exists(jsonDir))
        {
            return;
        }

        try
        {
            services.LoadCatalogs(jsonDir);
        }
        catch
        {
            // run with empty catalogs; the overlay still shows
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
