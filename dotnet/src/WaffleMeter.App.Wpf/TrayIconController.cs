using System.IO;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// System-tray icon (Kotlin BrowserApp setupTray): toggle show/hide, recover overlay input, exit.
/// Uses WinForms NotifyIcon (needs an Icon, so falls back to the system app icon when no .ico is
/// bundled). All actions marshal window work to the dispatcher.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    /// <param name="loadPacketLog">Dev builds only — replays a recorded capture so the history/detail
    /// windows have a battle to show without running a dungeon. App passes null in release builds.</param>
    /// <param name="openAetherList">Toggle the 컨텐츠 관리 panel. The footer 오드 badge is its other entry
    /// point, but that badge is hidden while 오드 표시 is off or before the first broadcast of a session — and
    /// the list is about the OTHER characters, so it has to stay reachable when the badge isn't there.</param>
    public TrayIconController(OverlayWindow window, OverlayController controller, Action exit,
        Action? openReplay = null, Action? loadPacketLog = null, Action? openAetherList = null)
    {
        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = window.Title,
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("보이기/숨기기", null, (_, _) => window.Dispatcher.Invoke(controller.ToggleVisibility));
        menu.Items.Add("오버레이 입력 복구", null, (_, _) => window.Dispatcher.Invoke(() =>
        {
            window.SetClickThrough(false);
            controller.Present();
        }));
        if (openAetherList is not null)
        {
            menu.Items.Add("컨텐츠 관리", null, (_, _) => window.Dispatcher.Invoke(openAetherList));
        }

        // Only present when movement recording is enabled (replay.recordMovement=true); App passes null otherwise.
        if (openReplay is not null)
        {
            menu.Items.Add("전투 리플레이 (직전 전투)", null, (_, _) => window.Dispatcher.Invoke(openReplay));
        }

        if (loadPacketLog is not null)
        {
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("[개발] 패킷 로그 불러오기", null, (_, _) => window.Dispatcher.Invoke(loadPacketLog));
        }

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) =>
        {
            _icon.Visible = false;
            exit();
        });
        _icon.ContextMenuStrip = menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                window.Dispatcher.Invoke(controller.ToggleVisibility);
            }
        };
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "waffle.ico");
            if (File.Exists(path))
            {
                return new Drawing.Icon(path);
            }
        }
        catch
        {
            // fall through to the system icon
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

