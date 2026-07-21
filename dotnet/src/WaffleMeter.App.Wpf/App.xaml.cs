using System.Globalization;
using System.IO;
using System.Threading;
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
    private OverlayWindow? _overlayWindow;
    private DpsReport? _lastReport;
    private DetailWindow? _detailWindow;
    private DetailsViewModel? _detailViewModel;
    private int _detailUid;
    private JoinRequestPanel? _joinPanel;
    private JoinRequestViewModel? _joinViewModel;
    private bool _joinPanelPositioned;
    private bool _joinUserDismissed;                         // user closed the panel — suppress auto-show…
    private readonly HashSet<int> _joinDismissedIds = new(); // …until a requester NOT in this set applies (option a)
    private SkillSettingsFlyout? _skillFlyout;
    private bool _skillFlyoutVisible;
    private SettingsWindow? _settingsWindow; // single instance; the ⚙ button toggles it (open/close), not stacks
    private ReplayWindow? _replayWindow; // single instance; the tray item toggles it (open/close)
    private HistoryPanel? _historyPanel;
    private BattleHistoryViewModel? _historyViewModel;
    private bool _historyPanelPositioned;
    private bool _historyPanelVisible;
    private bool _viewingHistory;
    private long _historyBaselineBattleStart;
    // Pre-combat party preview: the roster = recent boss-combat contributors (the party). Combat is the only
    // reliable party signal — a 0x3645 nickname snapshot fires for EVERY nearby player, so in town that lists
    // strangers. A member fades after this long with no combat (leaving the party / lingering in town).
    private const long PreCombatPartyTtlMs = 300_000; // 5 min
    private readonly Dictionary<int, long> _partyLastCombatMs = new();
    private readonly HashSet<string> _consentPrompted = new();
    private bool _consentDialogOpen;
    private int _lastConsentBackfillId; // executor uid whose name was last persisted into its consent record
    private UpdateToast? _updateToast;
    private UpdateToastViewModel? _updateToastVm;
    private AlarmToast? _alarmToast;
    private AlarmToastViewModel? _alarmToastVm;
    private AlarmController? _alarms;
    private volatile bool _combatActive; // recent damage activity — gates the "mute field-boss alarm in combat" option
    private BuffOverlayPanel? _buffOverlay;
    private BuffOverlayViewModel? _buffOverlayVm;
    private BuffPresetManager? _buffPresets;
    private System.Windows.Threading.DispatcherTimer? _buffTimer;

    /// <summary>Auto-reset event the FIRST instance owns (set by <see cref="Program"/>); a later launch
    /// opens it by name and signals it instead of spawning a colliding UI. We wait on it and surface the
    /// overlay (un-hide from tray) so relaunching the shortcut brings the running instance back.</summary>
    public EventWaitHandle? SingleInstanceShowSignal { get; set; }

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

        var props = new PropertyHandler();
        // Overlay render mode. Default = software (no GPU compositing): keeps the overlay off the game's
        // GPU path (with WS_EX_NOACTIVATE it also never steals foreground) AND is the friendliest to
        // variable-refresh-rate (FreeSync/G-Sync) displays, where a GPU-composited transparent overlay can
        // break the game's flip/independent-present path and cause stutter. A user whose setup does better
        // with GPU rendering can turn the compat mode off. Process-global + read once → needs a restart.
        bool vrrCompat = props.GetProperty("vrrCompatMode") != "false";
        RenderOptions.ProcessRenderMode = vrrCompat ? RenderMode.SoftwareOnly : RenderMode.Default;

        // Text stays on WPF's default TextFormattingMode.Ideal. Display mode would grid-fit the glyphs and
        // visibly sharpen the 11-13 px UI text, but it also quantizes glyph advances to whole pixels, so every
        // bundled font collapses onto the same horizontal rhythm — measured, the four bundled families grow
        // 13% more alike, and moving one font from Ideal to Display shifts it about as far as swapping it for
        // a different family. Keeping the typeface's own metrics is the deliberate choice here.
        //
        // Nor is there a way to soften the antialiasing instead: TextRenderingMode.Aliased and
        // TextHintingMode.Fixed are both silently ignored under Ideal (verified — pixel-identical output),
        // and Animated hinting only smears further. The one lever that sharpens without touching the
        // typeface is snapping the text origin to a whole pixel, i.e. UseLayoutRounding on the meter
        // overlay (OverlayWindow.xaml) — it cut the overlay's partially-covered glyph pixels from 33% to
        // 29%. The settings window's origins already land on whole pixels, so it gains nothing there.

        var services = new MeterServices(props);
        TryLoadCatalogs(services);

        // Apply the persisted skin (palette) into Application.Resources before any window is built.
        _skin = new SkinManager(services.Props);
        _skin.ApplyInitial();

        _settings = new MeterSettings(services.Props);
        _theme = new MeterColorTheme(services.Props);
        SkinManager skinManager = _skin;
        var viewModel = new OverlayViewModel(services.Version, _settings, _theme, () => skinManager.IsLight);
        skinManager.Changed += viewModel.RefreshSkin; // re-theme stat colors on light/dark swap
        var window = new OverlayWindow { DataContext = viewModel };
        LoadPosition(services.Props, window);
        // The meter auto-sizes its HEIGHT to the row count (SizeToContent=Height) so no scrollbar appears;
        // only WIDTH is user-resizable + persisted.
        LoadWindowWidth(services.Props, "meterWidth", window);
        window.Show();
        _overlayWindow = window;
        AttachScreenClamp(window);
        ClampWhenLoaded(window); // pull a stale/off-screen restored position back onto a live monitor
        AttachResize(window, services.Props, "meterWidth", "meterHeight", widthOnly: true);
        // Snap all windows back onto a monitor the moment multi-monitor movement is turned off.
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeterSettings.MultiMonitorMode) && !_settings.MultiMonitorMode)
            {
                Dispatcher.BeginInvoke(ClampAllWindows);
            }
        };

        // Re-clamp every window onto a live monitor when the display topology changes (a monitor
        // unplugged / resolution or arrangement change can otherwise strand a window off the desktop).
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Auto-hide / park-present + tray (Kotlin BrowserApp behavior).
        _controller = new OverlayController(window, services.Props);
        _controller.Start();
        if (_settings.TaskbarMode)
        {
            _controller.SetTaskbarMode(true); // restore persisted taskbar/alt-tab mode
        }
        _tray = new TrayIconController(window, _controller, () => Dispatcher.Invoke(ExitApp),
            services.Movement != null ? () => OpenReplay(services, window) : null,
            DevPacketLogReplay.IsAvailable(VersionConfig.Resolve().Version) ? () => LoadPacketLog(services) : null);
        window.PositionChanged += (left, top) => SavePosition(services.Props, left, top);

        // Single-instance: surface this (running) instance when a later launch signals us, so relaunching
        // the shortcut un-hides the overlay instead of spawning a second UI that would collide on the pipe.
        StartSingleInstanceListener();

        // Global hotkeys (Ctrl+R reset / Ctrl+H visibility / Ctrl+T click-through). Callbacks fire on
        // the listener thread, so marshal window ops to the dispatcher.
        OverlayController controller = _controller;
        _hotkeys = new HotkeyHandler(services.Props)
        {
            OnReset = () => { _viewingHistory = false; _engine?.RequestReset(); }, // clears saved battles + live data, keeps recognized characters (consumer thread)
            OnVisibility = () => Dispatcher.Invoke(controller.ToggleVisibility),
            OnClickThrough = () => Dispatcher.Invoke(() =>
            {
                window.SetClickThrough(!window.ClickThrough);
                _buffOverlay?.SetClickThrough(window.ClickThrough); // buff overlay follows the meter at once
            }),
            // 허수아비 mode toggle marshals to the UI thread (it raises PropertyChanged that WPF bindings read);
            // the reset only flips a volatile flag on the engine, so it's fine straight off the listener thread.
            OnDummyToggle = () => Dispatcher.Invoke(() => _settings.DummyTestMode = !_settings.DummyTestMode),
            OnDummyReset = () => _engine?.RequestDummyReset(),
        };
        _hotkeys.Start();

        // Right-click overlay -> 설정 / 종료.
        HotkeyHandler hotkeys = _hotkeys;
        MeterSettings settings = _settings;
        MeterColorTheme theme = _theme;
        SkinManager skin = _skin;
        window.SettingsRequested += () =>
        {
            // Toggle like the other panels (전투 기록 / 파티 신청): a second press on the ⚙ button closes the
            // open window instead of stacking another one. The window nulls the field on close (✕ / Esc / Alt+F4),
            // so the next press reopens a fresh instance.
            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                return;
            }

            // _buffPresets is assigned later in OnStartup, well before the overlay exists to raise this.
            var svm = new SettingsViewModel(services, settings, theme, skin, controller, hotkeys, _buffPresets!);
            svm.CheckUpdateRequested = () => _ = _updateService?.CheckAndDownloadAsync(msg => Dispatcher.Invoke(() => viewModel.Status = msg));
            svm.ResetPositionRequested = which => ResetPanelPosition(which, services, window);
            svm.PlayReplayRequested = () => PlayReplayFromPicker(services, window);
            svm.DummyResetRequested = () => _engine?.RequestDummyReset(); // 허수아비 DPS 초기화 button (settings tab)
            var settingsWindow = new SettingsWindow(svm) { Owner = window };
            LoadWindowSize(services.Props, "settingsWidth", "settingsHeight", settingsWindow);
            settingsWindow.SizeChanged += (_, _) =>
            {
                services.Props.SetProperty("settingsWidth", settingsWindow.ActualWidth.ToString("0", CultureInfo.InvariantCulture));
                services.Props.SetProperty("settingsHeight", settingsWindow.ActualHeight.ToString("0", CultureInfo.InvariantCulture));
            };
            settingsWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, settingsWindow))
                {
                    _settingsWindow = null;
                }
            };
            _settingsWindow = settingsWindow;
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
        // 허수아비 테스트 header button: flip the one shared setting (mirrored live onto the capture gate above);
        // the header icon's accent state follows via a DataTrigger bound to Settings.DummyTestMode.
        window.DummyTestToggleRequested += () => settings.DummyTestMode = !settings.DummyTestMode;

        // Row click -> open/close the detail window for that player.
        viewModel.SelectionToggled += uid => ToggleDetail(uid, services, window, viewModel);

        // Party join-request panel (Kotlin JoinRequest family -> React JoinRequestPanel).
        WireJoinPanel(services, window);

        // Battle-history panel (React HistoryPanel): the 기록 header button toggles it.
        WireHistoryPanel(services, window, viewModel);

        // Capture runs in the elevated CaptureHost; the UI connects over the pipe (no admin here).
        // EnsureServing (below) already launches the helper, absorbs any UAC prompt, and WAITS for the
        // pipe to appear before we connect — so by connect time a healthy helper accepts in milliseconds.
        // The connect budget is therefore modest: a longer wait would only prolong the failure when the
        // single serve-once pipe is already OCCUPIED by another (e.g. pre-guard/old-build) instance.
        // captureBackend setting: "windivert" (default, embedded) or "npcap" (needs Npcap installed).
        string backend = services.Props.GetProperty("captureBackend") ?? "windivert";
        _engine = new MeterEngine(services, new NamedPipeCaptureClient(backend, connectTimeoutMs: 10_000));
        // Frame-drop relief: apply the persisted refresh interval, and keep it in sync live. Low-spec mode
        // pins it (EffectiveRefreshIntervalMs); the slider is otherwise honored.
        MeterEngine engine = _engine;
        _engine.ReportIntervalMs = _settings.EffectiveRefreshIntervalMs;
        // 허수아비 test mode: seed the capture pipeline from the persisted setting, then mirror live changes (the
        // header toggle / settings tab / hotkey all write MeterSettings — one source of truth) onto the gate.
        services.Data.DummyTestMode = _settings.DummyTestMode;
        services.Data.DummyDurationSec = _settings.DummyDurationSec;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MeterSettings.RefreshIntervalMs) or nameof(MeterSettings.LowSpecMode))
            {
                engine.ReportIntervalMs = _settings.EffectiveRefreshIntervalMs;
            }
            else if (e.PropertyName == nameof(MeterSettings.DummyTestMode))
            {
                services.Data.DummyTestMode = _settings.DummyTestMode;
            }
            else if (e.PropertyName == nameof(MeterSettings.DummyDurationSec))
            {
                services.Data.DummyDurationSec = _settings.DummyDurationSec;
            }
        };
        _engine.ReportUpdated += report => Dispatcher.Invoke(() =>
        {
            _lastReport = report;
            // Combat-active = recent damage; a few seconds of grace covers the gaps between hits in a fight.
            // Set from the LIVE report before any early-return (history replay) so the field-boss "mute in
            // combat" gate always reflects real combat, not the displayed (frozen) battle.
            _combatActive = report.Information.Count > 0
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - report.BattleEnd < 5000;
            // While viewing a saved battle, hold the overlay until a NEW battle begins (React resets the
            // selected history when isInCombat); the open detail follows the SAME displayed battle (below).
            if (_viewingHistory)
            {
                if (report.BattleStart > _historyBaselineBattleStart)
                {
                    _viewingHistory = false;
                }
                else
                {
                    // Still replaying a saved battle: the overlay stays frozen on it, so refresh the open
                    // detail against the SAME displayed (saved) report. Refreshing with the LIVE `report`
                    // here is what made a detail opened on a history row blank out into a raw-uid title +
                    // all-zero stats once the live battle moved on.
                    _detailViewModel?.Refresh(viewModel.CurrentReport ?? report);
                    return;
                }
            }

            // Pre-combat party preview: remember everyone dealing damage to the boss with me (the party) and
            // feed them as the idle roster, so a fresh dungeon entry shows the party — not every nearby player
            // (a nickname snapshot fires for all nearby players, which in town is strangers). OverlayViewModel
            // only merges this while idle, so combat rows are untouched. Only live reports reach here (history
            // replay returns above).
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (int combatUid in report.Information.Keys)
            {
                _partyLastCombatMs[combatUid] = nowMs;
            }
            int execUid = services.Data.ExecutorId();
            // Authoritative party from the 0x9702 roster packet (fires on party formation, so the party
            // shows on dungeon entry BEFORE any combat), unioned with recent boss-combat contributors as a
            // fallback (covers a party seen only in combat / before its roster snapshot arrives). Dedup by
            // uid, executor first then power desc.
            var rosterById = new Dictionary<int, User>();
            // The 0x9702-only party (no combat-contributor fallback): the party-context guard for lost-executor
            // recovery needs the AUTHORITATIVE party, because at a field boss the fallback below would fold the
            // zerg into the display roster and defeat the guard.
            List<User> authoritativeParty = services.Data.PartyRoster(PreCombatPartyTtlMs).ToList();
            foreach (User member in authoritativeParty)
            {
                rosterById[member.Id] = member;
            }
            foreach (KeyValuePair<int, long> kv in _partyLastCombatMs)
            {
                if (nowMs - kv.Value > PreCombatPartyTtlMs || rosterById.ContainsKey(kv.Key))
                {
                    continue;
                }

                User? u = services.Data.User(kv.Key);
                if (u != null && !string.IsNullOrWhiteSpace(u.Nickname))
                {
                    rosterById[kv.Key] = u;
                }
            }

            // Did a real party source (0x9702 roster / recent combat) report anyone? If not, the only thing in the
            // preview is the self-injection below — a purely solo preview we suppress (see the solo filter). In a
            // dungeon the 0x9702 packet fires (even a party-of-1), so this is true there and self shows.
            bool hasPartySource = rosterById.Count > 0;

            // Pin the recognized 본인 so the local player shows in the pre-combat preview even when the 0x9702
            // roster omits self (party packets often exclude the local player) or its name+server hasn't matched
            // a uid yet. Dedup by uid: if self already arrived via 0x9702 or combat, keep that object (it may
            // carry a better server/power). Self sorts first via the executor-first OrderBy below.
            if (execUid != 0 && !rosterById.ContainsKey(execUid))
            {
                User? self = services.Data.User(execUid);
                if (self != null && !string.IsNullOrWhiteSpace(self.Nickname))
                {
                    rosterById[execUid] = self;
                }
            }

            List<User> partyRoster = rosterById.Values
                .OrderByDescending(u => u.Id == execUid)
                .ThenByDescending(u => u.Power)
                .ToList();
            // Dedup by character identity (nickname+server): 본인 can persist under an OLD uid (e.g. before a
            // town→dungeon re-instance, kept since reset preserves users) AND the current executor uid — both
            // same nickname+server — so the 0x9702 name-match + self-inject would list 본인 twice. Keep the first;
            // the executor sorts first, so 본인 keeps its executor uid (self-coloring). Also collapses any other
            // same-character-different-uid duplicate across the roster sources.
            var seenIdentity = new HashSet<(string, int)>();
            partyRoster = partyRoster
                .Where(u => string.IsNullOrWhiteSpace(u.Nickname) || seenIdentity.Add((u.Nickname!, u.Server)))
                .ToList();
            // Suppress a PURELY self-injected solo preview (no party source) — e.g. in town right after a reset,
            // where the party roster was cleared but self is still recognized. In a dungeon, 0x9702 fires (even a
            // party-of-1), so hasPartySource is true and self still shows while waiting for members to join.
            if (!hasPartySource && partyRoster.Count == 1 && partyRoster[0].Id == execUid)
            {
                partyRoster.Clear();
            }

            viewModel.SetAuthoritativeParty(authoritativeParty); // 0x9702-only; the party-context guard for self-recovery
            viewModel.SetRoster(partyRoster);
            viewModel.Update(report);
            (int aBase, int aBonus, int _, bool aHas) = services.Data.CurrentAether;
            viewModel.SetAether(aBase, aBonus, aHas); // live aether badge (read each tick; fires even when idle)
            (int sBase, int sBonus, int _, bool sHas) = services.Data.CurrentShugoKey;
            viewModel.SetShugoKey(sBase, sBonus, sHas); // live shugo-festa key badge
            viewModel.SetPing(services.CurrentPing()); // live server-latency badge
            _detailViewModel?.Refresh(report); // live-refresh the open detail window
            StatsOwnCharacter own = services.StatsBuilder.OwnCharacter();
            // Pass the executor's known job (from its User) so the VM can recover 본인 when it re-instances and
            // its new id's own-load packet (0x3633) is missing — see OverlayRowBuilder lost-executor recovery.
            JobClass? ownJob = own.Detected ? services.Data.User(own.Id)?.Job : null;
            viewModel.SetRecognized(own.Detected, own.Nickname, own.Id, own.Server, ownJob, own.Power);
            // Persist the connected character's display name into its consent record (local only) once per
            // recognized character, so the '내 캐릭터 관리' list shows the real name instead of "이름 없음".
            if (own.Detected && own.Id != _lastConsentBackfillId)
            {
                _lastConsentBackfillId = own.Id;
                services.Consent.BackfillCurrentCharacterIdentity();
            }

            MaybePromptConsent(services, window);
        });
        _engine.CaptureError += message => Dispatcher.Invoke(() => viewModel.Status = CaptureErrorMessage(message));
        // A reset clears the data-layer party roster; also drop the UI-side recent-combat party tracker so a stale
        // party (e.g. after returning to town) doesn't re-preview on reset. Fires before the cleared report, so
        // there's no one-frame flash of the old party.
        _engine.ResetCompleted += () => Dispatcher.Invoke(() => _partyLastCombatMs.Clear());
        // A character switch (a DIFFERENT character connects) likewise drops the UI-side recent-combat tracker so
        // the previous character doesn't linger as a stale 0/s idle preview row under the new one (the data layer
        // drops its 0x9702 roster snapshot in lockstep). Mirrors the ResetCompleted ordering — queued from the
        // consumer thread before the next idle report, so there's no one-frame flash.
        _engine.ExecutorChanged += () => Dispatcher.Invoke(() => _partyLastCombatMs.Clear());

        viewModel.Status = "캡처 헬퍼 시작 중…";
        // Launch + connect entirely off the UI thread. EnsureServing registers/triggers the elevated helper
        // and WAITS for its pipe to actually appear: schtasks /run reports success when the task is merely
        // triggered, so a VPN/booster or AV that silently blocks the (unsigned, elevated) helper would
        // otherwise surface only as a 30s pipe-connect timeout. If the no-prompt task yields no pipe,
        // EnsureServing escalates to a user-approved runas (harder to block) before reporting Blocked.
        Task.Run(() =>
        {
            CaptureHostLaunch launch = CaptureHostLauncher.EnsureServing();
            if (launch is CaptureHostLaunch.Declined or CaptureHostLaunch.NotFound
                or CaptureHostLaunch.Failed or CaptureHostLaunch.Blocked)
            {
                Dispatcher.Invoke(() => viewModel.Status = CaptureLaunchMessage(launch));
                return;
            }

            Dispatcher.Invoke(() => viewModel.Status = "캡처 헬퍼 연결 중…");
            try
            {
                _engine.Start();
                Dispatcher.Invoke(() => viewModel.Status = "캡처 중");
            }
            catch (Exception ex)
            {
                // A helper pipe was already being served before we launched (AlreadyRunning) yet we still
                // can't connect → another waffle_meter is almost certainly occupying the single serve-once
                // helper. Surface that actionable cause instead of a raw connect error (covers an old,
                // pre-single-instance-guard build still running, or a cross-session instance).
                bool occupiedByOther = launch == CaptureHostLaunch.AlreadyRunning
                    && ex.Message.Contains("occupied", StringComparison.OrdinalIgnoreCase);
                string status = occupiedByOther
                    ? "다른 waffle_meter가 이미 실행 중인 것 같아요. 트레이의 기존 창을 쓰거나 종료한 뒤 다시 시작해 주세요."
                    : $"캡처 시작 실패 ({ex.Message})";
                Dispatcher.Invoke(() => viewModel.Status = status);
            }
        });

        // Background auto-update check (no-op for dev / non-Velopack installs) — surfaced via the toast.
        _updateToastVm = new UpdateToastViewModel();
        _updateToast = new UpdateToast { DataContext = _updateToastVm };
        _updateToast.Show();
        _updateToast.Park();
        _controller?.RegisterOverlay(_updateToast);
        _updateToast.CloseRequested += () => _updateToast.Park();

        // One-time post-update patch-note popup: the first launch after updating to a NEW version shows that
        // version's notes once. Deferred to ApplicationIdle so it appears after the overlay has settled; the
        // method records the version (a fresh install / first run with this feature is recorded silently, not
        // shown) and never throws into startup.
        Dispatcher.BeginInvoke(new Action(() => MaybeShowPatchNotes(services.Version)),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 슈고 페스타 (top-of-hour event) reminder: a transient toast + an app-scoped clock that fires it.
        _alarmToastVm = new AlarmToastViewModel();
        _alarmToast = new AlarmToast { DataContext = _alarmToastVm };
        _alarmToast.Show();
        _alarmToast.Park();
        _controller?.RegisterOverlay(_alarmToast);
        _alarmToast.CloseRequested += () => _alarmToast.Park();
        _alarms = new AlarmController(
            _settings,
            lead => Dispatcher.Invoke(() => ShowShugoAlarm(lead)),
            alarm => Dispatcher.Invoke(() => ShowCustomAlarm(alarm)),
            fieldBossTimers: () => services.Data.CurrentFieldBossTimers,
            onFieldBoss: due => Dispatcher.Invoke(() => ShowFieldBossAlarm(due)),
            combatActive: () => _combatActive);
        _alarms.Start();

        // Per-job buff picker: seed the observed catalog + hidden selection from persisted settings, and
        // persist the growing catalog back as new buffs are seen.
        services.Data.SeedObservedBuffBases(MeterSettings.ParseCodeSet(_settings.BuffUiObserved));
        HashSet<int> hidden = MeterSettings.ParseCodeSet(_settings.BuffUiHidden);
        if (!_settings.BuffUiDefaultsApplied)
        {
            // First run: hide the catalog's toggle/aura buffs (질주의 진언 / 불패의 진언 등) by default — they
            // stay on indefinitely, so they're noise in the overlay until the user opts them back in.
            foreach (int c in services.Data.DefaultOffBuffBases())
            {
                hidden.Add(c);
            }

            _settings.BuffUiHidden = string.Join(",", hidden);
            _settings.BuffUiDefaultsApplied = true;
        }

        services.Data.SetHiddenBuffBases(hidden);
        services.Data.SetVoiceBuffBases(MeterSettings.ParseCodeSet(_settings.BuffUiVoice)); // 음성만/오버레이+음성 buffs the store must keep
        services.Data.BuffCatalogChanged += () => Dispatcher.BeginInvoke(() =>
            _settings.BuffUiObserved = string.Join(",", services.Data.ObservedBuffBases()));

        // Buff presets: three saved copies of the whole buff config, the active one mirroring the live
        // settings. Built strictly AFTER the default-off merge above — seeded any earlier, slot 1 would
        // capture a hidden set the merge is about to change, and diverge from the running config.
        _buffPresets = new BuffPresetManager(_settings, services.Data.SetHiddenBuffBases, services.Data.SetVoiceBuffBases);

        // Aether badge: restore the last value so it shows immediately (the game only broadcasts the resource
        // on its own schedule — zone load etc. — so without this it's blank for the first minutes). Restore
        // BEFORE wiring the persister so the restore doesn't refresh the saved timestamp. The shugo-festa key
        // is deliberately not persisted — a stale key count would be misleading, so it waits for a broadcast.
        RestoreAetherFromSettings(services);
        services.Data.AetherStatusChanged += () => Dispatcher.BeginInvoke(() => PersistAether(services));

        // Combat-assist overlay: the local player's active buff slots, refreshed twice a second.
        _buffOverlayVm = new BuffOverlayViewModel();
        _buffOverlay = new BuffOverlayPanel(_buffOverlayVm);
        LoadPanelPosition(services.Props, _buffOverlay, "buffOverlayX", "buffOverlayY");
        ClampWhenLoaded(_buffOverlay);
        _buffOverlay.Show();
        _buffOverlay.Park();
        _controller?.RegisterOverlay(_buffOverlay);
        // Present/park the buff overlay in exact lockstep with the meter (gated by the toggle) so it never
        // disappears on its own — when the toggle is on it is always shown whenever the meter is.
        _controller?.SetCompanion(_buffOverlay, () => _settings?.ShowBuffUi == true);
        _buffOverlay.CloseRequested += () => { _settings.ShowBuffUi = false; };
        _buffOverlay.PositionChanged += (left, top) =>
        {
            services.Props.SetProperty("buffOverlayX", left.ToString("0", CultureInfo.InvariantCulture));
            services.Props.SetProperty("buffOverlayY", top.ToString("0", CultureInfo.InvariantCulture));
        };
        MeterServices svc = services;
        _buffTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _buffTimer.Tick += (_, _) => RefreshBuffOverlay(svc);
        _buffTimer.Start();

        _updateService = new UpdateService(prerelease: false);
        UpdateService updateService = _updateService;
        // Free the single-instance guard the instant an update-restart commits, so Velopack's relaunched
        // process acquires the mutex as "first" instead of racing this (exiting) process's handle.
        updateService.BeforeRestart = Program.ReleaseSingleInstance;
        _updateToast.RestartRequested += () => updateService.ApplyAndRestart();
        _updateService.StageChanged += (stage, info, percent) => Dispatcher.Invoke(() =>
        {
            switch (stage)
            {
                case UpdateService.UpdateStage.Downloading: _updateToastVm.SetDownloading(info, percent); break;
                case UpdateService.UpdateStage.Ready: _updateToastVm.SetReady(info); viewModel.SetUpdateReady(info); break;
                case UpdateService.UpdateStage.Failed: _updateToastVm.SetFailed(info); break;
            }

            // No auto-popup: the download runs silently and surfaces as the header "업데이트" badge on the
            // meter (UpdateReadyVisibility). The toast is shown only on demand when the user clicks the badge.
        });

        // User clicks the meter's update badge -> show the restart toast (bottom-right) so they apply when
        // they choose (the toast's 지금 재시작 -> UpdateService.ApplyAndRestart).
        window.UpdateRequested += () =>
        {
            Rect wa = SystemParameters.WorkArea;
            _updateToast.Left = wa.Right - _updateToast.Width - 16;
            _updateToast.Top = wa.Bottom - 130;
            _updateToast.Present(true);
        };
        _ = _updateService.CheckAndDownloadAsync(msg => Dispatcher.Invoke(() => viewModel.Status = msg));
    }

    private static string CaptureLaunchMessage(CaptureHostLaunch launch) => launch switch
    {
        CaptureHostLaunch.Blocked =>
            $"캡처 헬퍼('{CaptureHostLauncher.HostExeName}')가 차단된 것 같습니다. VPN·게임 가속기나 보안 프로그램이 " +
            "헬퍼 실행을 막고 있을 수 있어요. 보안 프로그램 허용 목록에 이 파일을 추가하거나 잠시 끄고 다시 시작해 주세요.",
        CaptureHostLaunch.Declined => "권한 상승(UAC)이 취소되어 캡처를 시작할 수 없습니다. 앱을 다시 시작하면 재시도합니다.",
        CaptureHostLaunch.NotFound => "캡처 헬퍼 파일을 찾을 수 없습니다. 앱을 재설치해 주세요.",
        _ => "캡처 헬퍼 시작에 실패했습니다. 잠시 후 다시 시도해 주세요.",
    };

    // The pipe wait only proves the helper PROCESS started — the WinDivert driver opens later, after the
    // client connects. So a booster/AV that allows the process but blocks the .sys surfaces here (not as
    // a launch failure). Re-route driver-load errors through the same actionable booster guidance.
    private static string CaptureErrorMessage(string raw)
    {
        if (raw.Contains("WinDivert", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("드라이버", StringComparison.Ordinal))
        {
            return "캡처 드라이버 로드에 실패했습니다. VPN·게임 가속기나 보안 프로그램이 드라이버를 막고 있을 수 있어요. " +
                   $"허용 목록에 추가하거나 잠시 끄고 다시 시작해 주세요. (원본: {raw})";
        }

        return raw;
    }

    /// <summary>Background waiter for the single-instance "show" signal. A later launch (see
    /// <see cref="Program"/>) opens the named event and Set()s it; we un-hide the overlay from the tray so
    /// the user gets the running instance back instead of a second, colliding one. Best-effort: any handle
    /// error (e.g. on shutdown) just ends the loop.</summary>
    private void StartSingleInstanceListener()
    {
        EventWaitHandle? signal = SingleInstanceShowSignal;
        if (signal is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    signal.WaitOne();
                }
                catch
                {
                    break; // handle disposed / abandoned on shutdown
                }

                Dispatcher.Invoke(() => _controller?.ShowFromTray());
            }
        })
        {
            IsBackground = true,
            Name = "single-instance-listener",
        };
        thread.Start();
    }

    // Open the positional replay for the last battle (the 직전 전투). Toggle: a second invocation closes it
    // so the next reopens with the latest recording. Only reachable when replay.recordMovement=true.
    /// <summary>
    /// Dev builds only (tray → "[개발] 패킷 로그 불러오기"). Replays a recorded packet-debug corpus through the
    /// live pipeline so its battles show up in the history/detail windows without running a dungeon.
    /// Replaying wipes the meter's live battle state, so it asks first.
    /// </summary>
    private void LoadPacketLog(WaffleMeter.App.Core.MeterServices services)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "패킷 로그 불러오기 (개발용)",
            Filter = "패킷 디버그 로그 (*.jsonl;*.jsonl.gz)|*.jsonl;*.jsonl.gz|모든 파일 (*.*)|*.*",
            InitialDirectory = Directory.Exists(DevPacketLogReplay.DefaultLogDirectory())
                ? DevPacketLogReplay.DefaultLogDirectory()
                : null,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"{Path.GetFileName(dialog.FileName)}\n\n" +
            "이 로그를 미터에 재생합니다. 현재 전투 상태는 초기화되고, 재생된 전투는 통계 사이트로 업로드되지 않습니다.\n" +
            "게임이 켜져 있으면 실시간 패킷과 섞일 수 있으니 꺼두는 것이 좋습니다.\n\n계속할까요?",
            "패킷 로그 불러오기 (개발용)",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        string path = dialog.FileName;
        Task.Run(() =>
        {
            try
            {
                int battles = DevPacketLogReplay.Run(services, path);
                Dispatcher.BeginInvoke(() =>
                {
                    services.NotifyBattleListChanged();
                    MessageBox.Show($"전투 {battles}건을 불러왔습니다.\n히스토리에서 열어보세요.",
                        "패킷 로그 불러오기 (개발용)", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() => MessageBox.Show(
                    "재생 실패: " + ex.Message, "패킷 로그 불러오기 (개발용)",
                    MessageBoxButton.OK, MessageBoxImage.Error));
            }
        });
    }

    /// <summary>Let the user pick a saved replay .json and play it (the "리플레이 재생" button). Opens the
    /// replays folder by default; reuses the single-instance replay window like every other open path.</summary>
    private void PlayReplayFromPicker(WaffleMeter.App.Core.MeterServices services, Window? owner)
    {
        string dir = services.ReplayDirectory;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
        }
        catch
        {
            // a missing/unwritable folder just means the dialog opens wherever it can
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "리플레이 파일 선택",
            Filter = "리플레이 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            InitialDirectory = System.IO.Directory.Exists(dir) ? dir : null,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        WaffleMeter.Replay.ReplayRecording rec;
        try
        {
            rec = WaffleMeter.Replay.ReplaySerializer.Deserialize(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"이 파일은 리플레이로 열 수 없어요.\n\n{ex.Message}", "리플레이 재생",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (rec.PointCount == 0)
        {
            MessageBox.Show(owner, "이 리플레이에는 표시할 이동 기록이 없어요.", "리플레이 재생",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowReplayWindow(rec, owner);
    }

    private void OpenReplay(WaffleMeter.App.Core.MeterServices services, Window owner)
    {
        if (_replayWindow != null)
        {
            _replayWindow.Close();
            return;
        }

        // Prefer the live last battle; after a restart that is empty, so fall back to the newest saved
        // recording on disk (history replay survives restart).
        WaffleMeter.Replay.ReplayRecording? rec = services.Movement?.LastRecording;
        if (rec is null || rec.PointCount == 0)
        {
            rec = TryLoadNewestSavedReplay(services);
        }

        if (rec is null || rec.PointCount == 0)
        {
            return; // no recorded battle with movement yet
        }

        ShowReplayWindow(rec, owner);
    }

    /// <summary>The recording for ONE saved battle: the live engine's copy if this session made it, else the
    /// file it wrote (recordings survive a restart). Null when that battle has no positions — which is what
    /// hides the ▶ on a history row.</summary>
    private static WaffleMeter.Replay.ReplayRecording? FindRecording(
        WaffleMeter.App.Core.MeterServices services, DpsReport report)
    {
        if (report.BattleStart <= 0)
        {
            return null;
        }

        if (services.Movement is { } engine
            && engine.TryGetForBattle(report.BattleStart, out WaffleMeter.Replay.ReplayRecording? live)
            && live is { PointCount: > 0 })
        {
            return live;
        }

        try
        {
            string path = System.IO.Path.Combine(services.ReplayDirectory, $"replay-{report.BattleStart}.json");
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            WaffleMeter.Replay.ReplayRecording saved =
                WaffleMeter.Replay.ReplaySerializer.Deserialize(System.IO.File.ReadAllText(path));
            return saved.PointCount > 0 ? saved : null;
        }
        catch
        {
            return null; // a corrupt/half-written file just means "no replay for this battle"
        }
    }

    // One replay window at a time: opening another battle's replay replaces the one on screen.
    private void ShowReplayWindow(WaffleMeter.Replay.ReplayRecording rec, Window? owner)
    {
        _replayWindow?.Close();

        var win = new ReplayWindow(rec);
        if (owner != null && owner.IsLoaded)
        {
            win.Owner = owner;
        }

        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_replayWindow, win))
            {
                _replayWindow = null;
            }
        };
        _replayWindow = win;
        win.Show();
    }

    private static WaffleMeter.Replay.ReplayRecording? TryLoadNewestSavedReplay(WaffleMeter.App.Core.MeterServices services)
    {
        try
        {
            string dir = System.IO.Path.Combine(services.Props.AppDirectory(), "replays");
            if (!System.IO.Directory.Exists(dir))
            {
                return null;
            }

            System.IO.FileInfo? f = new System.IO.DirectoryInfo(dir)
                .GetFiles("replay-*.json")
                .OrderByDescending(x => x.LastWriteTime)
                .FirstOrDefault();
            return f is null ? null : WaffleMeter.Replay.ReplaySerializer.Deserialize(System.IO.File.ReadAllText(f.FullName));
        }
        catch
        {
            return null;
        }
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

    private void ToggleDetail(int uid, MeterServices services, Window owner, OverlayViewModel meterVm)
    {
        // Resolve the clicked player against the report the OVERLAY IS CURRENTLY SHOWING (the live battle,
        // or a saved battle while replaying from history) — NOT the live _lastReport. A row clicked while
        // a saved battle is on screen carries a uid from that saved battle; resolving it against the live
        // report (a different, possibly unrelated battle) is what produced the "15485 상세내역" raw-uid
        // title, all-zero stats/skills, and the meter-vs-detail combat-time mismatch.
        DpsReport? source = meterVm.CurrentReport ?? _lastReport;
        if (source == null)
        {
            return;
        }

        if (_detailWindow != null && _detailUid == uid)
        {
            _detailWindow.Close(); // re-click same row -> close (toggle)
            return;
        }

        _detailWindow?.Close();

        string name = source.Contributors.FirstOrDefault(c => c.Id == uid)?.Nickname ?? uid.ToString();
        _detailViewModel = new DetailsViewModel(source, uid, services.Calculator, name, _theme!, _settings!.FontFamily);
        _detailUid = uid;
        _detailWindow = new DetailWindow { DataContext = _detailViewModel };
        _detailWindow.Closed += (s, _) =>
        {
            if (s is IReassertableOverlay overlay)
            {
                _controller?.UnregisterOverlay(overlay); // recreated per row -> drop the dead-HWND reference
            }

            _detailWindow = null;
            _detailViewModel = null;
            _detailUid = 0;
        };
        LoadWindowSize(services.Props, "detailWidth", "detailHeight", _detailWindow);
        PlaceDetailWindow(owner, _detailWindow); // right of the meter, flipping left if it would clip off-screen
        _detailWindow.Show();
        _controller?.RegisterOverlay(_detailWindow); // poll re-claims its topmost on alt-tab return to the game
        AttachScreenClamp(_detailWindow);
        AttachResize(_detailWindow, services.Props, "detailWidth", "detailHeight");
    }

    /// <summary>Place the detail window beside the meter: to its RIGHT by default, flipped to the LEFT
    /// when the right side would run off the monitor (the reported "opens off-screen" bug when the meter
    /// sits at the right edge). Clamped to the owner's monitor so it's always fully visible.</summary>
    private static void PlaceDetailWindow(Window owner, Window detail)
    {
        const double gap = 8;
        double w = detail.Width, h = detail.Height;

        IntPtr hwnd = new WindowInteropHelper(owner).Handle;
        System.Drawing.Rectangle b = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds; // physical px
        DpiScale dpi = VisualTreeHelper.GetDpi(owner);
        double left = b.Left / dpi.DpiScaleX, right = b.Right / dpi.DpiScaleX;
        double top = b.Top / dpi.DpiScaleY, bottom = b.Bottom / dpi.DpiScaleY;

        double rightPos = owner.Left + owner.ActualWidth + gap;
        double leftPos = owner.Left - w - gap;
        double x = rightPos + w <= right ? rightPos          // fits on the right
                 : leftPos >= left ? leftPos                 // else flip to the left
                 : Math.Max(left, right - w);                // neither side fits: clamp inside
        detail.Left = x;
        detail.Top = Math.Min(owner.Top, Math.Max(top, bottom - h));
    }

    private void WireJoinPanel(MeterServices services, OverlayWindow overlay)
    {
        var skillVisibility = new SkillVisibility(services.Props);
        _joinViewModel = new JoinRequestViewModel(_settings!, skillVisibility.Codes);
        _joinPanel = new JoinRequestPanel { DataContext = _joinViewModel };
        LoadWindowSize(services.Props, "joinPanelWidth", "joinPanelHeight", _joinPanel);

        // Build the HWND + assert the overlay ex-style, then park (hidden) until a request arrives.
        _joinPanel.Show();
        _joinPanel.Park();
        _controller?.RegisterOverlay(_joinPanel); // poll re-claims its topmost on alt-tab return to the game
        AttachScreenClamp(_joinPanel);
        AttachResize(_joinPanel, services.Props, "joinPanelWidth", "joinPanelHeight");

        // Restore a persisted position; otherwise dock under the meter overlay on first present.
        if (LoadPanelPosition(services.Props, _joinPanel, "joinPanelX", "joinPanelY"))
        {
            _joinPanelPositioned = true;
        }

        ClampWhenLoaded(_joinPanel); // a persisted off-screen panel position should restore reachable

        _joinPanel.PositionChanged += (left, top) =>
        {
            _joinPanelPositioned = true;
            services.Props.SetProperty("joinPanelX", left.ToString("0", CultureInfo.InvariantCulture));
            services.Props.SetProperty("joinPanelY", top.ToString("0", CultureInfo.InvariantCulture));
        };
        _joinPanel.CloseRequested += () =>
        {
            // Explicit close (✕): remember the requests showing now and stay closed until a genuinely NEW
            // requester applies. Clearing the VM resets its count to 0 so an enrichment re-Add of the SAME
            // request (which lands ~hundreds of ms later) can no longer re-fire the empty->non-empty auto-show
            // that made close look like it "didn't work".
            _joinDismissedIds.Clear();
            foreach (var s in services.JoinRequests.Snapshot())
            {
                _joinDismissedIds.Add(s.Requester);
            }

            _joinUserDismissed = true;
            _joinViewModel.Clear();
            _joinPanel.Park();
        };

        void PresentJoinPanel()
        {
            if (!_joinPanelPositioned)
            {
                _joinPanel.Left = overlay.Left;
                _joinPanel.Top = overlay.Top + overlay.ActualHeight + 8;
            }

            _joinPanel.Present(true);
        }

        // Auto-open on the empty -> non-empty transition (web isOpen behavior), unless the user turned off
        // auto-show (the header 파티 신청 button still opens it manually).
        _joinViewModel.RequestPresent += () =>
        {
            if (_settings!.ShowJoinPanel && !_joinUserDismissed)
            {
                PresentJoinPanel();
            }
        };

        // 계정/파티 신청 header button: toggle the panel manually (Opacity tracks park/present).
        overlay.JoinRequested += () =>
        {
            if (_joinPanel.Opacity > 0)
            {
                _joinPanel.Park();
            }
            else
            {
                _joinUserDismissed = false; // manual open overrides a prior dismissal
                _joinViewModel.Reconcile(services.JoinRequests.Snapshot()); // re-show currently-live requests
                PresentJoinPanel();
            }
        };

        // Store events fire on the meter-consumer thread; marshal to the UI.
        services.JoinRequests.Changed += () => Dispatcher.Invoke(() =>
        {
            var snapshot = services.JoinRequests.Snapshot();
            if (_joinUserDismissed)
            {
                foreach (var s in snapshot)
                {
                    if (!_joinDismissedIds.Contains(s.Requester))
                    {
                        _joinUserDismissed = false; // a brand-new requester re-arms auto-show (option a)
                        break;
                    }
                }
            }

            _joinViewModel.Reconcile(snapshot);
        });
        services.JoinRequests.Cleared += () => Dispatcher.Invoke(() =>
        {
            _joinUserDismissed = false; // party exit / instance start resets the dismissal
            _joinDismissedIds.Clear();
            _joinViewModel.Clear();
            _joinPanel.Park();
        });

        // Skill-settings flyout (visibleSkillCodes filter). The ⚙ button toggles it; changes re-render badges.
        var skillVm = new SkillSettingsViewModel(skillVisibility);
        _skillFlyout = new SkillSettingsFlyout { DataContext = skillVm };
        LoadWindowSize(services.Props, "skillFlyoutWidth", "skillFlyoutHeight", _skillFlyout);
        _skillFlyout.Show();
        _skillFlyout.Park();
        _controller?.RegisterOverlay(_skillFlyout);
        AttachScreenClamp(_skillFlyout);
        AttachResize(_skillFlyout, services.Props, "skillFlyoutWidth", "skillFlyoutHeight");
        _skillFlyout.CloseRequested += () => { _skillFlyoutVisible = false; _skillFlyout.Park(); };
        skillVm.Changed += () =>
        {
            _joinViewModel.SetVisibleCodes(skillVisibility.Codes);
            _joinViewModel.Reconcile(services.JoinRequests.Snapshot()); // rebuild rows so badges honor the new set
        };
        _joinPanel.SettingsRequested += () =>
        {
            if (_skillFlyoutVisible)
            {
                _skillFlyoutVisible = false;
                _skillFlyout.Park();
                return;
            }

            _skillFlyout.Left = _joinPanel.Left + _joinPanel.Width + 8;
            _skillFlyout.Top = _joinPanel.Top;
            _skillFlyoutVisible = true;
            _skillFlyout.Present(true);
        };
    }

    private void WireHistoryPanel(MeterServices services, OverlayWindow overlay, OverlayViewModel meterViewModel)
    {
        _historyViewModel = new BattleHistoryViewModel(_theme!, _settings!);
        _historyPanel = new HistoryPanel { DataContext = _historyViewModel };
        LoadWindowSize(services.Props, "historyPanelWidth", "historyPanelHeight", _historyPanel);
        _historyPanel.Show();
        _historyPanel.Park();
        _controller?.RegisterOverlay(_historyPanel);
        AttachScreenClamp(_historyPanel);
        AttachResize(_historyPanel, services.Props, "historyPanelWidth", "historyPanelHeight");

        if (LoadPanelPosition(services.Props, _historyPanel, "historyPanelX", "historyPanelY"))
        {
            _historyPanelPositioned = true;
        }

        ClampWhenLoaded(_historyPanel); // a persisted off-screen panel position should restore reachable

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

        // Saved-battle snapshots arrive on the consumer thread; cache them on the UI thread. BeginInvoke
        // (not Invoke) so the consumer never blocks on the UI thread — during app shutdown the UI thread is
        // itself joining the consumer, and a synchronous Invoke there would mutually deadlock (and stall the
        // shutdown save). A history-panel refresh is not latency-critical; if the dispatcher is already
        // shutting down the post simply doesn't run.
        services.BattleListChanged += battles => Dispatcher.BeginInvoke(() => _historyViewModel.SetBattles(battles));

        // Clicking a saved battle replays it in the meter until the next live battle starts.
        _historyViewModel.BattleSelected += report =>
        {
            _viewingHistory = true;
            _historyBaselineBattleStart = _lastReport?.BattleStart ?? 0;
            meterViewModel.Update(report);
        };

        // ▶ on a row: the positional replay for THAT battle (the tray entry only opens the last one). The
        // button is only rendered for battles that actually have a recording.
        _historyViewModel.HasReplay = report => FindRecording(services, report) is not null;
        _historyViewModel.ReplayRequested += report =>
        {
            if (FindRecording(services, report) is { } rec)
            {
                ShowReplayWindow(rec, _historyPanel);
            }
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

    /// <summary>Settings "위치 초기화": clear a panel's saved position and re-dock it now.</summary>
    private void ResetPanelPosition(string which, MeterServices services, OverlayWindow overlay)
    {
        switch (which)
        {
            case "meter":
                services.Props.SetProperty("uiX", string.Empty);
                services.Props.SetProperty("uiY", string.Empty);
                services.Props.SetProperty("windowX", string.Empty);
                services.Props.SetProperty("windowY", string.Empty);
                overlay.Left = 40;
                overlay.Top = 40;
                break;
            case "join":
                services.Props.SetProperty("joinPanelX", string.Empty);
                services.Props.SetProperty("joinPanelY", string.Empty);
                _joinPanelPositioned = false;
                if (_joinPanel is { } jp && jp.Opacity > 0)
                {
                    jp.Left = overlay.Left;
                    jp.Top = overlay.Top + overlay.ActualHeight + 8;
                }

                break;
            case "history":
                services.Props.SetProperty("historyPanelX", string.Empty);
                services.Props.SetProperty("historyPanelY", string.Empty);
                _historyPanelPositioned = false;
                if (_historyPanel is { } hp && _historyPanelVisible)
                {
                    hp.Left = overlay.Left + overlay.ActualWidth + 8;
                    hp.Top = overlay.Top;
                }

                break;
        }
    }

    /// <summary>Confine a window to its monitor while multi-monitor movement is off (off-screen guard).</summary>
    private void AttachScreenClamp(Window w)
    {
        w.LocationChanged += (_, _) => ScreenClamp.Apply(w, _settings?.MultiMonitorMode ?? false);
    }

    /// <summary>One-shot off-screen reconciliation after a window is shown. LoadPosition/LoadPanelPosition
    /// assign Left/Top before the HWND and layout exist, so a persisted position naming a monitor that no
    /// longer exists (undocked/disconnected) or lying outside the virtual desktop would otherwise restore
    /// the window invisibly. Dispatched at Loaded priority so ActualWidth/Height are valid when it runs.</summary>
    private void ClampWhenLoaded(Window w) =>
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => ScreenClamp.Apply(w, _settings?.MultiMonitorMode ?? false)));

    /// <summary>Display topology changed (monitor unplugged / resolution / arrangement) — pull every
    /// window back onto a live monitor. Fires off the UI thread, so marshal the clamp to the dispatcher.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ClampAllWindows);

    /// <summary>Re-clamp every meter window (called when multi-monitor is turned off).</summary>
    private void ClampAllWindows()
    {
        bool allow = _settings?.MultiMonitorMode ?? false;
        foreach (Window? w in new Window?[] { _overlayWindow, _joinPanel, _historyPanel, _skillFlyout, _detailWindow })
        {
            if (w != null)
            {
                ScreenClamp.Apply(w, allow);
            }
        }
    }

    /// <summary>Apply a persisted manual size (no-op if unset/invalid).</summary>
    private static void LoadWindowSize(PropertyHandler props, string wKey, string hKey, Window window)
    {
        if (double.TryParse(props.GetProperty(wKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double w) && w >= window.MinWidth &&
            double.TryParse(props.GetProperty(hKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h >= window.MinHeight)
        {
            window.Width = w;
            window.Height = h;
        }
    }

    /// <summary>Apply only a persisted WIDTH (for the meter, whose height auto-sizes to its content).</summary>
    private static void LoadWindowWidth(PropertyHandler props, string wKey, Window window)
    {
        if (double.TryParse(props.GetProperty(wKey), NumberStyles.Float, CultureInfo.InvariantCulture, out double w) && w >= window.MinWidth)
        {
            window.Width = w;
        }
    }

    /// <summary>Attach edge resize + persist the new size on resize. When <paramref name="widthOnly"/>,
    /// only the width is persisted (the window's height is content-driven).</summary>
    private void AttachResize(Window window, PropertyHandler props, string wKey, string hKey, bool widthOnly = false)
    {
        WindowResizer.Attach(window);
        window.SizeChanged += (_, _) =>
        {
            props.SetProperty(wKey, window.ActualWidth.ToString("0", CultureInfo.InvariantCulture));
            if (!widthOnly)
            {
                props.SetProperty(hKey, window.ActualHeight.ToString("0", CultureInfo.InvariantCulture));
            }
        };
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

    // The aether badge is worth showing immediately from a cached value; older than this it's likely stale
    // (values change between sessions), so we skip the restore and wait for a fresh broadcast instead.
    private const long AetherRestoreMaxAgeMs = 12L * 60 * 60 * 1000; // 12h

    /// <summary>Seed the aether balance from the persisted "base,bonus,total,unixMs" value, if it's recent
    /// enough. Clamped for sanity. Never overrides a live value (RestoreAetherStatus is onlyIfEmpty).</summary>
    private void RestoreAetherFromSettings(MeterServices services)
    {
        string[] parts = _settings!.AetherLastValue.Split(',');
        if (parts.Length != 4
            || !int.TryParse(parts[0], out int b) || !int.TryParse(parts[1], out int bonus)
            || !int.TryParse(parts[2], out int total) || !long.TryParse(parts[3], out long savedAtMs))
        {
            return;
        }

        long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - savedAtMs;
        if (ageMs < 0 || ageMs > AetherRestoreMaxAgeMs)
        {
            return; // too old (or a clock skew) — wait for a fresh broadcast rather than show a stale value
        }

        b = Math.Clamp(b, 0, 10000);
        bonus = Math.Clamp(bonus, 0, 10000);
        total = Math.Clamp(total, 0, 20000);
        if (b == 0 && bonus == 0 && total == 0)
        {
            return;
        }

        services.Data.RestoreAetherStatus(b, bonus, total);
    }

    /// <summary>Persist the current aether value (with a timestamp) so the next launch can restore it. On a
    /// character switch the value is cleared (HasValue=false) → we drop the persisted value so a different
    /// character's balance isn't restored next time.</summary>
    private void PersistAether(MeterServices services)
    {
        (int b, int bonus, int total, bool has) = services.Data.CurrentAether;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _settings!.AetherLastValue = has
            ? string.Concat(b.ToString(CultureInfo.InvariantCulture), ",", bonus.ToString(CultureInfo.InvariantCulture), ",",
                total.ToString(CultureInfo.InvariantCulture), ",", nowMs.ToString(CultureInfo.InvariantCulture))
            : string.Empty;

        // Also remember this balance UNDER the current character's identity, so the 캐릭터 관리 list can show
        // every character's 오드 — not just the active one. Keyed by the same stats identity hash the list uses.
        if (has)
        {
            string? hash = services.Consent.CurrentCharacterHash();
            if (!string.IsNullOrEmpty(hash))
            {
                AetherPerCharacterStore store = AetherPerCharacterStore.Parse(_settings.AetherPerCharacter);
                if (store.Upsert(hash, new AetherSnapshot(b, bonus, total, nowMs)))
                {
                    _settings.AetherPerCharacter = store.Serialize();
                }
            }
        }
    }

    /// <summary>Show the 슈고 페스타 reminder toast (docked under the meter) + play the alarm chime.</summary>
    /// <summary>One-time post-update patch-note popup. Shows the running version's RELEASE_NOTES section exactly
    /// once after an UPDATE: compares the running base version to the last-shown one persisted in settings. A
    /// fresh install / first run with this feature (no last-shown version yet) is recorded SILENTLY — it is not
    /// an update. Records the version before showing so a failure never re-pops, and never throws into startup.</summary>
    private void MaybeShowPatchNotes(string version)
    {
        try
        {
            string baseVer = PatchNotesProvider.BaseVersion(version);
            if (baseVer.Length == 0 || _settings is not { } settings)
            {
                return;
            }

            string last = settings.PatchNotesLastShownVersion;
            if (string.IsNullOrEmpty(last))
            {
                settings.PatchNotesLastShownVersion = baseVer; // fresh install: record, do NOT pop
                return;
            }

            if (last == baseVer)
            {
                return; // already shown for this version
            }

            settings.PatchNotesLastShownVersion = baseVer; // record BEFORE showing so a failure never re-pops
            string? notes = PatchNotesProvider.SectionForVersion(LoadEmbeddedReleaseNotes(), baseVer);
            if (string.IsNullOrWhiteSpace(notes))
            {
                return; // no section for this version (e.g. a hotfix with no entry) — skip silently
            }

            new PatchNotesWindow(baseVer, notes).Show();
        }
        catch
        {
            // a "what's new" popup must never disturb startup
        }
    }

    /// <summary>The bundled RELEASE_NOTES.md text (embedded resource), or "" if unavailable.</summary>
    private static string LoadEmbeddedReleaseNotes()
    {
        try
        {
            using Stream? stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("RELEASE_NOTES.md");
            if (stream == null)
            {
                return "";
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    private void ShowShugoAlarm(int lead)
    {
        if (_alarmToast is null || _alarmToastVm is null)
        {
            return;
        }

        _alarmToastVm.SetShugo(lead);
        if (_overlayWindow is { } w)
        {
            _alarmToast.Left = w.Left;
            _alarmToast.Top = w.Top + w.ActualHeight + 8;
        }

        _alarmToast.Present(true);
        PlayAlert(_alarmToastVm.SpokenText);
    }

    /// <summary>Show a field-boss respawn reminder toast (docked under the meter) + alert sound/voice.</summary>
    private void ShowFieldBossAlarm(FieldBossAlarm.Due due)
    {
        if (_alarmToast is null || _alarmToastVm is null)
        {
            return;
        }

        DateTime respawn = DateTimeOffset.FromUnixTimeMilliseconds(due.TargetMs).LocalDateTime;
        _alarmToastVm.SetFieldBoss(FieldBossCatalog.Name(due.Code), due.LeadMinutes, respawn);
        if (_overlayWindow is { } w)
        {
            _alarmToast.Left = w.Left;
            _alarmToast.Top = w.Top + w.ActualHeight + 8;
        }

        _alarmToast.Present(true);
        PlayAlert(_alarmToastVm.SpokenText);
    }

    // ~0.8s pre-warn so the "오프" voice lands right around the buff's actual expiry (TTS init + speak latency).
    private const long BuffEndTtsLeadMs = 800;
    private readonly HashSet<int> _buffStartAnnounced = new(); // base codes we've spoken "온" for (cleared when they end)
    private readonly Dictionary<int, long> _buffEndAnnouncedFor = new(); // base code -> the End(ms) already "오프"-warned; a re-cast extends End and re-arms
    private long _lastBuffClearRevision; // 마지막으로 본 DataManager.OwnerBuffClearRevision (사망 클리어 감지)

    // Refresh the combat-assist overlay each tick: pull the local player's active buffs, fire the start/end
    // voice alerts, update the slot content, AND reconcile the window's visibility. This 500ms timer always
    // runs (unlike the controller poll, which skips the companion during the startup grace — the cause of the
    // buff overlay staying hidden until settings was opened), so it is the reliable visibility driver. It keys
    // off the controller's CompanionShown — the SAME decision the poll acts on — so Present/Fade never disagree
    // (keying off MeterShown made the two fight while the meter was hidden with "오버레이 유지" on → flicker).
    private void RefreshBuffOverlay(MeterServices services)
    {
        if (_buffOverlayVm is null || _settings is null)
        {
            return;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        IReadOnlyList<WaffleMeter.Data.OwnerBuffView> buffs = services.Data.ActiveOwnerBuffs(nowMs);
        if (!_settings.ShowOtherPlayerBuffs)
        {
            buffs = buffs.Where(b => !b.ByOther).ToList();
        }

        // 사망으로 버프가 통째로 비워진 틱에서는 종료 음성을 내지 않는다. 클리어된 버프는 스냅샷에서 사라져
        // 보통은 "오프" 조건(남은시간 800ms 이하)에 닿지도 않지만, 스냅샷을 뜬 직후 클리어가 들어오는
        // 서브초 레이스에서는 잔여 버프가 한 번 외칠 수 있다. 알림 상태도 함께 비워 부활 후 재시전 때
        // "온"이 정상적으로 다시 나오게 한다.
        long clearRevision = services.Data.OwnerBuffClearRevision;
        if (clearRevision != _lastBuffClearRevision)
        {
            _lastBuffClearRevision = clearRevision;
            _buffStartAnnounced.Clear();
            _buffEndAnnouncedFor.Clear();
        }
        else
        {
            // The announce list includes 음성만 (voice-only) buffs; the overlay draws only Overlay==true ones.
            AnnounceBuffTransitions(buffs);
        }

        _buffOverlayVm.ShowBackground = !_settings.BuffUiTransparent;
        _buffOverlayVm.SetIconSize(_settings.BuffUiIconSize);
        _buffOverlayVm.SetTextColor(_settings.BuffUiTextColor);
        // 표시 순서: 전역 정렬 모드로 줄을 세우고, 사용자가 "맨 앞 고정"한 버프를 그 앞으로 끌어온다.
        List<WaffleMeter.Data.OwnerBuffView> drawn = BuffOverlayOrder.Sort(
            buffs.Where(b => b.Overlay).ToList(), _settings.BuffUiSortMode, _settings.BuffUiPinnedCodes);
        _buffOverlayVm.Update(drawn, _settings.BuffUiGrayOnCooldown);

        // Visibility: mirror the controller's companion decision (CompanionShown already folds in ShowBuffUi,
        // the meter's on-screen state, and the "메터 숨겨도 오버레이 유지" toggle). Mirror the meter's click-through
        // when shown, and re-claim topmost each tick so a borderless-fullscreen game can't strand it behind.
        if (_buffOverlay is not null)
        {
            bool show = _settings.ShowBuffUi && (_controller?.CompanionShown ?? true);
            if (show)
            {
                _buffOverlay.SetClickThrough(_controller?.MeterClickThrough ?? false);
                _buffOverlay.Present(true);
                _buffOverlay.ReassertTopmostIfBuried();
            }
            else
            {
                _buffOverlay.Fade();
            }
        }
    }

    // Speak "이름 온" when a buff set to "오버레이+음성" / "음성만" starts and "이름 오프" just before it ends (each
    // once, gated by the global start/end toggles). Per-buff voice is chosen in the 버프 알림 tab; independent of
    // the visual overlay so voice can be used on its own (음성만). Codes here are already BASE codes, so the same
    // buff re-cast by another player is one entry — it takes over the earlier one WITHOUT a second start voice,
    // and re-arms the end alert off the refreshed expiry. Alerts are queued durably so a burst of simultaneous
    // buffs is spoken in sequence rather than the later ones being dropped.
    private void AnnounceBuffTransitions(IReadOnlyList<WaffleMeter.Data.OwnerBuffView> buffs)
    {
        if (_settings is not { } s || (!s.BuffTtsOnStart && !s.BuffTtsOnEnd))
        {
            _buffStartAnnounced.Clear();
            _buffEndAnnouncedFor.Clear();
            return;
        }

        HashSet<int> voiceCodes = s.BuffUiVoiceCodes; // base codes set to 오버레이+음성 or 음성만
        foreach (WaffleMeter.Data.OwnerBuffView b in buffs)
        {
            if (!voiceCodes.Contains(b.Code))
            {
                continue; // overlay-only (or off) buff — no voice
            }

            // Start once per buff. A same-buff re-cast (base already announced) does NOT re-announce — the later
            // cast silently takes over the earlier one.
            if (s.BuffTtsOnStart && _buffStartAnnounced.Add(b.Code))
            {
                TtsSpeech.Speak($"{b.Name} 온", s.AlarmVolume, durable: true);
            }

            // Pre-warn the end once inside the lead window (skip very short buffs so it doesn't double up with
            // the start). Keyed on the End(ms): a re-cast that extends the buff gives a new End and re-arms this,
            // so the end alert fires off the REFRESHED duration. A maintained stance (폭주) is skipped entirely:
            // its expiry is a synthetic keep-alive, not a real end, so pre-warning it spoke a false "오프" every
            // time a held re-broadcast gap elapsed while the stance was still up.
            if (s.BuffTtsOnEnd && !b.Indefinite && b.DurationMs > BuffEndTtsLeadMs * 2 && b.RemainingMs > 0 && b.RemainingMs <= BuffEndTtsLeadMs
                && (!_buffEndAnnouncedFor.TryGetValue(b.Code, out long warnedEnd) || warnedEnd != b.EndMs))
            {
                _buffEndAnnouncedFor[b.Code] = b.EndMs;
                TtsSpeech.Speak($"{b.Name} 오프", s.AlarmVolume, durable: true);
            }
        }

        // Codes no longer active can announce again next time they appear.
        var current = buffs.Select(b => b.Code).ToHashSet();
        _buffStartAnnounced.RemoveWhere(c => !current.Contains(c));
        foreach (int c in _buffEndAnnouncedFor.Keys.Where(c => !current.Contains(c)).ToList())
        {
            _buffEndAnnouncedFor.Remove(c);
        }
    }

    /// <summary>Sound an alert: speak it with TTS if enabled (which falls back to the chime on failure),
    /// otherwise play the chime when the sound setting is on.</summary>
    private void PlayAlert(string spokenText)
    {
        if (_settings is not { } s)
        {
            return;
        }

        if (s.TtsEnabled)
        {
            TtsSpeech.Speak(spokenText, s.AlarmVolume);
        }
        else if (s.AlarmSoundEnabled)
        {
            AlarmSound.Play(s.AlarmVolume);
        }
    }

    /// <summary>Show a user custom-alarm toast (docked under the meter) + play the alarm chime.</summary>
    private void ShowCustomAlarm(CustomAlarm alarm)
    {
        if (_alarmToast is null || _alarmToastVm is null)
        {
            return;
        }

        _alarmToastVm.SetCustom(alarm.Title);
        if (_overlayWindow is { } w)
        {
            _alarmToast.Left = w.Left;
            _alarmToast.Top = w.Top + w.ActualHeight + 8;
        }

        _alarmToast.Present(true);
        PlayAlert(_alarmToastVm.SpokenText);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _controller?.Stop(); // unhook the foreground WinEvent + stop the poll
        _alarms?.Stop();
        _buffPresets?.Dispose();
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _engine?.Services.DebugLogger.Stop(); // finalize the gzip trailer if a packet-log session is running
        _engine?.Dispose();
        base.OnExit(e);
    }
}
