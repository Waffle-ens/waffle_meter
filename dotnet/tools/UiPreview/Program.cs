using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaffleMeter.App.Core;
using WaffleMeter.App.Wpf;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Services;

namespace WaffleMeter.Tools.UiPreview;

/// <summary>
/// Renders the WPF overlay panels with sample data to PNG via RenderTargetBitmap — an offline UI check
/// (no game / capture host needed). Output dir is printed to stdout.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "waffle_ui_preview");
        Directory.CreateDirectory(outDir);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var props = new PropertyHandler();
        var settings = new MeterSettings(props);
        var theme = new MeterColorTheme(props);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Preload skin palettes once (re-sourcing a ResourceDictionary mid-run is flaky).
        var skins = new[] { "Dark", "Midnight", "Slate", "Light" }.ToDictionary(
            s => s,
            s => new ResourceDictionary { Source = new Uri($"pack://application:,,,/WaffleMeter.App.Wpf;component/Themes/Skin.{s}.xaml") });

        foreach (string skin in skins.Keys)
        {
            ResourceDictionary palette = skins[skin];

            var join = new JoinRequestViewModel(settings);
            join.Reconcile(new List<JoinRequestUser>
            {
                new() { Requester = 1, Nickname = "쿵해쫑", Job = "마도성", Server = 1001, Power = 423359, ArrivedAt = now - 2000,
                    Skill = new Dictionary<int, int> { [15210000] = 5, [15740000] = 3, [15360000] = 2, [15400000] = 4 } },
                new() { Requester = 2, Nickname = "검사왕", Job = "검성", Server = 1002, Power = 512000, ArrivedAt = now - 9000 },
                new() { Requester = 3, Nickname = "빛의사제", Job = "치유성", Server = 2001, Power = 298400, ArrivedAt = now - 16500 },
            });
            Capture(() => new JoinRequestPanel { DataContext = join }, palette, Path.Combine(outDir, $"join_{skin}.png"));

            var history = new BattleHistoryViewModel(theme, settings);
            history.SetBattles(SampleBattles(now));
            Capture(() => new HistoryPanel { DataContext = history }, palette, Path.Combine(outDir, $"history_{skin}.png"));

            string currentSkin = skin;
            var overlay = new OverlayViewModel("1.7.8", settings, theme, () => currentSkin == "Light") { Status = "캡처 중" };
            overlay.Update(SampleMeterReport(now));
            Capture(() => new OverlayWindow { DataContext = overlay }, palette, Path.Combine(outDir, $"meter_{skin}.png"));

            if (skin == "Dark")
            {
                // idle case: durationMs>0 but 0 rows — must NOT stack placeholder + combat-timer pill.
                var idle = new OverlayViewModel("1.7.8", settings, theme) { Status = "캡처 헬퍼 시작 실패: NotFound" };
                idle.Update(new DpsReport { BattleStart = 0, BattleEnd = 5000 });
                Capture(() => new OverlayWindow { DataContext = idle }, palette, Path.Combine(outDir, "meter_idle_Dark.png"));

                // Detail window chrome (skill table is empty without packet data, but verifies re-theming).
                var calc = new WaffleMeter.Data.DpsCalculator(new WaffleMeter.Data.DataManager(), () => { });
                var details = new DetailsViewModel(SampleMeterReport(now), 1, calc, "콘팡", theme, settings.FontFamily);
                details.Refresh(SampleMeterReport(now));
                Capture(() => new DetailWindow { DataContext = details }, palette, Path.Combine(outDir, "detail_Dark.png"));

                Capture(() => new CloseActionDialog(), palette, Path.Combine(outDir, "closedialog_Dark.png"));
                Capture(() => new StatsConsentModal("콘팡 · 마도성"), palette, Path.Combine(outDir, "consent_Dark.png"));

                var dl = new UpdateToastViewModel();
                dl.SetDownloading("1.8.0", 62);
                Capture(() => new UpdateToast { DataContext = dl }, palette, Path.Combine(outDir, "toast_downloading_Dark.png"));
                var rdy = new UpdateToastViewModel();
                rdy.SetReady("1.8.0");
                Capture(() => new UpdateToast { DataContext = rdy }, palette, Path.Combine(outDir, "toast_ready_Dark.png"));

                var skillVis = new SkillVisibility(props);
                Capture(() => new SkillSettingsFlyout { DataContext = new SkillSettingsViewModel(skillVis) }, palette, Path.Combine(outDir, "skillsettings_Dark.png"));
            }
        }

        Console.WriteLine(outDir);
        app.Shutdown();
    }

    private static void Capture(Func<Window> factory, ResourceDictionary palette, string path)
    {
        Window? window = null;
        try
        {
            window = factory();
            // Inject the skin into the WINDOW's resources (not Application.Resources — mutating the latter
            // trips net10 ThemeManager.SyncApplicationThemeMode). DynamicResource Skin.* resolves here.
            window.Resources.MergedDictionaries.Insert(0, palette);
            window.Left = -10000;
            window.Top = -10000;
            window.Show();

            // Drain the dispatcher so data binding + item realization complete, then measure explicitly
            // (SizeToContent's async sizing returns 0 for later windows).
            Drain(window.Dispatcher);

            var content = (FrameworkElement)window.Content;
            double availableW = double.IsNaN(window.Width) ? content.ActualWidth : window.Width;
            content.Measure(new Size(availableW, double.PositiveInfinity));
            double measuredH = content.DesiredSize.Height > 0 ? content.DesiredSize.Height : content.ActualHeight;
            content.Arrange(new Rect(0, 0, availableW, measuredH));
            content.UpdateLayout();

            int width = (int)Math.Ceiling(availableW);
            int height = (int)Math.Ceiling(measuredH);
            if (width <= 0 || height <= 0)
            {
                Console.WriteLine($"  [skip] {Path.GetFileName(path)} measured {width}x{height}");
                return;
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using FileStream fs = File.Create(path);
            encoder.Save(fs);
            Console.WriteLine($"  [ok]   {Path.GetFileName(path)} {width}x{height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [fail] {Path.GetFileName(path)}: {ex.Message}");
        }
        finally
        {
            window?.Close();
        }
    }

    /// <summary>Process all pending dispatcher work down to Background priority (waits for layout/binding).</summary>
    private static void Drain(Dispatcher dispatcher)
    {
        foreach (DispatcherPriority p in new[] { DispatcherPriority.Render, DispatcherPriority.Loaded, DispatcherPriority.Background })
        {
            dispatcher.Invoke(() => { }, p);
        }
    }

    private static List<(int Index, DpsReport Report)> SampleBattles(long now) => new()
    {
        Battle(0, "들판의 늑대", false, now - 600_000, now - 600_000 + 45_000, 120_000, 95_000),
        Battle(1, "발탄 군주", true, now - 300_000, now - 300_000 + 183_000, 8_200_000, 6_400_000, 3_100_000),
        Battle(2, "그림자 추적자", true, now - 120_000, now - 120_000 + 92_000, 4_200_000, 3_800_000),
    };

    private static DpsReport SampleMeterReport(long now)
    {
        var report = new DpsReport
        {
            BattleStart = now - 145_300,
            BattleEnd = now,
            Target = new MobInfo(999, new Mob(500, "크로메데의 심연", true), remainHp: 0, maxHp: 168_750_000),
            Contributors = new List<User>
            {
                new(1, "콘팡", 1001, JobClass.SORCERER, isExecutor: true, power: 656_000),
                new(2, "쌈", 1001, JobClass.GLADIATOR, power: 663_400),
                new(3, "강까", 1001, JobClass.RANGER, power: 659_500),
                new(4, "노까", 1002, JobClass.CLERIC, power: 591_700),
            },
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(59_300_000, 408_239, 35.1, 35.1),
                [2] = new DpsInformation(46_200_000, 318_077, 27.4, 27.4),
                [3] = new DpsInformation(36_300_000, 249_953, 21.5, 21.5),
                [4] = new DpsInformation(27_000_000, 185_861, 16.0, 16.0),
            },
        };
        return report;
    }

    private static (int, DpsReport) Battle(int idx, string mob, bool boss, long start, long end, params double[] amounts)
    {
        var info = new Dictionary<int, DpsInformation>();
        for (int i = 0; i < amounts.Length; i++)
        {
            info[i + 1] = new DpsInformation(amounts[i], 0, 0, 0);
        }

        return (idx, new DpsReport
        {
            BattleStart = start,
            BattleEnd = end,
            Target = new MobInfo(idx + 1, new Mob(100 + idx, mob, boss)),
            Information = info,
        });
    }
}
