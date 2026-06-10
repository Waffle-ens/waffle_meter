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

    public TrayIconController(OverlayWindow window, OverlayController controller, Action exit)
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

