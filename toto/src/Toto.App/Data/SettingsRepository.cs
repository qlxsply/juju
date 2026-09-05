using System.Globalization;
using Toto.App.Domain;

namespace Toto.App.Data;

/// <summary>负责应用设置和计划弹窗标记的 INI 持久化。</summary>
/// <remarks>圆括号中的 <c>AppPaths paths</c> 是 C# 12 主构造函数：它直接为实例成员提供参数，不同于 Java 必须显式声明构造函数体。</remarks>
internal sealed class SettingsRepository(AppPaths paths)
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["hotkey"] = "Ctrl+Alt+Space", ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q",
        ["shortcut_settings"] = "Alt+S", ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D",
        ["shortcut_edit"] = "Alt+E", ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C",
        ["default_remind_minutes"] = "5", ["auto_start"] = "0", ["work_start_popup_enabled"] = "0",
        ["work_end_popup_enabled"] = "0", ["work_start_time"] = "09:00", ["work_end_time"] = "18:00"
    };

    private readonly Lock gate = new();

    /// <summary>加载设置，并为缺失的受支持键补充默认值。</summary>
    public IReadOnlyDictionary<string, string> Load()
    {
        // lock 以 gate 为互斥对象，类似 synchronized；作用域退出时自动释放监视器锁。
        lock (gate)
        {
            var result = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
            var ini = IniFile.Load(paths.ConfigPath);
            foreach (var (key, value) in ini.GetSection("General"))
                if (Defaults.ContainsKey(key))
                    result[key] = value;
            return result;
        }
    }

    /// <summary>确保配置文件存在；首次调用时写入默认设置。</summary>
    public void EnsureExists()
    {
        lock (gate)
            if (!File.Exists(paths.ConfigPath))
                SaveCore(Defaults);
    }

    /// <summary>保存受支持的设置键，并保留 INI 中其他节的数据。</summary>
    public void Save(IReadOnlyDictionary<string, string> settings)
    {
        lock (gate) SaveCore(settings);
    }

    /// <summary>读取指定窗口上次正常显示时的边界；未记录或格式无效时返回空。</summary>
    public Rectangle? LoadWindowBounds(string window)
    {
        lock (gate)
        {
            var text = IniFile.Load(paths.ConfigPath).Get("WindowState", window);
            var values = text?.Split(',');
            if (values is not { Length: 4 } || !int.TryParse(values[0], out var left) ||
                !int.TryParse(values[1], out var top) || !int.TryParse(values[2], out var width) ||
                !int.TryParse(values[3], out var height) || width <= 0 || height <= 0)
                return null;
            return new Rectangle(left, top, width, height);
        }
    }

    /// <summary>保存窗口上次正常显示时的左上角位置和大小。</summary>
    public void SaveWindowBounds(string window, Rectangle bounds)
    {
        lock (gate)
        {
            var ini = IniFile.Load(paths.ConfigPath);
            ini.Set("WindowState", window, $"{bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}");
            ini.SaveAtomically(paths.ConfigPath);
        }
    }

    /// <summary>判断指定日期和弹窗类型是否已经显示过。</summary>
    public bool WasScheduledPopupShown(DateOnly date, ScheduledPopupKind kind)
    {
        lock (gate) return IniFile.Load(paths.ConfigPath).Get("ScheduledPopups", PopupKey(date, kind)) is not null;
    }

    /// <summary>原子地记录计划弹窗已显示；若已记录则返回 <see langword="false"/>。</summary>
    public bool TryMarkScheduledPopupShown(DateOnly date, ScheduledPopupKind kind, DateTime shownAt)
    {
        lock (gate)
        {
            var ini = IniFile.Load(paths.ConfigPath);
            // 仅保留当天的上班/下班标记，避免计划弹窗状态按日期无限增长。
            var expired = ini.GetSection("ScheduledPopups").Keys
                .Where(key => IsExpiredPopupKey(key, date))
                .ToArray();
            foreach (var expiredKey in expired) ini.Remove("ScheduledPopups", expiredKey);

            var key = PopupKey(date, kind);
            if (ini.Get("ScheduledPopups", key) is not null)
            {
                if (expired.Length > 0) ini.SaveAtomically(paths.ConfigPath);
                return false;
            }
            ini.Set("ScheduledPopups", key, DateTimeText.Text(shownAt));
            ini.SaveAtomically(paths.ConfigPath);
            return true;
        }
    }

    /// <summary>在调用方已持有互斥锁时执行实际写入。</summary>
    private void SaveCore(IReadOnlyDictionary<string, string> settings)
    {
        var ini = IniFile.Load(paths.ConfigPath);
        foreach (var (key, defaultValue) in Defaults)
            ini.Set("General", key, settings.GetValueOrDefault(key, defaultValue));
        ini.SaveAtomically(paths.ConfigPath);
    }

    /// <summary>生成 INI 中用于标识一次计划弹窗的稳定键。</summary>
    private static string PopupKey(DateOnly date, ScheduledPopupKind kind) =>
        $"{date:yyyy-MM-dd}.{(kind == ScheduledPopupKind.WorkStart ? "work_start" : "work_end")}";

    /// <summary>判断是否为早于当前日期的已知计划弹窗标记；未知键不会被清理。</summary>
    private static bool IsExpiredPopupKey(string key, DateOnly currentDate)
    {
        const string workStart = ".work_start";
        const string workEnd = ".work_end";
        if (!key.EndsWith(workStart, StringComparison.Ordinal) && !key.EndsWith(workEnd, StringComparison.Ordinal)) return false;

        var dateText = key[..key.IndexOf('.')];
        return DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var recordedDate) && recordedDate < currentDate;
    }
}
