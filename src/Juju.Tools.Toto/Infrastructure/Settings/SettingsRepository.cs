using System.Globalization;
using System.Text.Json;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Infrastructure.Storage;
using Toto.App.Data;
using Toto.App.Domain;

namespace Juju.Tools.Toto.Infrastructure.Settings;

/// <summary>负责应用设置、计划弹窗标记和历史归档进度的 JSON 持久化。</summary>
internal sealed class SettingsRepository(TotoPaths paths) : ITotoSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["hotkey"] = "None", ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q",
        ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D",
        ["shortcut_edit"] = "Alt+E", ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C",
        ["default_remind_minutes"] = "5", ["daily_review_start_time"] = "",
        ["daily_review_end_time"] = "", ["daily_review_workdays"] = "0"
    };

    private readonly Lock gate = new();

    /// <summary>加载支持的设置，并为缺失键补充默认值。</summary>
    public IReadOnlyDictionary<string, string> Load()
    {
        lock (gate)
        {
            var configuration = LoadCore();
            var result = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in configuration.Settings)
                if (Defaults.ContainsKey(key)) result[key] = value;
            result["daily_review_start_time"] = NormalizeOptionalTime(result["daily_review_start_time"]);
            result["daily_review_end_time"] = NormalizeOptionalTime(result["daily_review_end_time"]);
            result["daily_review_workdays"] = result["daily_review_workdays"] == "1" ? "1" : "0";
            return result;
        }
    }

    public string ArchiveProgressMonth
    {
        get
        {
            lock (gate)
            {
                var value = LoadCore().ArchiveProgressMonth;
                return IsValidArchiveMonth(value) ? value : "";
            }
        }
    }

    /// <summary>确保配置文件存在；首次调用时写入默认设置。</summary>
    public void EnsureExists()
    {
        lock (gate)
            if (!File.Exists(paths.ConfigPath)) SaveCore(Defaults);
    }

    /// <summary>保存受支持的设置键。</summary>
    public void Save(IReadOnlyDictionary<string, string> settings)
    {
        lock (gate) SaveCore(settings);
    }

    /// <summary>保存盘点配置，并仅清除今天已过去提醒的对应标记（当新配置时间仍在未来时）。</summary>
    public void SaveDailyReviewSettings(IReadOnlyDictionary<string, string> values, DateTime now)
    {
        lock (gate)
        {
            var configuration = LoadCore();
            var startChanged = NormalizeOptionalTime(configuration.Settings.GetValueOrDefault("daily_review_start_time")) !=
                               NormalizeOptionalTime(values.GetValueOrDefault("daily_review_start_time"));
            var endChanged = NormalizeOptionalTime(configuration.Settings.GetValueOrDefault("daily_review_end_time")) !=
                             NormalizeOptionalTime(values.GetValueOrDefault("daily_review_end_time"));
            SaveGeneral(configuration, values);

            var today = DateOnly.FromDateTime(now);
            if (startChanged && IsFuture(values.GetValueOrDefault("daily_review_start_time"), now))
                configuration.ScheduledPopups.Remove(PopupKey(today, ScheduledPopupKind.WorkStart));
            if (endChanged && IsFuture(values.GetValueOrDefault("daily_review_end_time"), now))
                configuration.ScheduledPopups.Remove(PopupKey(today, ScheduledPopupKind.WorkEnd));
            SaveCore(configuration);
        }
    }

    /// <summary>记录成功完成的历史归档截止月份。</summary>
    public void SaveArchiveProgressMonth(string month)
    {
        if (!IsValidArchiveMonth(month)) throw new ArgumentException("归档月份必须使用 yyyyMM 格式。", nameof(month));
        lock (gate)
        {
            var configuration = LoadCore();
            configuration.ArchiveProgressMonth = month;
            SaveCore(configuration);
        }
    }

    private static bool IsFuture(string? value, DateTime now) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
        time.ToTimeSpan() > now.TimeOfDay;

    /// <summary>判断指定日期和弹窗类型是否已经显示过。</summary>
    public bool WasScheduledPopupShown(DateOnly date, ScheduledPopupKind kind)
    {
        lock (gate) return LoadCore().ScheduledPopups.ContainsKey(PopupKey(date, kind));
    }

    /// <summary>原子地记录计划弹窗已显示；若已记录则返回 <see langword="false"/>。</summary>
    public bool TryMarkScheduledPopupShown(DateOnly date, ScheduledPopupKind kind, DateTime shownAt)
    {
        lock (gate)
        {
            var configuration = LoadCore();
            var expired = configuration.ScheduledPopups.Keys.Where(key => IsExpiredPopupKey(key, date)).ToArray();
            foreach (var expiredKey in expired) configuration.ScheduledPopups.Remove(expiredKey);

            var key = PopupKey(date, kind);
            if (configuration.ScheduledPopups.ContainsKey(key))
            {
                if (expired.Length > 0) SaveCore(configuration);
                return false;
            }
            configuration.ScheduledPopups[key] = DateTimeText.Text(shownAt);
            SaveCore(configuration);
            return true;
        }
    }

    private static string NormalizeOptionalTime(string? value) =>
        TimeOnly.TryParse(value, out var time) ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : "";

    private static bool IsValidArchiveMonth(string? value) => value is { Length: 6 } &&
        DateOnly.TryParseExact(value + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private Configuration LoadCore()
    {
        if (!File.Exists(paths.ConfigPath)) return new Configuration();
        var configuration = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(paths.ConfigPath), JsonOptions)
                            ?? new Configuration();
        configuration.Settings ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        configuration.ScheduledPopups ??= new Dictionary<string, string>(StringComparer.Ordinal);
        configuration.ArchiveProgressMonth ??= "";
        return configuration;
    }

    private void SaveCore(IReadOnlyDictionary<string, string> settings)
    {
        var configuration = LoadCore();
        SaveGeneral(configuration, settings);
        SaveCore(configuration);
    }

    private static void SaveGeneral(Configuration configuration, IReadOnlyDictionary<string, string> settings)
    {
        configuration.Settings.Clear();
        foreach (var (key, defaultValue) in Defaults)
            configuration.Settings[key] = settings.GetValueOrDefault(key, defaultValue);
    }

    private void SaveCore(Configuration configuration)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var temporary = paths.ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporary, paths.ConfigPath, true);
    }

    private static string PopupKey(DateOnly date, ScheduledPopupKind kind) =>
        $"{date:yyyy-MM-dd}.{(kind == ScheduledPopupKind.WorkStart ? "work_start" : "work_end")}";

    private static bool IsExpiredPopupKey(string key, DateOnly currentDate)
    {
        const string workStart = ".work_start";
        const string workEnd = ".work_end";
        if (!key.EndsWith(workStart, StringComparison.Ordinal) && !key.EndsWith(workEnd, StringComparison.Ordinal))
            return false;
        var dateText = key[..key.IndexOf('.')];
        return DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var recordedDate) && recordedDate < currentDate;
    }

    private sealed class Configuration
    {
        public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ScheduledPopups { get; set; } = new(StringComparer.Ordinal);
        public string ArchiveProgressMonth { get; set; } = "";
    }
}
