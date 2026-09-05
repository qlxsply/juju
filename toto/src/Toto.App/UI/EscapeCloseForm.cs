namespace Toto.App.UI;

/// <summary>为 Toto 的业务窗口提供统一的 Escape 关闭行为。</summary>
internal class EscapeCloseForm : Form
{
    /// <summary>从嵌入 <c>my_icon.ico</c> 的应用 EXE 读取一次，供所有窗口共用。</summary>
    private static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

    protected EscapeCloseForm()
    {
        Icon = AppIcon;
    }

    /// <summary>在控件处理按键前关闭当前窗口，确保 Esc 与标题栏关闭按钮走同一关闭流程。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);

        Close();
        return true;
    }
}
