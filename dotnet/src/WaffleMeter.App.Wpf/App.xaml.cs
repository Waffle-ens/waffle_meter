using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

public partial class App : Application
{
    private MeterEngine? _engine;
    private MeterSettings? _settings;
    private HotkeyHandler? _hotkeys;
    private OverlayController? _controller;
    private TrayIconController? _tray;
    private DpsReport? _lastReport;
    private DetailWindow? _detailWindow;
    private DetailsViewModel? _detailViewModel;
    private int _detailUid;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Decided overlay render mode: software rendering (no GPU compositing) keeps the overlay off
        // the game's GPU path; WS_EX_NOACTIVATE on the window keeps it from stealing foreground.
        // Mirrors the proven Kotlin prism-sw + NOACTIVATE approach.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var services = new MeterServices(new PropertyHandler());
        TryLoadCatalogs(services);

        _settings = new MeterSettings(services.Props);
        var viewModel = new OverlayViewModel(services.Version, _settings);
        var window = new OverlayWindow { DataContext = viewModel };
        LoadPosition(services.Props, window);
        window.Show();

        // Auto-hide / park-present + tray (Kotlin BrowserApp behavior).
        _controller = new OverlayController(window, services.Props);
        _controller.Start();
        _tray = new TrayIconController(window, _controller, () => Dispatcher.Invoke(ExitApp));
        window.PositionChanged += (left, top) => SavePosition(services.Props, left, top);

        // Global hotkeys (Ctrl+R reset / Ctrl+H visibility / Ctrl+T click-through). Callbacks fire on
        // the listener thread, so marshal window ops to the dispatcher.
        OverlayController controller = _controller;
        _hotkeys = new HotkeyHandler(services.Props)
        {
            OnReset = () => { /* placeholder: the Kotlin reset hotkey is currently a no-op */ },
            OnVisibility = () => Dispatcher.Invoke(controller.ToggleVisibility),
            OnClickThrough = () => Dispatcher.Invoke(() => window.SetClickThrough(!window.ClickThrough)),
        };
        _hotkeys.Start();

        // Right-click overlay -> 설정 / 종료.
        HotkeyHandler hotkeys = _hotkeys;
        MeterSettings settings = _settings;
        window.SettingsRequested += () =>
        {
            var settingsWindow = new SettingsWindow(new SettingsViewModel(services, settings, controller, hotkeys)) { Owner = window };
            settingsWindow.Show();
        };
        window.ExitRequested += ExitApp;

        // Row click -> open/close the detail window for that player.
        viewModel.SelectionToggled += uid => ToggleDetail(uid, services, window);

        // Capture runs in the elevated CaptureHost; the UI connects over the pipe (no admin here).
        // The connect timeout is generous so it tolerates the user answering the UAC prompt.
        _engine = new MeterEngine(services, new NamedPipeCaptureClient("windivert", connectTimeoutMs: 30_000));
        _engine.ReportUpdated += report => Dispatcher.Invoke(() =>
        {
            _lastReport = report;
            viewModel.Update(report);
            _detailViewModel?.Refresh(report); // live-refresh the open detail window
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
        _detailViewModel = new DetailsViewModel(_lastReport, uid, services.Calculator, name);
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
