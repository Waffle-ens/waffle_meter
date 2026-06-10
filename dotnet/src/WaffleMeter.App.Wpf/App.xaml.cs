using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Capture.Live;
using WaffleMeter.Services;

namespace WaffleMeter.App.Wpf;

public partial class App : Application
{
    private MeterEngine? _engine;
    private HotkeyHandler? _hotkeys;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Decided overlay render mode: software rendering (no GPU compositing) keeps the overlay off
        // the game's GPU path; WS_EX_NOACTIVATE on the window keeps it from stealing foreground.
        // Mirrors the proven Kotlin prism-sw + NOACTIVATE approach.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var services = new MeterServices(new PropertyHandler());
        TryLoadCatalogs(services);

        var viewModel = new OverlayViewModel(services.Version);
        var window = new OverlayWindow { DataContext = viewModel };
        window.Show();

        // Global hotkeys (Ctrl+R reset / Ctrl+H visibility / Ctrl+T click-through). Callbacks fire on
        // the listener thread, so marshal window ops to the dispatcher.
        _hotkeys = new HotkeyHandler(services.Props)
        {
            OnReset = () => { /* placeholder: the Kotlin reset hotkey is currently a no-op */ },
            OnVisibility = () => Dispatcher.Invoke(() =>
                window.Visibility = window.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible),
            OnClickThrough = () => Dispatcher.Invoke(() => window.SetClickThrough(!window.ClickThrough)),
        };
        _hotkeys.Start();

        // Capture runs in the elevated CaptureHost; the UI connects over the pipe (no admin here).
        // The connect timeout is generous so it tolerates the user answering the UAC prompt.
        _engine = new MeterEngine(services, new NamedPipeCaptureClient("windivert", connectTimeoutMs: 30_000));
        _engine.ReportUpdated += report => Dispatcher.Invoke(() => viewModel.Update(report));
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
        _hotkeys?.Dispose();
        _engine?.Dispose();
        base.OnExit(e);
    }
}
