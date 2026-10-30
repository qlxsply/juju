using System.Drawing;
using System.Windows.Forms;
using Juju.App.Contracts.Windows;

namespace Juju.App.Bootstrap;

/// <summary>
/// Juju 的唯一通知区域图标和五项主菜单。由 ToolStripProfessionalRenderer 自绘成平面浅色菜单，
/// 不创建 JSON/Toto 的分工具托盘入口，也不另设工具级菜单或设置入口。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _trayImage;
    private readonly ContextMenuStrip _menu;

    public TrayService(IAppWindowManager windows, Action exit)
    {
        using var stream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/juju.ico"))!.Stream;
        using var image = new Icon(stream, SystemInformation.SmallIconSize);
        _trayImage = (Icon)image.Clone();

        _menu = new ContextMenuStrip
        {
            // 纯文字菜单：关闭图标列，固定每行尺寸和左右内边距，文本垂直居中、统一左对齐。
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
        _menu.Items.Add(CreateItem("显示主界面", (_, _) => windows.ShowLauncher()));
        _menu.Items.Add(CreateItem("设置", (_, _) => windows.ShowSettings()));
        _menu.Items.Add(CreateItem("帮助", (_, _) => ShowHelp()));
        _menu.Items.Add(CreateItem("检查更新", (_, _) => ShowUpdateStatus()));
        _menu.Items.Add(CreateItem("退出", (_, _) => exit()));

        _icon = new NotifyIcon { Text = "juju Toolbox", Icon = _trayImage, ContextMenuStrip = _menu, Visible = true };
        // 左键点击托盘图标直接唤出统一的工具箱入口；右键使用上方五项菜单。
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) windows.ShowLauncher();
        };
    }

    private static ToolStripMenuItem CreateItem(string text, EventHandler click) =>
        new ToolStripMenuItem(text, null, click)
        {
            AutoSize = false,
            Size = new Size(180, 34),
            Margin = Padding.Empty,
            Padding = new Padding(14, 0, 14, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.White
        };

    private static void ShowHelp() =>
        MessageBox.Show("juju Toolbox\n\n显示主界面可打开工具列表；应用设置集中管理工具箱和各工具选项。",
            "帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void ShowUpdateStatus() =>
        MessageBox.Show("当前版本没有配置在线更新服务。请使用 Toolbox 安装包更新应用。",
            "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public void Dispose()
    {
        _icon.Visible = false;
        _menu.Dispose();
        _icon.Dispose();
        _trayImage.Dispose();
    }

    /// <summary>FlatMenuColorTable 提供浅色底、细边框和低对比 hover，不使用 Office 渐变/凸起边框。</summary>
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
            // ToolStripMenuItem.TextAlign 在部分 DPI/Renderer 组合下仍按顶部基线排字；
            // 用整行高度显式垂直居中，并沿用 TextRectangle 的左侧 padding 对齐。
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
