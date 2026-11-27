mod deadline_scheduler;
mod holidays;
mod json_documents;
#[allow(dead_code)]
mod reminder_queue;
mod scheduler;
mod settings;
mod toto;

use chrono::{Local, NaiveDateTime, NaiveTime, SecondsFormat, TimeZone, Utc};
use deadline_scheduler::DeadlineScheduler;
use scheduler::TaskScheduler;
use serde::{Deserialize, Serialize};
use settings::AppSettings;
use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime};
use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Emitter, LogicalPosition, Manager, Position, State, WindowEvent};
use tauri_plugin_autostart::ManagerExt as AutostartManagerExt;
use tauri_plugin_global_shortcut::{GlobalShortcutExt, ShortcutState};
use toto::{TotoHistoryItem, TotoHoliday, TotoItem};

struct LauncherShortcutStatus {
    launcher: Mutex<Option<String>>,
    tools: Mutex<BTreeMap<String, String>>,
}
#[derive(Clone, Copy, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct WindowPosition {
    x: f64,
    y: f64,
    width: Option<f64>,
    height: Option<f64>,
    maximized: bool,
}

struct WindowPositionStore(Arc<Mutex<HashMap<String, WindowPosition>>>);
static TOTO_SYNC_LOCK: Mutex<()> = Mutex::new(());
static DAILY_REVIEW_LOCK: Mutex<()> = Mutex::new(());
#[derive(Default)]
struct ReminderWindows(Mutex<HashMap<String, usize>>);

fn next_reminder_slot(slots: &HashMap<String, usize>) -> usize {
    (0..4)
        .find(|index| !slots.values().any(|value| value == index))
        .unwrap_or(3)
}

fn show_toto_reminder(app: &AppHandle, id: &str) -> Result<(), String> {
    let id = uuid::Uuid::parse_str(id)
        .map_err(|error| error.to_string())?
        .to_string();
    let label = format!("toto-reminder-{id}");
    let slots = app.state::<ReminderWindows>();
    let mut slots = slots.0.lock().map_err(|_| "提醒窗口管理器不可用。")?;
    if app.get_webview_window(&label).is_some() {
        return Ok(());
    }
    let slot = next_reminder_slot(&slots);
    let main = app.get_webview_window("main");
    let width: f64 = 600.0;
    let height = (width / 1.618_033_988_75).round();
    let mut builder = tauri::WebviewWindowBuilder::new(
        app,
        &label,
        tauri::WebviewUrl::App(format!("/?window=toto-reminder&id={id}").into()),
    )
    .title("提醒")
    .inner_size(width, height)
    .resizable(false)
    .decorations(false);
    if let Some(monitor) = main.and_then(|window| window.current_monitor().ok().flatten()) {
        let scale = monitor.scale_factor();
        let area = monitor.work_area();
        let x = area.position.x as f64 / scale
            + (area.size.width as f64 / scale - width) / 2.0
            + slot as f64 * 24.0;
        let y = area.position.y as f64 / scale
            + (area.size.height as f64 / scale - height) / 2.0
            + slot as f64 * 24.0;
        builder = builder.position(x, y);
    } else {
        builder = builder.center();
    }
    let window = builder.build().map_err(|error| error.to_string())?;
    slots.insert(label.clone(), slot);
    drop(slots);
    let app = app.clone();
    window.on_window_event(move |event| {
        if matches!(event, WindowEvent::Destroyed) {
            if let Ok(mut slots) = app.state::<ReminderWindows>().0.lock() {
                slots.remove(&label);
            }
        }
    });
    Ok(())
}

fn queue_reminder_delivery(app: &AppHandle, id: &str, delay_seconds: i64) -> Result<(), String> {
    let scheduler = app.state::<DeadlineScheduler>();
    let app = app.clone();
    scheduler.schedule(
        Utc::now() + chrono::Duration::seconds(delay_seconds),
        "toto.delivery",
        id,
        move |id| {
            let result = toto::load_items(&app).and_then(|items| {
                if items
                    .iter()
                    .any(|item| item.id == id && item.reminder_status == "待确认")
                {
                    show_toto_reminder(&app, &id)?;
                    let latest = toto::load_items(&app)?;
                    sync_reminder_window(
                        &app,
                        &id,
                        latest
                            .iter()
                            .any(|item| item.id == id && item.reminder_status == "待确认"),
                    );
                    Ok(())
                } else {
                    Ok(())
                }
            });
            if let Err(error) = result {
                log::warn!("Could not show Toto reminder {id}: {error}");
                if let Err(error) = queue_reminder_delivery(&app, &id, 30) {
                    log::warn!("Could not retry Toto reminder {id}: {error}");
                }
            }
        },
    )
}

fn retry_toto_item(app: &AppHandle, id: &str) {
    let scheduler = app.state::<DeadlineScheduler>();
    let app = app.clone();
    if let Err(error) = scheduler.schedule(
        Utc::now() + chrono::Duration::seconds(30),
        "toto.retry",
        id,
        move |id| {
            let Ok(_guard) = TOTO_SYNC_LOCK.lock() else {
                return;
            };
            match toto::load_items(&app) {
                Ok(items) => {
                    if let Some(item) = items.iter().find(|item| item.id == id) {
                        if let Err(error) = schedule_toto_item(&app, item) {
                            log::warn!("Could not retry Toto item {id}: {error}");
                            retry_toto_item(&app, &id);
                        }
                    }
                }
                Err(error) => {
                    log::warn!("Could not reload Toto item {id}: {error}");
                    retry_toto_item(&app, &id);
                }
            }
        },
    ) {
        log::warn!("Could not queue retry for Toto item {id}: {error}");
    }
}

fn schedule_toto_item(app: &AppHandle, item: &TotoItem) -> Result<(), String> {
    let scheduler = app.state::<DeadlineScheduler>();
    scheduler.cancel("toto.item", &item.id)?;
    if item.reminder_status != "未提醒" {
        return Ok(());
    }
    let Some(value) = item.reminder_at.as_deref() else {
        return Ok(());
    };
    let local = NaiveDateTime::parse_from_str(value, "%Y-%m-%d %H:%M:%S")
        .map_err(|error| format!("提醒时间无效：{error}"))?;
    let at = Local
        .from_local_datetime(&local)
        .earliest()
        .ok_or_else(|| format!("提醒时间不在本地时区有效范围内：{value}"))?
        .with_timezone(&Utc);
    let app = app.clone();
    let expected = value.to_owned();
    scheduler.schedule(at, "toto.item", &item.id, move |id| {
        let Ok(_guard) = TOTO_SYNC_LOCK.lock() else {
            return;
        };
        match toto::mark_due_reminder(&app, &id, &expected) {
            Ok(Some(item)) => {
                if let Err(error) = queue_reminder_delivery(&app, &item.id, 0) {
                    log::warn!("Could not queue Toto reminder: {error}");
                }
            }
            Ok(None) => {
                // A clock change or concurrent edit may have invalidated the deadline.
                retry_toto_item(&app, &id);
            }
            Err(error) => {
                log::warn!("Could not process Toto reminder: {error}");
                retry_toto_item(&app, &id);
            }
        }
    })
}

#[tauri::command]
fn resolve_toto_reminder(
    app: AppHandle,
    id: String,
    expected: String,
    snooze: bool,
) -> Result<(), String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    if let Some(item) = toto::resolve_reminder(&app, &id, &expected, snooze)? {
        if let Err(error) = schedule_toto_item(&app, &item) {
            log::warn!("Could not schedule reminder after confirmation: {error}");
            retry_toto_item(&app, &id);
        }
    }
    Ok(())
}

fn sync_reminder_window(app: &AppHandle, id: &str, pending: bool) {
    let label = format!("toto-reminder-{id}");
    if let Some(window) = app.get_webview_window(&label) {
        if pending {
            let _ = window.emit("toto:reminder-changed", ());
        } else if let Err(error) = window.destroy() {
            log::warn!("Could not close obsolete reminder {id}: {error}");
        }
    }
}

fn watch_toto_items(app: &AppHandle) -> Result<(), String> {
    let path = toto::items_path(app)?;
    let metadata = std::fs::metadata(&path).map_err(|error| error.to_string())?;
    let initial = toto::load_items(app)?;
    let state = Arc::new(Mutex::new((
        (metadata.modified().ok(), metadata.len()),
        initial,
    )));
    app.state::<TaskScheduler>().schedule_periodic(
        "toto-file-watch",
        app.clone(),
        Duration::from_secs(3),
        move |app| {
            let Ok(_guard) = TOTO_SYNC_LOCK.lock() else {
                return;
            };
            let Ok(metadata) = std::fs::metadata(&path) else {
                return;
            };
            let stamp: (Option<SystemTime>, u64) = (metadata.modified().ok(), metadata.len());
            let Ok(mut previous) = state.lock() else {
                return;
            };
            if previous.0 == stamp {
                return;
            }
            let Ok(items) = toto::load_items(app) else {
                return;
            };
            // Do not accept a snapshot if a writer changed the file during the read.
            let Ok(after) = std::fs::metadata(&path) else {
                return;
            };
            if (after.modified().ok(), after.len()) != stamp {
                return;
            }
            for old in &previous.1 {
                if !items.iter().any(|item| item.id == old.id) {
                    let _ = app
                        .state::<DeadlineScheduler>()
                        .cancel("toto.item", &old.id);
                    sync_reminder_window(app, &old.id, false);
                }
            }
            for item in &items {
                if previous.1.iter().find(|old| old.id == item.id) != Some(item) {
                    if let Err(error) = schedule_toto_item(app, item) {
                        log::warn!("Could not synchronize Toto item {}: {error}", item.id);
                        retry_toto_item(app, &item.id);
                    }
                    sync_reminder_window(app, &item.id, item.reminder_status == "待确认");
                    if item.reminder_status == "待确认" {
                        let _ = queue_reminder_delivery(app, &item.id, 0);
                    }
                }
            }
            *previous = (stamp, items);
            let _ = app.emit("toto:items-changed", ());
        },
    )
}

fn schedule_daily_reviews(app: &AppHandle) -> Result<(), String> {
    let settings = toto::load_settings(app)?;
    let now = Local::now();
    let scheduler = app.state::<DeadlineScheduler>();
    for (key, id) in [
        ("daily_review_start_time", "start"),
        ("daily_review_end_time", "end"),
    ] {
        scheduler.cancel("toto.daily", id)?;
        let Some(time) = settings.get(key).filter(|time| !time.is_empty()) else {
            continue;
        };
        let time = NaiveTime::parse_from_str(time, "%H:%M").map_err(|error| error.to_string())?;
        let today = now.date_naive().and_time(time);
        // At startup the existing daily deduplication check handles today's missed events.
        let next = if today <= now.naive_local() {
            now.date_naive()
                .succ_opt()
                .ok_or("日期超出范围。")?
                .and_time(time)
        } else {
            today
        };
        let at = Local
            .from_local_datetime(&next)
            .earliest()
            .ok_or_else(|| format!("盘点时间无效：{next}"))?
            .with_timezone(&Utc);
        let app = app.clone();
        scheduler.schedule(at, "toto.daily", id, move |_| {
            dispatch_daily_reviews(&app);
            if let Err(error) = schedule_daily_reviews(&app) {
                log::warn!("Could not schedule Toto daily reviews: {error}");
            }
        })?;
    }
    Ok(())
}

fn dispatch_daily_reviews(app: &AppHandle) {
    let Ok(_guard) = DAILY_REVIEW_LOCK.lock() else {
        return;
    };
    let result = toto::check_daily_reviews(app).and_then(|reviews| {
        for review in &reviews {
            let label = format!(
                "toto-daily-{}-{}",
                review.kind.replace('_', "-"),
                review.date
            );
            if app.get_webview_window(&label).is_none() {
                tauri::WebviewWindowBuilder::new(
                    app,
                    label,
                    tauri::WebviewUrl::App(
                        format!(
                            "/?window=toto-daily-review&kind={}&date={}",
                            review.kind, review.date
                        )
                        .into(),
                    ),
                )
                .title(if review.kind == "work_start" {
                    "开始盘点"
                } else {
                    "结束复盘"
                })
                .inner_size(600.0, 480.0)
                .center()
                .build()
                .map_err(|error| error.to_string())?;
            }
        }
        Ok(!reviews.is_empty())
    });
    if let Err(error) = &result {
        log::warn!("Could not deliver Toto daily review: {error}");
    }
    if !matches!(result, Ok(false)) {
        let handle = app.clone();
        if let Err(error) = app.state::<DeadlineScheduler>().schedule(
            Utc::now() + chrono::Duration::seconds(30),
            "toto.daily",
            "delivery-retry",
            move |_| dispatch_daily_reviews(&handle),
        ) {
            log::warn!("Could not retry Toto daily review: {error}");
        }
    }
}

fn queue_daily_review_check(app: &AppHandle) -> Result<(), String> {
    let handle = app.clone();
    app.state::<DeadlineScheduler>()
        .schedule(Utc::now(), "toto.daily", "check", move |_| {
            dispatch_daily_reviews(&handle);
        })
}

#[tauri::command]
fn load_toto_daily_review(
    app: AppHandle,
    date: String,
    kind: String,
) -> Result<toto::TotoDailyReview, String> {
    let _guard = DAILY_REVIEW_LOCK.lock().map_err(|_| "盘点状态不可用。")?;
    let review = toto::daily_review_snapshot(&app, &date, &kind)?;
    toto::acknowledge_daily_review(&app, &date, &kind)?;
    Ok(review)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TimeContextPayload {
    now: String,
    time_zone: String,
    locale: String,
}

#[tauri::command]
fn get_time_context() -> Result<TimeContextPayload, String> {
    let time_zone =
        iana_time_zone::get_timezone().map_err(|error| format!("无法读取系统时区：{error}"))?;
    Ok(TimeContextPayload {
        now: Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true),
        time_zone,
        locale: "zh-CN".into(),
    })
}

impl Default for WindowPositionStore {
    fn default() -> Self {
        Self(Arc::new(Mutex::new(HashMap::new())))
    }
}

#[tauri::command]
fn load_app_settings(app: AppHandle) -> Result<AppSettings, String> {
    let mut settings = AppSettings::load(&app)?;
    let json_settings = settings::load_json_tool_settings(&app)?;
    if let Some(global_shortcut) = json_settings
        .get("globalShortcut")
        .and_then(serde_json::Value::as_str)
    {
        settings
            .tool_global_shortcuts
            .insert("json".into(), global_shortcut.into());
    }
    settings.tool_settings.insert("json".into(), json_settings);
    if let Ok(toto_settings) = toto::load_settings(&app) {
        if let Some(hotkey) = toto_settings.get("hotkey") {
            settings
                .tool_global_shortcuts
                .insert("toto".into(), hotkey.clone());
        }
    }
    Ok(settings)
}

#[tauri::command]
fn save_app_settings(
    app: AppHandle,
    status: State<'_, LauncherShortcutStatus>,
    settings: AppSettings,
) -> Result<(), String> {
    let previous = load_app_settings(app.clone())?;
    let previous_registered = status
        .launcher
        .lock()
        .map_err(|_| "快捷键状态不可用。".to_string())?
        .clone();
    let previous_tools = status
        .tools
        .lock()
        .map_err(|_| "快捷键状态不可用。".to_string())?
        .clone();
    let next_tools = active_tool_shortcuts(&settings.tool_global_shortcuts);
    let next_launcher = normalize_shortcut(&settings.leader_shortcut);
    let shortcut_changed = normalize_shortcut(&previous.leader_shortcut) != next_launcher;
    let should_register = shortcut_changed;

    if should_register {
        if let Some(shortcut) = &previous_registered {
            let _ = app.global_shortcut().unregister(shortcut.as_str());
        }
        if let Err(error) = app.global_shortcut().register(next_launcher.as_str()) {
            if let Some(shortcut) = &previous_registered {
                let _ = app.global_shortcut().register(shortcut.as_str());
            }
            return Err(format!("快捷键无法注册：{error}"));
        }
        *status
            .launcher
            .lock()
            .map_err(|_| "快捷键状态不可用。".to_string())? = Some(next_launcher.clone());
    }

    if let Err(error) = sync_tool_shortcuts(&app, &status, next_tools) {
        if should_register {
            let _ = app.global_shortcut().unregister(next_launcher.as_str());
            if let Some(shortcut) = &previous_registered {
                let _ = app.global_shortcut().register(shortcut.as_str());
            }
            if let Ok(mut registered) = status.launcher.lock() {
                *registered = previous_registered;
            }
        }
        return Err(error);
    }

    let save_result = AppSettings::save(&app, &settings).and_then(|()| {
        settings
            .tool_settings
            .get("json")
            .map_or(Ok(()), |json_settings| {
                settings::save_json_tool_settings(&app, json_settings)
            })
    });
    if let Err(error) = save_result {
        let _ = AppSettings::save(&app, &previous);
        let _ = sync_tool_shortcuts(&app, &status, previous_tools);
        if should_register {
            let _ = app.global_shortcut().unregister(next_launcher.as_str());
            if let Some(shortcut) = &previous_registered {
                let _ = app.global_shortcut().register(shortcut.as_str());
            }
            *status
                .launcher
                .lock()
                .map_err(|_| "快捷键状态不可用。".to_string())? = previous_registered;
        }
        return Err(error);
    }
    let _ = app.emit("app:settings-changed", settings.clone());
    Ok(())
}

#[tauri::command]
fn schedule_demo_task(
    app: AppHandle,
    scheduler: State<'_, TaskScheduler>,
    delay_ms: u64,
) -> Result<String, String> {
    scheduler.schedule_demo(app, delay_ms)
}

#[tauri::command]
fn cancel_scheduled_task(
    scheduler: State<'_, TaskScheduler>,
    task_id: String,
) -> Result<bool, String> {
    scheduler.cancel(&task_id)
}

fn active_tool_shortcuts(shortcuts: &BTreeMap<String, String>) -> BTreeMap<String, String> {
    shortcuts
        .iter()
        .filter(|(_, shortcut)| {
            !shortcut.trim().is_empty() && !shortcut.eq_ignore_ascii_case("None")
        })
        .map(|(tool, shortcut)| (tool.clone(), normalize_shortcut(shortcut)))
        .collect()
}

fn normalize_shortcut(shortcut: &str) -> String {
    shortcut
        .chars()
        .filter(|character| !character.is_whitespace())
        .collect()
}

fn shortcuts_match(left: &str, right: &str) -> bool {
    normalize_shortcut(left).eq_ignore_ascii_case(&normalize_shortcut(right))
}

fn should_return_to_launcher_on_close(label: &str, preference: bool) -> bool {
    preference
        && !label.starts_with("toto-reminder-")
        && !matches!(label, "settings" | "toto-history" | "toto-reminder")
}

fn sync_tool_shortcuts(
    app: &AppHandle,
    status: &LauncherShortcutStatus,
    next: BTreeMap<String, String>,
) -> Result<(), String> {
    let previous = status
        .tools
        .lock()
        .map_err(|_| "快捷键状态不可用。".to_string())?
        .clone();
    let changed: Vec<_> = previous
        .iter()
        .filter(|(tool, shortcut)| next.get(*tool) != Some(*shortcut))
        .map(|(tool, shortcut)| (tool.clone(), shortcut.clone()))
        .collect();
    let mut changed_registered: Vec<String> = Vec::new();

    for (_, shortcut) in &changed {
        let _ = app.global_shortcut().unregister(shortcut.as_str());
    }
    for (tool, shortcut) in &next {
        if previous.get(tool) == Some(shortcut) {
            continue;
        }
        if let Err(error) = app.global_shortcut().register(shortcut.as_str()) {
            for registered in &changed_registered {
                let _ = app.global_shortcut().unregister(registered.as_str());
            }
            for (_, old_shortcut) in &changed {
                let _ = app.global_shortcut().register(old_shortcut.as_str());
            }
            return Err(format!("工具快捷键无法注册：{error}"));
        }
        changed_registered.push(shortcut.clone());
    }

    *status
        .tools
        .lock()
        .map_err(|_| "快捷键状态不可用。".to_string())? = next;
    Ok(())
}

#[tauri::command]
fn hide_main_window(app: AppHandle) -> Result<(), String> {
    app.get_webview_window("main")
        .ok_or_else(|| "主界面窗口不存在。".to_string())?
        .hide()
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn show_main_window(app: AppHandle) -> Result<(), String> {
    let window = app
        .get_webview_window("main")
        .ok_or_else(|| "主界面窗口不存在。".to_string())?;
    window.show().map_err(|error| error.to_string())?;
    window.unminimize().map_err(|error| error.to_string())?;
    window.set_focus().map_err(|error| error.to_string())
}

#[tauri::command]
fn get_window_position(
    state: State<'_, WindowPositionStore>,
    label: String,
) -> Result<Option<WindowPosition>, String> {
    state
        .0
        .lock()
        .map(|positions| positions.get(&label).copied())
        .map_err(|_| "窗口位置状态不可用。".into())
}

#[tauri::command]
fn remember_window_position(
    state: State<'_, WindowPositionStore>,
    label: String,
    x: f64,
    y: f64,
) -> Result<(), String> {
    let mut positions = state
        .0
        .lock()
        .map_err(|_| "窗口位置状态不可用。".to_string())?;
    let entry = positions.entry(label).or_insert(WindowPosition {
        x,
        y,
        width: None,
        height: None,
        maximized: false,
    });
    entry.x = x;
    entry.y = y;
    Ok(())
}

#[tauri::command]
fn open_settings_window_command(app: AppHandle) -> Result<(), String> {
    request_settings_window(&app)
}

#[tauri::command]
fn navigate_settings_window(app: AppHandle, section: String) -> Result<(), String> {
    app.emit_to("settings", "app:navigate-settings", section)
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn close_window_command(app: AppHandle, label: String) -> Result<(), String> {
    let window = app
        .get_webview_window(&label)
        .ok_or_else(|| format!("窗口不存在：{label}"))?;
    if let Some(store) = app.try_state::<WindowPositionStore>() {
        if let (Ok(position), Ok(size), Ok(scale), Ok(maximized), Ok(mut positions)) = (
            window.outer_position(),
            window.inner_size(),
            window.scale_factor(),
            window.is_maximized(),
            store.0.lock(),
        ) {
            let entry = positions.entry(label.clone()).or_insert(WindowPosition {
                x: position.x as f64 / scale,
                y: position.y as f64 / scale,
                width: None,
                height: None,
                maximized: false,
            });
            if !maximized {
                entry.x = position.x as f64 / scale;
                entry.y = position.y as f64 / scale;
                entry.width = Some(size.width as f64 / scale);
                entry.height = Some(size.height as f64 / scale);
            }
            entry.maximized = maximized;
        }
    }
    let return_to_launcher = if matches!(
        label.as_str(),
        "settings" | "toto-history" | "toto-reminder"
    ) {
        false
    } else {
        let preferred = AppSettings::load(&app)
            .map(|settings| settings.return_to_launcher_on_tool_close)
            .unwrap_or_else(|error| {
                log::warn!(
                    "Could not read the launcher return preference while closing {label}: {error}"
                );
                false
            });
        should_return_to_launcher_on_close(&label, preferred)
    };
    let result = if label == "settings" {
        let _ = app.emit_to("main", "app:settings-closed", ());
        window.destroy().map_err(|error| error.to_string())
    } else if label == "toto" {
        window.hide().map_err(|error| error.to_string())
    } else if label == "tool-json" {
        let settings = settings::load_json_tool_settings(&app).unwrap_or_else(|error| {
            log::warn!("Could not read JSON window settings while closing: {error}");
            settings::default_json_tool_settings()
        });
        if settings
            .get("keepWindowOnClose")
            .and_then(serde_json::Value::as_bool)
            .unwrap_or(true)
        {
            window.hide().map_err(|error| error.to_string())
        } else {
            window.destroy().map_err(|error| error.to_string())
        }
    } else {
        window.destroy().map_err(|error| error.to_string())
    };
    result?;
    if return_to_launcher {
        show_main(&app);
    }
    Ok(())
}

#[tauri::command]
fn load_toto_items(app: AppHandle) -> Result<Vec<TotoItem>, String> {
    toto::load_items(&app)
}

#[tauri::command]
fn load_toto_history(app: AppHandle) -> Result<Vec<TotoHistoryItem>, String> {
    toto::load_history(&app)
}

#[tauri::command]
async fn load_toto_calendar(app: AppHandle, year: i32) -> Result<Vec<TotoHoliday>, String> {
    toto::ensure_calendar(&app, year).await
}

#[tauri::command]
fn save_toto_calendar(app: AppHandle, year: i32, days: Vec<TotoHoliday>) -> Result<(), String> {
    toto::save_calendar(&app, year, days)
}

#[tauri::command]
async fn download_toto_calendar(app: AppHandle, year: i32) -> Result<Vec<TotoHoliday>, String> {
    toto::download_calendar(&app, year).await
}

#[tauri::command]
fn add_toto_item(
    app: AppHandle,
    content: String,
    planned_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    let item = toto::add_item(&app, content, planned_at, notes)?;
    schedule_toto_item(&app, &item)?;
    Ok(item)
}

#[tauri::command]
fn add_toto_quick_item(app: AppHandle, input: String) -> Result<TotoItem, String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    let item = toto::add_quick_item_command(&app, &input)?;
    schedule_toto_item(&app, &item)?;
    Ok(item)
}

#[tauri::command]
fn preview_toto_quick_item(
    app: AppHandle,
    input: String,
) -> Result<toto::TotoQuickPreview, String> {
    toto::preview_quick_item(&app, &input)
}

#[tauri::command]
fn update_toto_item(
    app: AppHandle,
    id: String,
    content: String,
    planned_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    let item = toto::update_item(&app, &id, content, planned_at, notes)?;
    schedule_toto_item(&app, &item)?;
    sync_reminder_window(&app, &id, item.reminder_status == "待确认");
    Ok(item)
}

#[tauri::command]
fn update_toto_item_full(
    app: AppHandle,
    id: String,
    content: String,
    planned_at: Option<String>,
    reminder_at: Option<String>,
    notes: String,
) -> Result<TotoItem, String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    let item = toto::update_item_full(&app, &id, content, planned_at, reminder_at, notes)?;
    schedule_toto_item(&app, &item)?;
    sync_reminder_window(&app, &id, item.reminder_status == "待确认");
    Ok(item)
}

#[tauri::command]
fn finish_toto_item(app: AppHandle, id: String, completed: bool) -> Result<(), String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    toto::finish_item(&app, &id, completed)?;
    app.state::<DeadlineScheduler>().cancel("toto.item", &id)?;
    sync_reminder_window(&app, &id, false);
    Ok(())
}

#[tauri::command]
fn finish_toto_item_with_note(
    app: AppHandle,
    id: String,
    completed: bool,
    note: String,
) -> Result<(), String> {
    let _guard = TOTO_SYNC_LOCK.lock().map_err(|_| "Toto 调度暂不可用。")?;
    toto::finish_item_with_note(&app, &id, completed, Some(note))?;
    app.state::<DeadlineScheduler>().cancel("toto.item", &id)?;
    sync_reminder_window(&app, &id, false);
    Ok(())
}

#[tauri::command]
fn load_toto_default_remind_minutes(app: AppHandle) -> Result<i64, String> {
    toto::load_default_remind_minutes(&app)
}

#[tauri::command]
fn save_toto_default_remind_minutes(app: AppHandle, minutes: i64) -> Result<(), String> {
    let _guard = DAILY_REVIEW_LOCK
        .lock()
        .map_err(|_| "Toto 配置暂不可用。")?;
    toto::save_default_remind_minutes(&app, minutes)
}

#[tauri::command]
fn load_toto_settings(app: AppHandle) -> Result<BTreeMap<String, String>, String> {
    toto::load_settings(&app)
}

#[tauri::command]
fn save_toto_settings(app: AppHandle, settings: BTreeMap<String, String>) -> Result<(), String> {
    {
        let _guard = DAILY_REVIEW_LOCK
            .lock()
            .map_err(|_| "Toto 配置暂不可用。")?;
        toto::save_settings(&app, settings)?;
    }
    schedule_daily_reviews(&app)?;
    queue_daily_review_check(&app)
}

#[tauri::command]
fn list_json_documents(app: AppHandle) -> Result<Vec<json_documents::JsonDocument>, String> {
    json_documents::list(&app)
}

#[tauri::command]
fn open_json_document(
    app: AppHandle,
    id: String,
) -> Result<json_documents::JsonDocumentSnapshot, String> {
    json_documents::open(&app, &id)
}

#[tauri::command]
fn create_json_document(app: AppHandle) -> Result<json_documents::JsonDocumentSnapshot, String> {
    json_documents::create(&app)
}

#[tauri::command]
fn save_json_document(
    app: AppHandle,
    id: String,
    content: String,
    expected_revision: String,
) -> Result<String, String> {
    json_documents::save(&app, &id, &content, &expected_revision)
}

#[tauri::command]
fn overwrite_json_document(app: AppHandle, id: String, content: String) -> Result<String, String> {
    json_documents::overwrite(&app, &id, &content)
}

#[tauri::command]
fn rename_json_document(
    app: AppHandle,
    id: String,
    name: String,
) -> Result<json_documents::JsonDocument, String> {
    json_documents::rename(&app, &id, &name)
}

#[tauri::command]
fn delete_json_document(app: AppHandle, id: String) -> Result<(), String> {
    json_documents::delete(&app, &id)
}

#[tauri::command]
fn reorder_json_document(
    app: AppHandle,
    id: String,
    order: usize,
) -> Result<Vec<json_documents::JsonDocument>, String> {
    json_documents::reorder(&app, &id, order)
}

#[tauri::command]
fn import_json_documents(
    app: AppHandle,
    paths: Vec<String>,
) -> Result<Vec<json_documents::JsonDocument>, String> {
    json_documents::import(&app, paths)
}

#[tauri::command]
fn copy_json_document_file(app: AppHandle, id: String) -> Result<String, String> {
    json_documents::copy_file_to_clipboard(&app, &id)
}

fn show_main(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        if let Some(position_store) = app.try_state::<WindowPositionStore>() {
            if let Ok(positions) = position_store.0.lock() {
                if let Some(position) = positions.get("main") {
                    let _ = window.set_position(Position::Logical(LogicalPosition::new(
                        position.x, position.y,
                    )));
                }
            }
        }
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
}

fn request_settings_window(app: &AppHandle) -> Result<(), String> {
    app.emit_to("main", "app:open-settings", ())
        .map_err(|error| error.to_string())
}

fn toggle_main(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        if window.is_visible().unwrap_or(false) {
            let _ = window.hide();
        } else {
            show_main(app);
        }
    }
}

fn build_tray(app: &tauri::App) -> tauri::Result<()> {
    let show = MenuItem::with_id(app, "show", "显示 Juju", true, None::<&str>)?;
    let settings = MenuItem::with_id(app, "settings", "设置", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let exit = MenuItem::with_id(app, "exit", "退出", true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&show, &settings, &separator, &exit])?;
    let mut builder = TrayIconBuilder::new()
        .menu(&menu)
        .show_menu_on_left_click(false)
        .tooltip("Juju");

    if let Some(icon) = app.default_window_icon() {
        builder = builder.icon(icon.clone());
    }

    builder
        .on_menu_event(|app, event| match event.id().as_ref() {
            "show" => show_main(app),
            "settings" => {
                if let Err(error) = request_settings_window(app) {
                    log::error!("Could not open the settings window: {error}");
                }
            }
            "exit" => exit_app(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                if app
                    .get_webview_window("main")
                    .and_then(|window| window.is_visible().ok())
                    .unwrap_or(false)
                {
                    if let Some(window) = app.get_webview_window("main") {
                        let _ = window.hide();
                    }
                } else {
                    show_main(app);
                }
            }
        })
        .build(app)?;
    Ok(())
}

#[cfg(test)]
mod shortcut_tests {
    use super::{normalize_shortcut, shortcuts_match, should_return_to_launcher_on_close};

    #[test]
    fn reminder_slots_reuse_gaps_without_releasing_overlapping_windows() {
        let mut slots = std::collections::HashMap::new();
        for index in 0..6 {
            let slot = super::next_reminder_slot(&slots);
            assert_eq!(slot, index.min(3));
            slots.insert(index.to_string(), slot);
        }
        slots.remove("3");
        assert_eq!(super::next_reminder_slot(&slots), 3);
        slots.remove("1");
        assert_eq!(super::next_reminder_slot(&slots), 1);
    }

    #[test]
    fn shortcut_matching_ignores_display_spacing_and_case() {
        assert_eq!(normalize_shortcut("Ctrl + Alt + Space"), "Ctrl+Alt+Space");
        assert!(shortcuts_match("Ctrl + Alt + Space", "CTRL+ALT+SPACE"));
    }

    #[test]
    fn closing_tools_restores_launcher_without_affecting_nested_toto_windows() {
        assert!(should_return_to_launcher_on_close("toto", true));
        assert!(should_return_to_launcher_on_close("tool-json", true));
        assert!(!should_return_to_launcher_on_close("toto-history", true));
        assert!(!should_return_to_launcher_on_close("toto-reminder", true));
        assert!(!should_return_to_launcher_on_close(
            "toto-reminder-123",
            true
        ));
        assert!(!should_return_to_launcher_on_close("tool-json", false));
    }
}

fn exit_app(app: &AppHandle) {
    match AppSettings::apply_pending_data_root(app) {
        Ok(()) => app.exit(0),
        Err(error) => {
            log::error!("Could not apply pending data root: {error}");
            let _ = app.emit("app:storage-error", error);
            show_main(app);
            let _ = request_settings_window(app);
        }
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { .. } = event {
                if let Some(store) = window.app_handle().try_state::<WindowPositionStore>() {
                    if let Ok(mut positions) = store.0.lock() {
                        if let Some(entry) = positions.get_mut(window.label()) {
                            entry.maximized = window.is_maximized().unwrap_or(false);
                        }
                    }
                }
            }
            if let WindowEvent::Moved(position) = event {
                if let Some(store) = window.app_handle().try_state::<WindowPositionStore>() {
                    if let Ok(mut positions) = store.0.lock() {
                        let scale = window.scale_factor().unwrap_or(1.0);
                        if !window.is_maximized().unwrap_or(false) {
                            let entry = positions.entry(window.label().to_string()).or_insert(
                                WindowPosition {
                                    x: 0.0,
                                    y: 0.0,
                                    width: None,
                                    height: None,
                                    maximized: false,
                                },
                            );
                            entry.x = position.x as f64 / scale;
                            entry.y = position.y as f64 / scale;
                        }
                    }
                }
            }
            if let WindowEvent::Resized(size) = event {
                if !window.is_maximized().unwrap_or(false)
                    && !window.is_minimized().unwrap_or(false)
                {
                    if let (Some(store), Ok(position), Ok(scale)) = (
                        window.app_handle().try_state::<WindowPositionStore>(),
                        window.outer_position(),
                        window.scale_factor(),
                    ) {
                        if let Ok(mut positions) = store.0.lock() {
                            let entry = positions.entry(window.label().to_string()).or_insert(
                                WindowPosition {
                                    x: position.x as f64 / scale,
                                    y: position.y as f64 / scale,
                                    width: None,
                                    height: None,
                                    maximized: false,
                                },
                            );
                            entry.width = Some(size.width as f64 / scale);
                            entry.height = Some(size.height as f64 / scale);
                        }
                    }
                }
            }
        })
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            show_main(app);
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_autostart::init(
            tauri_plugin_autostart::MacosLauncher::LaunchAgent,
            None,
        ))
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, shortcut, event| {
                    if event.state() == ShortcutState::Pressed {
                        let shortcut = shortcut.to_string();
                        let status = app.try_state::<LauncherShortcutStatus>();
                        let Some(status) = status else {
                            return;
                        };
                        let is_launcher = status
                            .launcher
                            .lock()
                            .map(|registered| {
                                registered
                                    .as_deref()
                                    .is_some_and(|value| shortcuts_match(value, &shortcut))
                            })
                            .unwrap_or(false);
                        if is_launcher {
                            toggle_main(app);
                            return;
                        }
                        let tool = status.tools.lock().ok().and_then(|registered| {
                            registered.iter().find_map(|(tool, value)| {
                                shortcuts_match(value, &shortcut).then(|| tool.clone())
                            })
                        });
                        if let Some(tool) = tool {
                            let _ = app.emit_to("main", "app:open-tool", tool);
                        }
                    }
                })
                .build(),
        )
        .plugin(
            tauri_plugin_log::Builder::default()
                .level(log::LevelFilter::Info)
                .build(),
        )
        .manage(TaskScheduler::default())
        .manage(DeadlineScheduler::new())
        .manage(ReminderWindows::default())
        .invoke_handler(tauri::generate_handler![
            holidays::holiday_snapshot,
            holidays::holiday_set_overrides,
            holidays::holiday_fetch,
            get_time_context,
            load_app_settings,
            save_app_settings,
            schedule_demo_task,
            cancel_scheduled_task,
            hide_main_window,
            show_main_window,
            open_settings_window_command,
            navigate_settings_window,
            close_window_command,
            get_window_position,
            remember_window_position,
            load_toto_items,
            load_toto_history,
            load_toto_calendar,
            save_toto_calendar,
            download_toto_calendar,
            add_toto_item,
            add_toto_quick_item,
            preview_toto_quick_item,
            update_toto_item,
            update_toto_item_full,
            finish_toto_item,
            finish_toto_item_with_note,
            resolve_toto_reminder,
            load_toto_daily_review,
            load_toto_default_remind_minutes,
            save_toto_default_remind_minutes,
            load_toto_settings,
            save_toto_settings,
            list_json_documents,
            open_json_document,
            create_json_document,
            save_json_document,
            overwrite_json_document,
            rename_json_document,
            delete_json_document,
            reorder_json_document,
            import_json_documents,
            copy_json_document_file
        ])
        .setup(|app| {
            holidays::start_background(app.handle());
            app.manage(WindowPositionStore::default());
            if let Ok(directory) = app.path().app_config_dir() {
                let old_state = directory.join(".window-state.json");
                if let Err(error) = std::fs::remove_file(old_state) {
                    if error.kind() != std::io::ErrorKind::NotFound {
                        log::warn!("Could not remove obsolete window state: {error}");
                    }
                }
            }
            let main_window = app
                .get_webview_window("main")
                .expect("main window must be configured");
            let main_for_close = main_window.clone();
            main_window.on_window_event(move |event| {
                if let WindowEvent::CloseRequested { api, .. } = event {
                    api.prevent_close();
                    let _ = main_for_close.hide();
                }
            });

            build_tray(app)?;

            let mut settings =
                AppSettings::load(&app.handle().clone()).map_err(std::io::Error::other)?;
            let json_settings = settings::load_json_tool_settings(&app.handle().clone())
                .map_err(std::io::Error::other)?;
            if let Some(global_shortcut) = json_settings
                .get("globalShortcut")
                .and_then(serde_json::Value::as_str)
            {
                settings
                    .tool_global_shortcuts
                    .insert("json".into(), global_shortcut.into());
            }
            settings.tool_settings.insert("json".into(), json_settings);
            if let Ok(toto_settings) = toto::load_settings(&app.handle().clone()) {
                if let Some(hotkey) = toto_settings.get("hotkey") {
                    settings
                        .tool_global_shortcuts
                        .insert("toto".into(), hotkey.clone());
                }
            }
            toto::load_items(&app.handle().clone()).map_err(std::io::Error::other)?;
            toto::reconcile_active_history(app.handle()).map_err(std::io::Error::other)?;
            toto::archive_history(&app.handle().clone()).map_err(std::io::Error::other)?;
            for item in toto::load_items(app.handle()).map_err(std::io::Error::other)? {
                if let Err(error) = schedule_toto_item(app.handle(), &item) {
                    log::warn!("Could not schedule Toto item {}: {error}", item.id);
                }
                if item.reminder_status == "待确认" {
                    if let Err(error) = queue_reminder_delivery(app.handle(), &item.id, 0) {
                        log::warn!("Could not restore Toto reminder {}: {error}", item.id);
                    }
                }
            }
            watch_toto_items(app.handle()).map_err(std::io::Error::other)?;
            if let Err(error) = schedule_daily_reviews(app.handle()) {
                log::warn!("Could not schedule Toto daily reviews: {error}");
            }
            if let Err(error) = queue_daily_review_check(app.handle()) {
                log::warn!("Could not check missed Toto daily reviews: {error}");
            }
            let autostart = app.autolaunch();
            let autostart_enabled = autostart.is_enabled().map_err(std::io::Error::other)?;
            if settings.start_at_login && !autostart_enabled {
                autostart.enable().map_err(std::io::Error::other)?;
            } else if !settings.start_at_login && autostart_enabled {
                autostart.disable().map_err(std::io::Error::other)?;
            }
            let launcher_shortcut = normalize_shortcut(&settings.leader_shortcut);
            let shortcut_registered =
                match app.global_shortcut().register(launcher_shortcut.as_str()) {
                    Ok(()) => Some(launcher_shortcut),
                    Err(error) => {
                        log::warn!("Could not register the Launcher shortcut: {error}");
                        None
                    }
                };
            app.manage(LauncherShortcutStatus {
                launcher: Mutex::new(shortcut_registered),
                tools: Mutex::new(BTreeMap::new()),
            });
            if let Err(error) = sync_tool_shortcuts(
                &app.handle().clone(),
                &app.state::<LauncherShortcutStatus>(),
                active_tool_shortcuts(&settings.tool_global_shortcuts),
            ) {
                log::warn!("Could not register one or more tool shortcuts: {error}");
            }

            if settings.start_minimized {
                main_window.hide()?;
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while building Juju desktop application");
}
