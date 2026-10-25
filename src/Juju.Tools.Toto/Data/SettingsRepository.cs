using System.Globalization;
using System.Drawing;
using Toto.App.Domain;

namespace Toto.App.Data;

/// <summary>负责应用设置和计划弹窗标记的 INI 持久化。</summary>
/// <remarks>圆括号中的 <c>AppPaths paths</c> 是 C# 12 主构造函数：它直接为实例成员提供参数，不同于 Java 必须显式声明构造函数体。</remarks>
internal sealed class SettingsRepository(AppPaths paths)
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["hotkey"] = "None", ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q",
        ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D",
        ["shortcut_edit"] = "Alt+E", ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C",
        ["default_remind_minutes"] = "5", ["auto_start"] = "0", ["daily_review_start_time"] = "",
        ["daily_review_end_time"] = "", ["daily_review_workdays"] = "0"
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
            var general = ini.GetSection("General");
            foreach (var (key, value) in general)
                if (Defaults.ContainsKey(key))
                    result[key] = value;
            var migrated = false;
            if (!general.ContainsKey("daily_review_start_time"))
            {
                result["daily_review_start_time"] = MigrateLegacyTime(general, "work_start_popup_enabled", "work_start_time");
                migrated = true;
            }
            if (!general.ContainsKey("daily_review_end_time"))
            {
                result["daily_review_end_time"] = MigrateLegacyTime(general, "work_end_popup_enabled", "work_end_time");
                migrated = true;
            }
            if (!general.ContainsKey("daily_review_workdays"))
            {
                result["daily_review_workdays"] = "0";
                migrated = true;
            }
            result["daily_review_start_time"] = NormalizeOptionalTime(result["daily_review_start_time"]);
            result["daily_review_end_time"] = NormalizeOptionalTime(result["daily_review_end_time"]);
            result["daily_review_workdays"] = result["daily_review_workdays"] == "1" ? "1" : "0";
            if (migrated) SaveCore(result);
            return result;
        }
    }

    private static string MigrateLegacyTime(IReadOnlyDictionary<string, string> general, string enabledKey, string timeKey)
    {
        if (general.GetValueOrDefault(enabledKey) != "1") return "";
        return NormalizeOptionalTime(general.GetValueOrDefault(timeKey));
    }

    private static string NormalizeOptionalTime(string? value) =>
        TimeOnly.TryParse(value, out var time) ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : "";

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

    /// <summary>保存盘点配置，并仅清除今天已过去提醒的对应标记（当新配置时间仍在未来时）。</summary>
    public void SaveDailyReviewSettings(IReadOnlyDictionary<string, string> values, DateTime now)
    {
        lock (gate)
        {
            var ini = IniFile.Load(paths.ConfigPath);
            var general = ini.GetSection("General");
            var startChanged = NormalizeOptionalTime(general.GetValueOrDefault("daily_review_start_time")) !=
                               NormalizeOptionalTime(values.GetValueOrDefault("daily_review_start_time"));
            var endChanged = NormalizeOptionalTime(general.GetValueOrDefault("daily_review_end_time")) !=
                             NormalizeOptionalTime(values.GetValueOrDefault("daily_review_end_time"));
            SaveGeneral(ini, values);

            var today = DateOnly.FromDateTime(now);
            if (startChanged && IsFuture(values.GetValueOrDefault("daily_review_start_time"), now))
                ini.Remove("ScheduledPopups", PopupKey(today, ScheduledPopupKind.WorkStart));
            if (endChanged && IsFuture(values.GetValueOrDefault("daily_review_end_time"), now))
                ini.Remove("ScheduledPopups", PopupKey(today, ScheduledPopupKind.WorkEnd));
            ini.SaveAtomically(paths.ConfigPath);
        }
    }

    private static bool IsFuture(string? value, DateTime now) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
        time.ToTimeSpan() > now.TimeOfDay;

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
        SaveGeneral(ini, settings);
        ini.SaveAtomically(paths.ConfigPath);
    }

    private static void SaveGeneral(IniFile ini, IReadOnlyDictionary<string, string> settings)
    {
        foreach (var (key, defaultValue) in Defaults)
            ini.Set("General", key, settings.GetValueOrDefault(key, defaultValue));
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
