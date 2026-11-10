// Значок у системному треї (область сповіщень біля годинника) з контекстним меню.
// Створюється в App. WPF власного значка трею не має, тому беремо NotifyIcon з WinForms.
// Іконка малюється в коді, щоб не тримати бінарних файлів у репозиторії.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScrobbleMe.UI;

public sealed class TrayIcon : IDisposable
{
    // Обмеження Windows на текст підказки — 127 символів.
    private const int MaxTooltip = 120;

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? PauseToggled;
    public event Action? ExitRequested;

    public Icon AppIcon { get; }

    public TrayIcon()
    {
        AppIcon = CreateIcon();

        _pauseItem = new ToolStripMenuItem("Призупинити скроблінг", null, (_, _) => PauseToggled?.Invoke());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Відкрити", null, (_, _) => OpenRequested?.Invoke()) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("Налаштування…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Вийти", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "ScrobbleMe",
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Лівий клік — відкрити віконце; правий — меню (стандартна поведінка трею).
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public void SetTooltip(string text) =>
        _icon.Text = text.Length > MaxTooltip ? text[..(MaxTooltip - 1)] + "…" : text;

    public void SetPaused(bool paused) =>
        _pauseItem.Text = paused ? "Відновити скроблінг" : "Призупинити скроблінг";

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.None);

    /// <summary>Червоне коло з нотою — фірмовий колір last.fm, щоб значок легко знаходився в треї.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0xD5, 0x10, 0x07));
            g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI Symbol", 19, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("♪", font, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
