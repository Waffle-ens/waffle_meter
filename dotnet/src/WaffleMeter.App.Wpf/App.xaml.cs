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

        // Capture runs in the elevated CaptureHost; the UI connects over the pipe (no admin here).
        _engine = new MeterEngine(services, new NamedPipeCaptureClient("windivert"));
        _engine.ReportUpdated += report => Dispatcher.Invoke(() => viewModel.Update(report));
        _engine.CaptureError += message => Dispatcher.Invoke(() => viewModel.Status = message);
        try
        {
            _engine.Start();
            viewModel.Status = "캡처 중";
        }
        catch (Exception ex)
        {
            viewModel.Status = $"캡처 헬퍼 미연결 ({ex.Message})";
        }

        window.Show();
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
        _engine?.Dispose();
        base.OnExit(e);
    }
}
