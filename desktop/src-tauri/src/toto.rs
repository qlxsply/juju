use crate::settings::AppSettings;
use chrono::{Datelike, Duration, Local, NaiveDate, NaiveDateTime, NaiveTime, Weekday};
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value, json};
use std::collections::{BTreeMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};
use tauri::AppHandle;
use uuid::Uuid;

const ITEMS_FILE: &str = "toto_ing.csv";
const HISTORY_FILE: &str = "toto_end.csv";
const CSV_BOM: &[u8] = b"\xef\xbb\xbf";
const DEFAULT_SETTINGS: [(&str, &str); 11] = [
    ("hotkey", "None"),
    ("shortcut_add", "Alt+A"),
    ("shortcut_history", "Alt+Q"),
    ("shortcut_refresh", "Alt+R"),
    ("shortcut_detail", "Alt+D"),
    ("shortcut_edit", "Alt+E"),
    ("shortcut_complete", "Alt+F"),
    ("shortcut_cancel", "Alt+C"),
    ("default_remind_minutes", "5"),
    ("daily_review_start_time", ""),
    ("daily_review_end_time", ""),
];

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
pub struct TotoItem {
    #[serde(rename = "事项ID")]
    pub id: String,
    #[serde(rename = "事项内容")]
    pub content: String,
    #[serde(rename = "计划时间")]
    pub planned_at: Option<String>,
    #[serde(rename = "提醒时间")]
    pub reminder_at: Option<String>,
    #[serde(rename = "创建时间")]
    pub created_at: String,
    #[serde(rename = "创建序号")]
    pub created_order: u64,
    #[serde(rename = "提醒状态")]
    pub reminder_status: String,
    #[serde(rename = "响铃时间")]
    pub reminded_at: Option<String>,
    #[serde(rename = "备注")]
    pub notes: String,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
pub struct TotoHistoryItem {
    #[serde(rename = "事项ID")]
    pub id: String,
    #[serde(rename = "事项内容")]
    pub content: String,
    #[serde(rename = "计划时间")]
    pub planned_at: Option<String>,
    #[serde(rename = "提醒时间")]
    pub reminder_at: Option<String>,
    #[serde(rename = "创建时间")]
    pub created_at: String,
    #[serde(rename = "创建序号")]
    pub created_order: u64,
    #[serde(rename = "结束状态")]
    pub end_status: String,
    #[serde(rename = "结束时间")]
    pub ended_at: String,
    #[serde(rename = "备注")]
    pub notes: String,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TotoHoliday {
    pub name: String,
    pub date: String,
    pub is_off_day: bool,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TotoQuickPreview {
    pub content: String,
    pub planned_at: Option<String>,
    pub reminder_at: Option<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TotoDailyReview {
    pub kind: String,
    pub date: String,
    pub active_items: Vec<TotoItem>,
    pub finished_items: Vec<TotoHistoryItem>,
}

pub fn load_items(app: &AppHandle) -> Result<Vec<TotoItem>, String> {
    let path = tool_path(app, ITEMS_FILE)?;
    if !path.exists() {
        save_items_at(&path, &[])?;
    }
    let mut items: Vec<TotoItem> = read_csv(&path)?;
    let history_ids: HashSet<String> = load_history(app)?.into_iter().map(|item| item.id).collect();
    items.retain(|item| !history_ids.contains(&item.id));
    items.sort_by(|left, right| match (&left.planned_at, &right.planned_at) {
        (Some(left_time), Some(right_time)) => left_time
            .cmp(right_time)
            .then(left.created_order.cmp(&right.created_order)),
        (Some(_), None) => std::cmp::Ordering::Less,
        (None, Some(_)) => std::cmp::Ordering::Greater,
        (None, None) => left.created_order.cmp(&right.created_order),
    });
    Ok(items)
}

pub fn load_history(app: &AppHandle) -> Result<Vec<TotoHistoryItem>, String> {
    let current_path = tool_path(app, HISTORY_FILE)?;
    let mut items = load_history_at(&current_path)?;
    let directory = current_path
        .parent()
        .ok_or_else(|| "无法定位 Toto 数据目录。".to_string())?;
    for entry in fs::read_dir(directory).map_err(|error| error.to_string())? {
        let path = entry.map_err(|error| error.to_string())?.path();
        let Some(name) = path.file_name().and_then(|name| name.to_str()) else {
            continue;
        };
        if name.starts_with("toto_end_") && path.extension().is_some_and(|ext| ext == "csv") {
            items.extend(read_csv::<TotoHistoryItem>(&path)?);
        }
    }
    let mut seen = HashSet::new();
    items.retain(|item| seen.insert(item.id.clone()));
    items.sort_by(|left, right| {
        right
            .ended_at
            .cmp(&left.ended_at)
            .then(right.created_order.cmp(&left.created_order))
    });
    Ok(items)
}

pub fn archive_history(app: &AppHandle) -> Result<(), String> {
    let current_path = tool_path(app, HISTORY_FILE)?;
    let current_month = Local::now().format("%Y%m").to_string();
    let current = load_history_at(&current_path)?;
    let mut retained = Vec::new();
    let mut archived: BTreeMap<String, Vec<TotoHistoryItem>> = BTreeMap::new();
    for item in current {
        let month = item.ended_at.get(..7).unwrap_or_default().replace('-', "");
        if month.len() == 6 && month < current_month {
            archived.entry(month).or_default().push(item);
        } else {
            retained.push(item);
        }
    }
    for (month, mut moved) in archived {
        let archive_path = tool_path(app, &format!("toto_end_{month}.csv"))?;
        if archive_path.exists() {
            moved.extend(read_csv::<TotoHistoryItem>(&archive_path)?);
        }
        let mut seen = HashSet::new();
        moved.retain(|item| seen.insert(item.id.clone()));
        save_history_at(&archive_path, &moved)?;
    }
    save_history_at(&current_path, &retained)
}

pub fn load_calendar(app: &AppHandle, year: i32) -> Result<Vec<TotoHoliday>, String> {
    let path = tool_path(app, &format!("{year}.json"))?;
    if !path.exists() {
        write_json_atomic(&path, &Value::Array(Vec::new()))?;
    }
    let contents = fs::read_to_string(path).map_err(|error| error.to_string())?;
    let mut days: Vec<TotoHoliday> =
        serde_json::from_str(&contents).map_err(|error| error.to_string())?;
    validate_calendar(year, &mut days)?;
    Ok(days)
}

pub async fn ensure_calendar(app: &AppHandle, year: i32) -> Result<Vec<TotoHoliday>, String> {
    let path = tool_path(app, &format!("{year}.json"))?;
    if path.exists() {
        return load_calendar(app, year);
    }
    download_calendar(app, year).await
}

pub async fn download_calendar(app: &AppHandle, year: i32) -> Result<Vec<TotoHoliday>, String> {
    let current_year = Local::now().year();
    if !(2020..=current_year + 1).contains(&year) {
        return Err("节假日年份不在支持范围内。".into());
    }
    let url =
        format!("https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json");
    let response = reqwest::get(url)
        .await
        .map_err(|error| format!("下载节假日数据失败：{error}"))?
        .error_for_status()
        .map_err(|error| format!("下载节假日数据失败：{error}"))?;
    let mut days: Vec<TotoHoliday> = response
        .json()
        .await
        .map_err(|error| format!("解析节假日数据失败：{error}"))?;
    validate_calendar(year, &mut days)?;
    write_json_atomic(
        &tool_path(app, &format!("{year}.json"))?,
        &serde_json::to_value(&days).map_err(|error| error.to_string())?,
    )?;
    Ok(days)
}

pub fn save_calendar(app: &AppHandle, year: i32, mut days: Vec<TotoHoliday>) -> Result<(), String> {
    validate_calendar(year, &mut days)?;
    let value = serde_json::to_value(days).map_err(|error| error.to_string())?;
    write_json_atomic(&tool_path(app, &format!("{year}.json"))?, &value)
}

fn validate_calendar(year: i32, days: &mut Vec<TotoHoliday>) -> Result<(), String> {
    let mut seen = HashSet::new();
    for day in days.iter_mut() {
        day.name = day.name.trim().to_string();
        let date = chrono::NaiveDate::parse_from_str(&day.date, "%Y-%m-%d")
            .map_err(|_| format!("节假日日期无效：{}", day.date))?;
        if date.year() != year {
            return Err(format!("节假日日期必须属于 {year} 年。"));
        }
        if day.name.is_empty() {
            return Err("节假日名称不能为空。".into());
        }
        if !seen.insert(day.date.clone()) {
            return Err(format!("日期重复：{}", day.date));
        }
    }
    days.sort_by(|left, right| left.date.cmp(&right.date));
    Ok(())
}

pub fn load_default_remind_minutes(app: &AppHandle) -> Result<i64, String> {
    let config = read_config(app)?;
    let value = config
        .get("settings")
        .and_then(Value::as_object)
        .and_then(|settings| settings.get("default_remind_minutes"))
        .and_then(Value::as_str)
        .unwrap_or("5");
    value
        .parse::<i64>()
        .ok()
        .filter(|minutes| *minutes >= 0)
        .ok_or_else(|| "Toto 默认提醒分钟数配置无效。".into())
}

pub fn load_settings(app: &AppHandle) -> Result<BTreeMap<String, String>, String> {
    let config = read_config(app)?;
    let saved = config.get("settings").and_then(Value::as_object);
    let mut settings: BTreeMap<String, String> = DEFAULT_SETTINGS
        .iter()
        .map(|(key, default)| {
            let value = saved
                .and_then(|values| values.get(*key))
                .and_then(Value::as_str)
                .unwrap_or(default);
            ((*key).to_string(), value.to_string())
        })
        .collect();
    settings.insert(
        "daily_review_workdays".into(),
        saved
            .and_then(|values| values.get("daily_review_workdays"))
            .and_then(Value::as_str)
            .unwrap_or("0")
            .to_string(),
    );
    let is_normalized = saved.is_some_and(|values| {
        values.len() == settings.len()
            && settings
                .keys()
                .all(|key| values.get(key).and_then(Value::as_str).is_some())
    });
    if !is_normalized {
        save_settings(app, settings.clone())?;
    }
    Ok(settings)
}

pub fn save_settings(app: &AppHandle, settings: BTreeMap<String, String>) -> Result<(), String> {
    let mut normalized: BTreeMap<String, String> = DEFAULT_SETTINGS
        .iter()
        .map(|(key, default)| {
            (
                (*key).to_string(),
                settings
                    .get(*key)
                    .cloned()
                    .unwrap_or_else(|| (*default).to_string()),
            )
        })
        .collect();
    let workdays = settings
        .get("daily_review_workdays")
        .map(String::as_str)
        .unwrap_or("0");
    if workdays != "0" && workdays != "1" {
        return Err("每日盘点工作日设置无效。".into());
    }
    normalized.insert("daily_review_workdays".into(), workdays.to_string());

    let local_shortcuts = [
        "shortcut_add",
        "shortcut_history",
        "shortcut_refresh",
        "shortcut_detail",
        "shortcut_edit",
        "shortcut_complete",
        "shortcut_cancel",
    ];
    let mut seen_shortcuts = HashSet::new();
    for key in local_shortcuts {
        let shortcut = normalized.get(key).expect("default shortcut exists");
        if shortcut.trim().is_empty() || shortcut.eq_ignore_ascii_case("None") {
            return Err("Toto 窗口快捷键不能为空。".into());
        }
        let canonical = shortcut
            .chars()
            .filter(|character| !character.is_whitespace())
            .collect::<String>()
            .to_lowercase();
        if !seen_shortcuts.insert(canonical) {
            return Err("Toto 窗口快捷键不能重复。".into());
        }
    }

    let minutes = normalized["default_remind_minutes"]
        .parse::<i64>()
        .ok()
        .filter(|minutes| *minutes >= 0)
        .ok_or_else(|| "默认提醒提前分钟数必须为非负整数。".to_string())?;
    normalized.insert("default_remind_minutes".into(), minutes.to_string());
    for key in ["daily_review_start_time", "daily_review_end_time"] {
        let time = normalized.get(key).expect("default time setting exists");
        if !time.is_empty() && chrono::NaiveTime::parse_from_str(time, "%H:%M").is_err() {
            return Err(format!("{key} 必须使用 HH:mm 格式。"));
        }
    }

    let mut config = read_config(app)?;
    if !config.is_object() {
        config = json!({});
    }
    let root = config
        .as_object_mut()
        .ok_or_else(|| "Toto 配置格式无效。".to_string())?;
    let values = root
        .entry("settings")
        .or_insert_with(|| Value::Object(Map::new()));
    *values = serde_json::to_value(normalized).map_err(|error| error.to_string())?;
    write_json_atomic(&tool_path(app, "config.json")?, &config)
}

pub fn save_default_remind_minutes(app: &AppHandle, minutes: i64) -> Result<(), String> {
    let mut config = read_config(app)?;
    set_default_remind_minutes(&mut config, minutes)?;
    write_json_atomic(&tool_path(app, "config.json")?, &config)
}

fn set_default_remind_minutes(config: &mut Value, minutes: i64) -> Result<(), String> {
    if minutes < 0 {
        return Err("默认提醒提前分钟数必须为非负整数。".into());
    }
    if !config.is_object() {
        *config = json!({});
    }
    let root = config
        .as_object_mut()
        .ok_or_else(|| "Toto 配置格式无效。".to_string())?;
    let settings = root
        .entry("settings")
        .or_insert_with(|| Value::Object(Map::new()));
    if !settings.is_object() {
        *settings = Value::Object(Map::new());
    }
    settings
        .as_object_mut()
        .ok_or_else(|| "Toto 设置格式无效。".to_string())?
        .insert(
            "default_remind_minutes".into(),
            Value::String(minutes.to_string()),
        );
    Ok(())
}

pub fn mark_due_reminders(app: &AppHandle) -> Result<Vec<TotoItem>, String> {
    let now = Local::now().naive_local();
    let now_text = now.format("%Y-%m-%d %H:%M:%S").to_string();
    let mut items = load_items(app)?;
    let due = mark_due_items(&mut items, &now_text);
    if !due.is_empty() {
        save_items_at(&tool_path(app, ITEMS_FILE)?, &items)?;
    }
    Ok(due)
}

pub fn check_daily_reviews(app: &AppHandle) -> Result<Vec<TotoDailyReview>, String> {
    let settings = load_settings(app)?;
    let now = Local::now();
    let workdays_only = settings
        .get("daily_review_workdays")
        .is_some_and(|value| value == "1");
    if should_skip_daily_review(workdays_only, now.weekday()) {
        return Ok(Vec::new());
    }
    let date = now.format("%Y-%m-%d").to_string();
    let now_time = now.format("%H:%M").to_string();
    let config_path = tool_path(app, "config.json")?;
    let mut config = read_config(app)?;
    let root = config
        .as_object_mut()
        .ok_or_else(|| "Toto 配置格式无效。".to_string())?;
    let scheduled = root
        .entry("scheduledPopups")
        .or_insert_with(|| Value::Object(Map::new()));
    if !scheduled.is_object() {
        *scheduled = Value::Object(Map::new());
    }
    let date_status = scheduled
        .as_object_mut()
        .and_then(|dates| {
            dates
                .entry(date.clone())
                .or_insert_with(|| Value::Object(Map::new()))
                .as_object_mut()
        })
        .ok_or_else(|| "Toto 盘点状态格式无效。".to_string())?;

    let mut kinds = Vec::new();
    for (setting_key, kind) in [
        ("daily_review_start_time", "work_start"),
        ("daily_review_end_time", "work_end"),
    ] {
        let scheduled_time = settings.get(setting_key).map(String::as_str).unwrap_or("");
        if !scheduled_time.is_empty()
            && now_time.as_str() >= scheduled_time
            && !date_status.contains_key(kind)
        {
            date_status.insert(
                kind.into(),
                Value::String(now.format("%Y-%m-%d %H:%M:%S").to_string()),
            );
            kinds.push(kind.to_string());
        }
    }
    if kinds.is_empty() {
        return Ok(Vec::new());
    }
    write_json_atomic(&config_path, &config)?;

    let active_items = load_items(app)?;
    let finished_items = load_history(app)?
        .into_iter()
        .filter(|item| item.ended_at.starts_with(&date) && item.end_status == "已完成")
        .collect::<Vec<_>>();
    Ok(kinds
        .into_iter()
        .map(|kind| TotoDailyReview {
            kind,
            date: date.clone(),
            active_items: active_items.clone(),
            finished_items: finished_items.clone(),
        })
        .collect())
}

fn should_skip_daily_review(workdays_only: bool, weekday: Weekday) -> bool {
    workdays_only && matches!(weekday, Weekday::Sat | Weekday::Sun)
}

fn mark_due_items(items: &mut [TotoItem], now: &str) -> Vec<TotoItem> {
    let mut due = Vec::new();
    for item in items {
        if item.reminder_status == "未提醒"
            && item
                .reminder_at
                .as_deref()
                .is_some_and(|reminder_at| reminder_at <= now)
        {
            item.reminder_status = "已提醒".into();
            item.reminded_at = Some(now.to_string());
            due.push(item.clone());
        }
    }
    due
}

pub fn add_item(
    app: &AppHandle,
    content: String,
    planned_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let reminder_minutes = load_default_remind_minutes(app)?;
    add_item_with_reminder(app, content, planned_at, notes, reminder_minutes)
}

pub fn add_quick_item(app: &AppHandle, input: &str) -> Result<TotoItem, String> {
    let parsed = parse_quick_text(
        input,
        Local::now().naive_local(),
        load_default_remind_minutes(app)?,
    )?;
    let mut item = add_item_with_reminder(
        app,
        parsed.content,
        parsed.planned_at,
        String::new(),
        parsed.reminder_lead_minutes,
    )?;
    if item.planned_at.is_none() {
        item.reminder_status = "无提醒".into();
    }
    Ok(item)
}

pub fn add_quick_item_command(app: &AppHandle, input: &str) -> Result<TotoItem, String> {
    add_quick_item(app, input)
}

pub fn preview_quick_item(app: &AppHandle, input: &str) -> Result<TotoQuickPreview, String> {
    let parsed = parse_quick_text(
        input,
        Local::now().naive_local(),
        load_default_remind_minutes(app)?,
    )?;
    let reminder_at = parsed
        .planned_at
        .as_deref()
        .map(|planned| shift_time(planned, -parsed.reminder_lead_minutes))
        .transpose()?;
    Ok(TotoQuickPreview {
        content: parsed.content,
        planned_at: parsed.planned_at,
        reminder_at,
    })
}

fn add_item_with_reminder(
    app: &AppHandle,
    content: String,
    planned_at: Option<String>,
    notes: String,
    reminder_lead_minutes: i64,
) -> Result<TotoItem, String> {
    let content = validate_content(content)?;
    let planned_at = normalize_planned_time(planned_at)?;
    let mut items = load_items(app)?;
    let created_order = items
        .iter()
        .map(|item| item.created_order)
        .chain(load_history(app)?.iter().map(|item| item.created_order))
        .max()
        .unwrap_or(0)
        + 1;
    let item = TotoItem {
        id: Uuid::new_v4().to_string(),
        content,
        reminder_at: planned_at
            .as_deref()
            .map(|value| shift_time(value, -reminder_lead_minutes))
            .transpose()?,
        reminder_status: if planned_at.is_some() {
            "未提醒".into()
        } else {
            "无提醒".into()
        },
        planned_at,
        created_at: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
        created_order,
        reminded_at: None,
        notes,
    };
    items.push(item.clone());
    save_items_at(&tool_path(app, ITEMS_FILE)?, &items)?;
    Ok(item)
}

struct ParsedQuickText {
    content: String,
    planned_at: Option<String>,
    reminder_lead_minutes: i64,
}

fn parse_quick_text(
    input: &str,
    now: NaiveDateTime,
    default_remind_minutes: i64,
) -> Result<ParsedQuickText, String> {
    let parts: Vec<&str> = input.split('@').collect();
    if parts.len() > 3 {
        return Err("快速新增最多只能包含三个 @ 分段。".into());
    }
    let content = validate_content(parts[0].to_string())?;
    let planned_at = if parts.len() >= 2 {
        let value = parts[1].trim();
        if value.is_empty() {
            return Err("计划时间不能为空。".into());
        }
        Some(
            parse_planned_time(value, now)?
                .format("%Y-%m-%d %H:%M:%S")
                .to_string(),
        )
    } else {
        None
    };
    let reminder_lead_minutes = if parts.len() == 3 {
        let minutes = parts[2]
            .trim()
            .parse::<i64>()
            .map_err(|_| "提醒提前分钟数必须是非负整数。".to_string())?;
        if minutes < 0 {
            return Err("提醒提前分钟数必须是非负整数。".into());
        }
        minutes
    } else {
        default_remind_minutes
    };

    Ok(ParsedQuickText {
        content,
        planned_at,
        reminder_lead_minutes,
    })
}

fn parse_planned_time(value: &str, now: NaiveDateTime) -> Result<NaiveDateTime, String> {
    let value = value.to_ascii_uppercase();
    if value.starts_with('+') {
        let days = value
            .chars()
            .take_while(|character| *character == '+')
            .count() as i64;
        let clock = parse_clock(&value[days as usize..])?;
        let date = now
            .date()
            .checked_add_signed(Duration::days(days))
            .ok_or_else(|| "计划时间超出有效范围。".to_string())?;
        return Ok(date.and_time(clock));
    }

    if value.starts_with('W') {
        let weeks = value
            .chars()
            .take_while(|character| *character == 'W')
            .count() as i64;
        let suffix = &value[weeks as usize..];
        let (weekday, clock_text) = if suffix.len() == 5 {
            let day = suffix[..1]
                .parse::<u32>()
                .map_err(|_| "周计划时间格式无效。".to_string())?;
            if !(1..=7).contains(&day) {
                return Err("星期必须为 1 到 7。".into());
            }
            (weekday_from_monday(day), &suffix[1..])
        } else if suffix.len() == 4 {
            (now.weekday(), suffix)
        } else {
            return Err("周计划时间格式无效。".into());
        };
        let current_monday =
            now.date() - Duration::days(i64::from(now.weekday().number_from_monday() - 1));
        let target_date = current_monday
            .checked_add_signed(Duration::days(
                weeks * 7 + i64::from(weekday.number_from_monday() - 1),
            ))
            .ok_or_else(|| "计划时间超出有效范围。".to_string())?;
        return Ok(target_date.and_time(parse_clock(clock_text)?));
    }

    if value.starts_with('H') || value.starts_with('M') {
        return parse_relative_time(&value, now);
    }

    let clock = match value.len() {
        4 => parse_clock(&value)?,
        6 => {
            let day = value[..2]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            date_with_time(now.year(), now.month(), day, &value[2..])?
        }
        8 => {
            let month = value[..2]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            let day = value[2..4]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            date_with_time(now.year(), month, day, &value[4..])?
        }
        12 => {
            let year = value[..4]
                .parse::<i32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            let month = value[4..6]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            let day = value[6..8]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?;
            date_with_time(year, month, day, &value[8..])?
        }
        _ => return Err("计划时间格式无效。".into()),
    };

    let date = match value.len() {
        4 => Some(now.date()),
        6 => NaiveDate::from_ymd_opt(
            now.year(),
            now.month(),
            value[..2]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?,
        ),
        8 => NaiveDate::from_ymd_opt(
            now.year(),
            value[..2]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?,
            value[2..4]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?,
        ),
        12 => NaiveDate::from_ymd_opt(
            value[..4]
                .parse::<i32>()
                .map_err(|_| "日期格式无效。".to_string())?,
            value[4..6]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?,
            value[6..8]
                .parse::<u32>()
                .map_err(|_| "日期格式无效。".to_string())?,
        ),
        _ => unreachable!(),
    }
    .ok_or_else(|| "计划日期无效。".to_string())?;
    Ok(date.and_time(clock))
}

fn date_with_time(year: i32, month: u32, day: u32, clock: &str) -> Result<NaiveTime, String> {
    NaiveDate::from_ymd_opt(year, month, day).ok_or_else(|| "计划日期无效。".to_string())?;
    parse_clock(clock)
}

fn parse_clock(value: &str) -> Result<NaiveTime, String> {
    if value.len() != 4 || !value.bytes().all(|byte| byte.is_ascii_digit()) {
        return Err("时间格式应为 HHmm。".into());
    }
    let hour = value[..2]
        .parse::<u32>()
        .map_err(|_| "时间格式无效。".to_string())?;
    let minute = value[2..]
        .parse::<u32>()
        .map_err(|_| "时间格式无效。".to_string())?;
    NaiveTime::from_hms_opt(hour, minute, 0).ok_or_else(|| "计划时间无效。".into())
}

fn weekday_from_monday(day: u32) -> Weekday {
    match day {
        1 => Weekday::Mon,
        2 => Weekday::Tue,
        3 => Weekday::Wed,
        4 => Weekday::Thu,
        5 => Weekday::Fri,
        6 => Weekday::Sat,
        _ => Weekday::Sun,
    }
}

fn parse_relative_time(value: &str, now: NaiveDateTime) -> Result<NaiveDateTime, String> {
    let (hours, minutes) = if let Some(minutes) = value.strip_prefix('M') {
        (
            0_i64,
            minutes
                .parse::<i64>()
                .map_err(|_| "相对时间格式无效。".to_string())?,
        )
    } else {
        let body = value
            .strip_prefix('H')
            .ok_or_else(|| "相对时间格式无效。".to_string())?;
        let mut parts = body.split('M');
        let hours = parts
            .next()
            .unwrap_or_default()
            .parse::<i64>()
            .map_err(|_| "相对时间格式无效。".to_string())?;
        let minutes = parts
            .next()
            .unwrap_or("0")
            .parse::<i64>()
            .map_err(|_| "相对时间格式无效。".to_string())?;
        if parts.next().is_some() {
            return Err("相对时间格式无效。".into());
        }
        (hours, minutes)
    };
    if hours < 0 || minutes < 0 || (hours == 0 && minutes == 0) {
        return Err("相对时间必须大于零。".into());
    }
    let total_minutes = hours
        .checked_mul(60)
        .and_then(|hours| hours.checked_add(minutes))
        .ok_or_else(|| "计划时间超出有效范围。".to_string())?;
    let duration =
        Duration::try_minutes(total_minutes).ok_or_else(|| "计划时间超出有效范围。".to_string())?;
    now.checked_add_signed(duration)
        .ok_or_else(|| "计划时间超出有效范围。".into())
}

pub fn update_item(
    app: &AppHandle,
    id: &str,
    content: String,
    planned_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let planned = normalize_planned_time(planned_at)?;
    let reminder_minutes = load_default_remind_minutes(app)?;
    let reminder = planned
        .as_deref()
        .map(|value| shift_time(value, -reminder_minutes))
        .transpose()?;
    update_item_full(app, id, content, planned, reminder, notes)
}

pub fn update_item_full(
    app: &AppHandle,
    id: &str,
    content: String,
    planned_at: Option<String>,
    reminder_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let content = validate_content(content)?;
    let planned_at = normalize_planned_time(planned_at)?;
    let reminder_at = normalize_planned_time(reminder_at)?;
    let mut items = load_items(app)?;
    let item = items
        .iter_mut()
        .find(|item| item.id == id)
        .ok_or_else(|| "事项不存在或已被外部修改。".to_string())?;

    if item.reminder_at != reminder_at {
        item.reminder_at = reminder_at;
        item.reminder_status = if item.reminder_at.is_some() {
            "未提醒".into()
        } else {
            "无提醒".into()
        };
        item.reminded_at = None;
    }
    item.content = content;
    item.planned_at = planned_at;
    item.notes = notes;
    let updated = item.clone();
    save_items_at(&tool_path(app, ITEMS_FILE)?, &items)?;
    Ok(updated)
}

pub fn finish_item(app: &AppHandle, id: &str, completed: bool) -> Result<(), String> {
    finish_item_with_note(app, id, completed, None)
}

pub fn finish_item_with_note(
    app: &AppHandle,
    id: &str,
    completed: bool,
    note: Option<String>,
) -> Result<(), String> {
    let mut items = load_items(app)?;
    let index = items
        .iter()
        .position(|item| item.id == id)
        .ok_or_else(|| "事项不存在或已被外部修改。".to_string())?;
    let item = items.remove(index);
    let mut history = load_history_at(&tool_path(app, HISTORY_FILE)?)?;
    history.push(TotoHistoryItem {
        id: item.id,
        content: item.content,
        planned_at: item.planned_at,
        reminder_at: item.reminder_at,
        created_at: item.created_at,
        created_order: item.created_order,
        end_status: if completed {
            "已完成".into()
        } else {
            "已取消".into()
        },
        ended_at: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
        notes: note.unwrap_or(item.notes),
    });

    save_history_at(&tool_path(app, HISTORY_FILE)?, &history)?;
    save_items_at(&tool_path(app, ITEMS_FILE)?, &items)
}

fn validate_content(content: String) -> Result<String, String> {
    let content = content.trim().to_string();
    if content.is_empty() {
        return Err("事项内容不能为空。".into());
    }
    if content.contains(['\r', '\n']) {
        return Err("事项内容不能包含换行。".into());
    }
    Ok(content)
}

fn normalize_planned_time(value: Option<String>) -> Result<Option<String>, String> {
    value
        .filter(|value| !value.trim().is_empty())
        .map(|value| {
            let parsed = NaiveDateTime::parse_from_str(&value, "%Y-%m-%d %H:%M")
                .or_else(|_| NaiveDateTime::parse_from_str(&value, "%Y-%m-%d %H:%M:%S"))
                .map_err(|_| "计划时间格式无效。".to_string())?;
            Ok(parsed.format("%Y-%m-%d %H:%M:%S").to_string())
        })
        .transpose()
}

fn shift_time(value: &str, minutes: i64) -> Result<String, String> {
    let parsed = NaiveDateTime::parse_from_str(value, "%Y-%m-%d %H:%M:%S")
        .map_err(|_| "事项中的时间格式无效。".to_string())?;
    let duration =
        Duration::try_minutes(minutes).ok_or_else(|| "提醒时间超出有效范围。".to_string())?;
    let shifted = parsed
        .checked_add_signed(duration)
        .ok_or_else(|| "提醒时间超出有效范围。".to_string())?;
    Ok(shifted.format("%Y-%m-%d %H:%M:%S").to_string())
}

fn tool_path(app: &AppHandle, file_name: &str) -> Result<PathBuf, String> {
    let settings = AppSettings::load(app)?;
    let directory = PathBuf::from(settings.data_root).join("tools").join("toto");
    fs::create_dir_all(&directory).map_err(|error| error.to_string())?;
    Ok(directory.join(file_name))
}

fn read_config(app: &AppHandle) -> Result<Value, String> {
    let path = tool_path(app, "config.json")?;
    if !path.exists() {
        write_json_atomic(
            &path,
            &json!({ "settings": { "default_remind_minutes": "5" } }),
        )?;
    }
    let contents = fs::read_to_string(path).map_err(|error| error.to_string())?;
    serde_json::from_str(&contents).map_err(|error| error.to_string())
}

fn write_json_atomic(path: &Path, value: &Value) -> Result<(), String> {
    let temporary = path.with_extension("json.tmp");
    let contents = serde_json::to_vec_pretty(value).map_err(|error| error.to_string())?;
    fs::write(&temporary, contents).map_err(|error| error.to_string())?;
    fs::rename(temporary, path).map_err(|error| error.to_string())
}

fn load_history_at(path: &Path) -> Result<Vec<TotoHistoryItem>, String> {
    if !path.exists() {
        save_history_at(path, &[])?;
    }
    read_csv(path)
}

fn read_csv<T: for<'de> Deserialize<'de>>(path: &Path) -> Result<Vec<T>, String> {
    let mut bytes = fs::read(path).map_err(|error| error.to_string())?;
    if bytes.starts_with(CSV_BOM) {
        bytes.drain(..CSV_BOM.len());
    }
    csv::ReaderBuilder::new()
        .has_headers(true)
        .from_reader(bytes.as_slice())
        .deserialize()
        .collect::<Result<Vec<T>, csv::Error>>()
        .map_err(|error| error.to_string())
}

fn save_items_at(path: &Path, items: &[TotoItem]) -> Result<(), String> {
    write_csv(
        path,
        items,
        &[
            "事项ID",
            "事项内容",
            "计划时间",
            "提醒时间",
            "创建时间",
            "创建序号",
            "提醒状态",
            "响铃时间",
            "备注",
        ],
    )
}

fn save_history_at(path: &Path, items: &[TotoHistoryItem]) -> Result<(), String> {
    write_csv(
        path,
        items,
        &[
            "事项ID",
            "事项内容",
            "计划时间",
            "提醒时间",
            "创建时间",
            "创建序号",
            "结束状态",
            "结束时间",
            "备注",
        ],
    )
}

fn write_csv<T: Serialize>(path: &Path, rows: &[T], headers: &[&str]) -> Result<(), String> {
    let mut writer = csv::WriterBuilder::new()
        .has_headers(false)
        .from_writer(Vec::new());
    writer
        .write_record(headers)
        .map_err(|error| error.to_string())?;
    for row in rows {
        writer.serialize(row).map_err(|error| error.to_string())?;
    }
    let body = writer.into_inner().map_err(|error| error.to_string())?;
    let mut contents = Vec::with_capacity(CSV_BOM.len() + body.len());
    contents.extend_from_slice(CSV_BOM);
    contents.extend_from_slice(&body);

    let temporary = path.with_extension("csv.tmp");
    fs::write(&temporary, contents).map_err(|error| error.to_string())?;
    fs::rename(temporary, path).map_err(|error| error.to_string())
}

#[cfg(test)]
mod tests {
    use super::{
        TotoItem, mark_due_items, normalize_planned_time, parse_quick_text, read_csv,
        save_items_at, set_default_remind_minutes, shift_time, should_skip_daily_review,
    };
    use chrono::{NaiveDate, Weekday};
    use serde_json::json;

    #[test]
    fn csv_round_trip_keeps_bom_commas_and_nine_columns() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let path = temporary.path().join("toto_ing.csv");
        let item = TotoItem {
            id: "test-id".into(),
            content: "事项,带逗号".into(),
            planned_at: None,
            reminder_at: None,
            created_at: "2026-11-21 12:00:00".into(),
            created_order: 1,
            reminder_status: "无提醒".into(),
            reminded_at: None,
            notes: "备注".into(),
        };

        save_items_at(&path, std::slice::from_ref(&item)).expect("save CSV");
        let bytes = std::fs::read(&path).expect("read CSV");
        assert!(bytes.starts_with(b"\xef\xbb\xbf"));
        assert_eq!(
            read_csv::<TotoItem>(&path).expect("read records"),
            vec![item]
        );
    }

    #[test]
    fn quick_text_parses_absolute_and_relative_plan_times() {
        let now = NaiveDate::from_ymd_opt(2026, 11, 21)
            .unwrap()
            .and_hms_opt(9, 0, 0)
            .unwrap();
        let absolute = parse_quick_text("发布@202612011600@10", now, 5).unwrap();
        assert_eq!(absolute.content, "发布");
        assert_eq!(absolute.planned_at.as_deref(), Some("2026-12-01 16:00:00"));
        assert_eq!(absolute.reminder_lead_minutes, 10);
        assert_eq!(
            normalize_planned_time(absolute.planned_at.clone()).unwrap(),
            absolute.planned_at
        );

        let relative = parse_quick_text("稍后@H2M30", now, 5).unwrap();
        assert_eq!(relative.planned_at.as_deref(), Some("2026-11-21 11:30:00"));

        let next_day = parse_quick_text("明天下午@+1600", now, 5).unwrap();
        assert_eq!(next_day.planned_at.as_deref(), Some("2026-11-22 16:00:00"));

        let next_monday = parse_quick_text("周会@W11600", now, 5).unwrap();
        assert_eq!(
            next_monday.planned_at.as_deref(),
            Some("2026-11-23 16:00:00")
        );
    }

    #[test]
    fn quick_text_rejects_invalid_dates_and_negative_reminder_leads() {
        let now = NaiveDate::from_ymd_opt(2026, 11, 21)
            .unwrap()
            .and_hms_opt(9, 0, 0)
            .unwrap();
        assert!(parse_quick_text("无效日期@202602301200", now, 5).is_err());
        assert!(parse_quick_text("事项@1600@-1", now, 5).is_err());
    }

    #[test]
    fn quick_text_rejects_extreme_time_offsets_without_overflowing() {
        let now = NaiveDate::from_ymd_opt(2026, 11, 21)
            .unwrap()
            .and_hms_opt(9, 0, 0)
            .unwrap();
        assert!(parse_quick_text("事项@H9223372036854775807", now, 5).is_err());
        assert!(shift_time("2026-11-21 09:00:00", i64::MIN).is_err());
    }

    #[test]
    fn due_reminder_is_marked_before_it_is_returned_for_notification() {
        let mut items = vec![TotoItem {
            id: "due".into(),
            content: "到期事项".into(),
            planned_at: Some("2026-11-21 09:00:00".into()),
            reminder_at: Some("2026-11-21 08:55:00".into()),
            created_at: "2026-11-20 12:00:00".into(),
            created_order: 1,
            reminder_status: "未提醒".into(),
            reminded_at: None,
            notes: String::new(),
        }];

        let due = mark_due_items(&mut items, "2026-11-21 09:00:00");

        assert_eq!(due.len(), 1);
        assert_eq!(items[0].reminder_status, "已提醒");
        assert_eq!(items[0].reminded_at.as_deref(), Some("2026-11-21 09:00:00"));
        assert!(mark_due_items(&mut items, "2026-11-21 09:01:00").is_empty());
    }

    #[test]
    fn changing_default_reminder_preserves_other_config_values() {
        let mut config = json!({
            "settings": {
                "default_remind_minutes": "5",
                "daily_review_start_time": "09:00"
            },
            "scheduledPopups": { "2026-11-21": true }
        });

        set_default_remind_minutes(&mut config, 12).expect("valid reminder interval");

        assert_eq!(config["settings"]["default_remind_minutes"], "12");
        assert_eq!(config["settings"]["daily_review_start_time"], "09:00");
        assert_eq!(config["scheduledPopups"]["2026-11-21"], true);
        assert!(set_default_remind_minutes(&mut config, -1).is_err());
    }

    #[test]
    fn workday_only_daily_reviews_skip_saturday_and_sunday() {
        assert!(should_skip_daily_review(true, Weekday::Sat));
        assert!(should_skip_daily_review(true, Weekday::Sun));
        assert!(!should_skip_daily_review(true, Weekday::Mon));
        assert!(!should_skip_daily_review(true, Weekday::Fri));
        assert!(!should_skip_daily_review(false, Weekday::Sun));
    }
}
