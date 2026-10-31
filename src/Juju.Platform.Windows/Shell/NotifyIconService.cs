using System.Drawing;
using System.Windows.Forms;
using Juju.Core.Contracts.Platform;

namespace Juju.Platform.Windows.Shell;

/// <summary>Windows 通知区域图标和统一的五项菜单。</summary>
public sealed class NotifyIconService : ITrayIconService
{
    private readonly NotifyIcon _icon;
    private readonly Icon _trayImage;
    private readonly ContextMenuStrip _menu;
    private bool _disposed;

    public event Action<TrayCommand>? CommandRequested;

    public NotifyIconService(byte[] iconBytes)
    {
        using var stream = new MemoryStream(iconBytes, writable: false);
        using var image = new Icon(stream, SystemInformation.SmallIconSize);
        _trayImage = (Icon)image.Clone();

        _menu = new ContextMenuStrip
        {
            AutoSize = false,
            Size = new Size(184, 174),
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new Padding(2),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(47, 57, 73),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Renderer = new FlatTrayMenuRenderer()
        };
        _menu.Items.Add(CreateItem("显示主界面", TrayCommand.ShowLauncher));
        _menu.Items.Add(CreateItem("设置", TrayCommand.ShowSettings));
        _menu.Items.Add(CreateItem("帮助", TrayCommand.Help));
        _menu.Items.Add(CreateItem("检查更新", TrayCommand.CheckForUpdates));
        _menu.Items.Add(CreateItem("退出", TrayCommand.Exit));

        _icon = new NotifyIcon { Text = "juju Toolbox", Icon = _trayImage, ContextMenuStrip = _menu, Visible = true };
        _icon.MouseClick += OnMouseClick;
    }

    private ToolStripMenuItem CreateItem(string text, TrayCommand command) =>
        new ToolStripMenuItem(text, null, (_, _) => CommandRequested?.Invoke(command))
        {
            AutoSize = false,
            Size = new Size(180, 34),
            Margin = Padding.Empty,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.White
        };

    private void OnMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left) CommandRequested?.Invoke(TrayCommand.ShowLauncher);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.MouseClick -= OnMouseClick;
        _icon.Visible = false;
        _menu.Dispose();
        _icon.Dispose();
        _trayImage.Dispose();
    }

    private sealed class FlatTrayMenuRenderer() : ToolStripProfessionalRenderer(new FlatMenuColorTable())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Color.White);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(215, 221, 231));
            var bounds = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, bounds.X, bounds.Y, Math.Max(0, bounds.Width - 1),
                Math.Max(0, bounds.Height - 1));
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            var color = e.Item.Pressed ? Color.FromArgb(223, 231, 245) :
                e.Item.Selected ? Color.FromArgb(239, 243, 250) : Color.White;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, bounds);
            if (e.Item.Selected)
            {
                using var indicator = new SolidBrush(Color.FromArgb(72, 120, 237));
                e.Graphics.FillRectangle(indicator, 0, 5, 2, Math.Max(1, bounds.Height - 10));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var bounds = e.TextRectangle;
            bounds.Y = 0;
            bounds.Height = e.Item.Height;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, e.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    private sealed class FlatMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuBorder => Color.FromArgb(215, 221, 231);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Color.FromArgb(239, 243, 250);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(239, 243, 250);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(239, 243, 250);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(223, 231, 245);
        public override Color MenuItemPressedGradientMiddle => Color.FromArgb(223, 231, 245);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(223, 231, 245);
    }
}
