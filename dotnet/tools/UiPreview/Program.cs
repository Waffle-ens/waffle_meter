using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WaffleMeter.App.Core;
using WaffleMeter.App.Wpf;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Replay;
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
        // Shared overlay chrome (HeaderIconButton/HeaderCloseButton) — merged app-wide by App.xaml in the
        // real app; the preview host must merge it too so windows resolve the StaticResource styles.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WaffleMeter.App.Wpf;component/Themes/PanelChrome.xaml"),
        });

        VerifySettings();

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
                // gauge-form variants: "fill" (above), "bar" (thin bottom bar), "none".
                foreach (string bs in new[] { "bar", "none" })
                {
                    settings.BarStyle = bs;
                    var bv = new OverlayViewModel("1.7.8", settings, theme) { Status = "캡처 중" };
                    bv.Update(SampleMeterReport(now));
                    Capture(() => new OverlayWindow { DataContext = bv }, palette, Path.Combine(outDir, $"meter_gauge_{bs}_Dark.png"));
                }
                settings.BarStyle = "fill";

                // font test: a visually-distinct BUNDLED font must actually reach the row text (item 3).
                string savedFont = settings.FontFamily;
                settings.FontFamily = "Tmoney RoundWind ExtraBold";
                var fv = new OverlayViewModel("1.7.8", settings, theme) { Status = "캡처 중" };
                fv.SetRecognized(true, "콘팡"); // also exercise the "캐릭터 인식됨" indicator (item 5)
                fv.Update(SampleMeterReport(now));
                Capture(() => new OverlayWindow { DataContext = fv }, palette, Path.Combine(outDir, "meter_font_Dark.png"));
                settings.FontFamily = savedFont;

                // idle case: durationMs>0 but 0 rows — must NOT stack placeholder + combat-timer pill.
                var idle = new OverlayViewModel("1.7.8", settings, theme) { Status = "캡처 헬퍼 시작 실패: NotFound" };
                idle.Update(new DpsReport { BattleStart = 0, BattleEnd = 5000 });
                Capture(() => new OverlayWindow { DataContext = idle }, palette, Path.Combine(outDir, "meter_idle_Dark.png"));

                // Detail window chrome (skill table is empty without packet data, but verifies re-theming).
                var calc = new WaffleMeter.Data.DpsCalculator(new WaffleMeter.Data.DataManager(), () => { });
                var details = new DetailsViewModel(SampleMeterReport(now), 1, calc, "콘팡", theme, settings.FontFamily);
                details.Refresh(SampleMeterReport(now));
                Capture(() => new DetailWindow { DataContext = details }, palette, Path.Combine(outDir, "detail_Dark.png"));

                var buffVm = new BuffOverlayViewModel();
                buffVm.Update(new List<(int, string, long, long, bool)>
                {
                    (18290000, "회전격", 12_000, 30_000, false),
                    (11400000, "축복", 45_000, 60_000, true),
                    (13050000, "섬광베기", 6_000, 20_000, false),
                });
                Capture(() => new BuffOverlayPanel(buffVm), palette, Path.Combine(outDir, "buffoverlay_Dark.png"));

                // Per-job buff picker: seed a small observed catalog so the grouped list renders.
                var pickerData = new DataManager();
                pickerData.LoadBuffNames(new (int, string, string)[]
                {
                    (11100000, "파멸의 맹타", "검성"), (11110000, "집중 막기", "검성"), (11800000, "살기 파열", "검성"),
                    (11390000, "격노 폭발", "검성"), (14050000, "송곳 화살", "궁성"), (14060000, "그리폰 화살", "궁성"),
                });
                pickerData.SeedObservedBuffBases(new[] { 11100000, 11110000, 11800000, 11390000, 14050000, 14060000 });
                var pickerVm = new BuffPickerViewModel(pickerData, settings);
                Capture(() => new BuffPickerWindow(pickerVm), palette, Path.Combine(outDir, "buffpicker_Dark.png"));

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

                // settings window (hotkeys tab) — verify the HotkeyCaptureBox now matches the dark style.
                string sdir = Path.Combine(Path.GetTempPath(), "waffle_settings_preview");
                Directory.CreateDirectory(sdir);
                var sp = new PropertyHandler(sdir);
                var ssvc = new MeterServices(sp);
                var svm = new SettingsViewModel(ssvc, new MeterSettings(sp), new MeterColorTheme(sp), new SkinManager(sp),
                    new OverlayController(new OverlayWindow(), sp), new HotkeyHandler(sp)) { SelectedNav = "display" };
                Capture(() => new SettingsWindow(svm), palette, Path.Combine(outDir, "settings_display_Dark.png"), fixedSize: true);
            }
        }

        CaptureReplay(LoadRealOrSynthetic(now), Path.Combine(outDir, "replay.png"), "replay.png");
        CaptureReplay(SampleMapReplay(now), Path.Combine(outDir, "replay-map.png"), "replay-map.png");

        Console.WriteLine(outDir);
        app.Shutdown();
    }

    /// <summary>Render the positional-replay window (paused mid-battle so paths/trails show) to PNG.</summary>
    private static void CaptureReplay(ReplayRecording rec, string path, string label)
    {
        ReplayWindow? win = null;
        try
        {
            win = new ReplayWindow(rec, autoPlay: false, startMs: rec.DurationMs * 0.5);
            win.Width = 940;
            win.Height = 660;
            win.Left = -10000;
            win.Top = -10000;
            win.Show();
            Drain(win.Dispatcher);

            var content = (FrameworkElement)win.Content;
            int w = (int)Math.Ceiling(content.ActualWidth);
            int h = (int)Math.Ceiling(content.ActualHeight);
            if (w <= 0 || h <= 0)
            {
                Console.WriteLine($"  [skip] {label} measured {w}x{h}");
                return;
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using FileStream fs = File.Create(path);
            encoder.Save(fs);
            Console.WriteLine($"  [ok]   {label} {w}x{h}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [fail] {label}: {ex.Message}");
        }
        finally
        {
            win?.Close();
        }
    }

    /// <summary>A synthetic battle anchored in a real dungeon's WORLD coordinates (무스펠의 성배, boss code
    /// 2301059) so the preview exercises the map-background projection: dots must land on the map art.</summary>
    private static ReplayRecording SampleMapReplay(long now)
    {
        string[] names = { "콘팡", "쌈", "강까", "노까", "빛의사제" };
        string[] jobs = { "권성", "검성", "궁성", "치유성", "호법성" };
        const int dur = 120_000;
        // A pull staged near the center of 무스펠 (world X ~18k, Y ~ -9k), spanning a few thousand units.
        const double baseX = 20000, baseY = -8000;
        var tracks = new List<ReplayTrack>();

        for (int k = 0; k < names.Length; k++)
        {
            var pts = new List<ReplayPoint>();
            double cx = baseX + (k * 700), cy = baseY + ((k % 2) * 900), r = 1200 + (k * 260);
            for (int t = 0; t <= dur; t += 400)
            {
                double a = (t / 1000.0) * (0.4 + (k * 0.05));
                float x = (float)(cx + (Math.Cos(a) * r) + (Math.Sin(t / 7000.0) * 500));
                float y = (float)(cy + (Math.Sin(a) * r) + (Math.Cos(t / 9000.0) * 500));
                float z = (float)(2000 + (200 * Math.Sin((t / 15000.0) + k)));
                pts.Add(new ReplayPoint(t, x, y, z));
            }

            tracks.Add(new ReplayTrack
            {
                Uid = 100 + k,
                Nickname = names[k],
                Server = 2003,
                Job = jobs[k],
                IsSelf = k == 0,
                PartySlot = k + 1,
                Points = pts,
                SourceOpcode = 0x371C,
                SourceOffset = 2,
            });
        }

        var boss = new List<ReplayPoint>();
        for (int t = 0; t <= dur; t += 800)
        {
            boss.Add(new ReplayPoint(t, (float)(baseX + (Math.Sin(t / 20000.0) * 700)), (float)(baseY + (Math.Cos(t / 20000.0) * 700)), 2000f));
        }

        tracks.Add(new ReplayTrack { Uid = 999, Nickname = "칼드릭스", IsTarget = true, Points = boss, SourceOpcode = 0x372F, SourceOffset = 1 });

        return new ReplayRecording
        {
            StartMs = now - dur,
            EndMs = now,
            BossDefeated = true,
            TargetName = "칼드릭스",
            TargetCode = 2301059, // 무스펠의 성배 -> matches the bundled map
            Tracks = tracks,
        };
    }

    /// <summary>Prefer the newest real persisted recording (shows true sampling gaps), else synthetic.</summary>
    private static ReplayRecording LoadRealOrSynthetic(long now)
    {
        try
        {
            string dir = Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "waffle_meter.v1.4", "replays");
            if (Directory.Exists(dir))
            {
                FileInfo? f = new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (f != null)
                {
                    Console.WriteLine($"  [info] replay.png using real recording {f.Name}");
                    return ReplaySerializer.Deserialize(File.ReadAllText(f.FullName));
                }
            }
        }
        catch
        {
            // fall back to synthetic
        }

        return SampleReplay(now);
    }

    /// <summary>A synthetic battle replay (5 players walking patterns + a drifting boss) for the preview.</summary>
    private static ReplayRecording SampleReplay(long now)
    {
        string[] names = { "콘팡", "쌈", "강까", "노까", "빛의사제" };
        string[] jobs = { "마도성", "검성", "궁성", "치유성", "호법성" };
        const int dur = 120_000;
        var tracks = new List<ReplayTrack>();

        for (int k = 0; k < names.Length; k++)
        {
            var pts = new List<ReplayPoint>();
            double cx = 200 + (k * 45), cy = 220 + ((k % 2) * 70), r = 60 + (k * 16);
            for (int t = 0; t <= dur; t += 500)
            {
                double a = (t / 1000.0) * (0.4 + (k * 0.05));
                float x = (float)(cx + (Math.Cos(a) * r) + (Math.Sin(t / 7000.0) * 30));
                float y = (float)(cy + (Math.Sin(a) * r) + (Math.Cos(t / 9000.0) * 30));
                float z = (float)(50 + (12 * Math.Sin((t / 15000.0) + k)));
                pts.Add(new ReplayPoint(t, x, y, z));
            }

            tracks.Add(new ReplayTrack
            {
                Uid = 100 + k,
                Nickname = names[k],
                Server = 2003,
                Job = jobs[k],
                IsSelf = k == 0,
                PartySlot = k + 1,
                Points = pts,
                SourceOpcode = 0x371C,
                SourceOffset = 2,
            });
        }

        var boss = new List<ReplayPoint>();
        for (int t = 0; t <= dur; t += 1000)
        {
            boss.Add(new ReplayPoint(t, (float)(280 + (Math.Sin(t / 20000.0) * 24)), (float)(260 + (Math.Cos(t / 20000.0) * 24)), 60f));
        }

        tracks.Add(new ReplayTrack { Uid = 999, Nickname = "크로메데", IsTarget = true, Points = boss, SourceOpcode = 0x372F, SourceOffset = 1 });

        return new ReplayRecording
        {
            StartMs = now - dur,
            EndMs = now,
            BossDefeated = true,
            TargetName = "크로메데의 심연",
            TargetCode = 500,
            Tracks = tracks,
        };
    }

    /// <summary>Drive every SettingsViewModel control/command against an ISOLATED settings file and assert
    /// each propagates. Prints a PASS/FAIL report — a one-cycle settings verification.</summary>
    private static void VerifySettings()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "waffle_settings_verify");
        Directory.CreateDirectory(tmp);
        foreach (string f in Directory.GetFiles(tmp))
        {
            File.Delete(f);
        }

        var props = new PropertyHandler(tmp);
        var services = new MeterServices(props);
        var settings = new MeterSettings(props);
        var theme = new MeterColorTheme(props);
        var skin = new SkinManager(props);
        var controller = new OverlayController(new OverlayWindow(), props);
        var hotkeys = new HotkeyHandler(props);
        var vm = new SettingsViewModel(services, settings, theme, skin, controller, hotkeys);

        int pass = 0, fail = 0;
        void Check(string name, bool ok)
        {
            Console.WriteLine($"  [{(ok ? "ok  " : "FAIL")}] {name}");
            if (ok)
            {
                pass++;
            }
            else
            {
                fail++;
            }
        }

        Console.WriteLine("=== settings verification cycle ===");

        vm.DisplayMode = "amount_percent"; Check("DisplayMode", settings.DisplayMode == "amount_percent" && props.GetProperty("displayMode") == "amount_percent");
        vm.DamageValueMode = "total"; Check("DamageValueMode", settings.UseTotalDamage);
        vm.ContributionMode = "entireContribution"; Check("ContributionMode", settings.UseEntireContribution);
        vm.NameDisplay = "me_only"; Check("NameDisplay", settings.NameDisplayMode == NameDisplay.MeOnly);
        vm.TargetInfoDisplayMode = "percent"; Check("TargetInfoDisplayMode", settings.TargetInfoDisplayMode == "percent");
        vm.BarStyle = "bar"; Check("BarStyle", settings.BarStyle == "bar" && props.GetProperty("barStyle") == "bar");
        vm.FontFamily = "Pretendard"; Check("FontFamily", settings.FontFamily == "Pretendard");
        vm.RowHeight = 50; Check("RowHeight", settings.RowHeight == 50 && props.GetProperty("rowHeight") == "50");
        vm.MeterOpacity = 0.7; Check("MeterOpacity", Math.Abs(settings.MeterOpacity - 0.7) < 0.001);

        vm.IsMinimal = true; Check("IsMinimal", settings.IsMinimal);
        vm.ShowCombatTimerInMinimal = false; Check("ShowCombatTimerInMinimal", !settings.ShowCombatTimerInMinimal);
        vm.ShowTargetInfoInMinimal = false; Check("ShowTargetInfoInMinimal", !settings.ShowTargetInfoInMinimal);
        vm.MultiMonitorMode = true; Check("MultiMonitorMode", settings.MultiMonitorMode);
        vm.IsAutoHide = false; Check("IsAutoHide", !controller.IsAutoHide);
        vm.TaskbarMode = true; Check("TaskbarMode", settings.TaskbarMode && controller.TaskbarMode);

        vm.CloseAction = "tray"; Check("CloseAction", settings.CloseAction == "tray");
        vm.CaptureBackend = "npcap"; Check("CaptureBackend", settings.CaptureBackend == "npcap");
        vm.ServerIp = "10.0.0.0/8"; vm.ServerPort = "7777"; vm.SaveServer();
        Check("SaveServer", props.GetProperty("server.ip") == "10.0.0.0/8" && props.GetProperty("server.port") == "7777");

        vm.Skin = "light"; Check("Skin (light)", skin.Current == "light" && skin.IsLight);

        theme.UserBarFrom = "#FF112233"; Check("Theme color set", theme.UserBarFrom == "#FF112233");
        vm.ResetTheme(); Check("ResetTheme", theme.UserBarFrom != "#FF112233");

        bool updateAsked = false; vm.CheckUpdateRequested = () => updateAsked = true; vm.CheckForUpdate(); Check("CheckForUpdate", updateAsked);
        string? resetWhich = null; vm.ResetPositionRequested = w => resetWhich = w;
        vm.ResetMeterPosition(); Check("ResetMeterPosition", resetWhich == "meter");
        vm.ResetJoinPosition(); Check("ResetJoinPosition", resetWhich == "join");
        vm.ResetHistoryPosition(); Check("ResetHistoryPosition", resetWhich == "history");

        vm.ToggleLogging(); Check("Logging start", services.DebugLogger.IsRunning);
        vm.ToggleLogging(); Check("Logging stop", !services.DebugLogger.IsRunning);

        vm.PendingReset = new HotkeyCombo(0, 0x71); vm.Commit(); // F2
        Check("Hotkey commit", hotkeys.Reset?.VkCode == 0x71);
        vm.PendingReset = null; vm.Commit(); // 미지정 round-trip
        Check("Hotkey unassign", hotkeys.Reset is null);

        vm.ResetDefaults();
        Check("ResetDefaults", settings.DisplayMode == "dps_percent" && settings.RowHeight == 36 && skin.Current == "dark");

        bool consentOk = true;
        try { vm.ConsentAccepted = false; vm.ApplyConsent(); } catch { consentOk = false; }
        Check("ApplyConsent (no crash)", consentOk);

        Console.WriteLine($"=== settings: {pass} passed, {fail} failed ===");
    }

    private static void Capture(Func<Window> factory, ResourceDictionary palette, string path, bool fixedSize = false)
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
            // fixedSize: arrange at the window's real height so overflow scrolls (shows the scrollbar).
            if (fixedSize && !double.IsNaN(window.Height) && window.Height > 0)
            {
                measuredH = window.Height;
            }

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
