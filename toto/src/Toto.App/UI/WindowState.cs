using Toto.App.Data;

namespace Toto.App.UI;

/// <summary>恢复并保存业务窗口的位置和普通窗口大小。</summary>
internal static class WindowStateTracker
{
    /// <summary>为窗体启用位置记忆；没有可用记录时首次显示在屏幕中央。</summary>
    public static void RestoreAndTrack(Form form, SettingsRepository settings, string key)
    {
        form.StartPosition = FormStartPosition.CenterScreen;
        if (settings.LoadWindowBounds(key) is { } saved && TryFitOnScreen(form, saved, out var bounds))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = bounds;
        }

        form.FormClosing += (_, _) => settings.SaveWindowBounds(key, CurrentBounds(form));
    }

    /// <summary>获取最大化或最小化前的普通边界，避免将特殊窗口状态写入配置。</summary>
    private static Rectangle CurrentBounds(Form form) =>
        form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;

    /// <summary>仅恢复仍在可用显示器内的边界，并收缩过大的窗口以适应当前工作区。</summary>
    private static bool TryFitOnScreen(Form form, Rectangle saved, out Rectangle result)
    {
        result = Rectangle.Empty;
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(saved))) return false;

        var area = Screen.FromRectangle(saved).WorkingArea;
        var width = Math.Min(Math.Max(saved.Width, Math.Max(1, form.MinimumSize.Width)), area.Width);
        var height = Math.Min(Math.Max(saved.Height, Math.Max(1, form.MinimumSize.Height)), area.Height);
        var left = Math.Clamp(saved.Left, area.Left - width + 64, area.Right - 64);
        var top = Math.Clamp(saved.Top, area.Top - height + 64, area.Bottom - 64);
        result = new Rectangle(left, top, width, height);
        return true;
    }
}
