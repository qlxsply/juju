mod json_documents;
#[allow(dead_code)]
mod reminder_queue;
mod scheduler;
mod settings;
mod toto;

use chrono::{SecondsFormat, Utc};
use scheduler::TaskScheduler;
use serde::{Deserialize, Serialize};
use settings::AppSettings;
use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex};
use std::time::Duration;
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
}

struct WindowPositionStore(Arc<Mutex<HashMap<String, WindowPosition>>>);

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
    preference && !matches!(label, "settings" | "toto-history" | "toto-reminder")
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
    positions.insert(label, WindowPosition { x, y });
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
    toto::add_item(&app, content, planned_at, notes)
}

#[tauri::command]
fn add_toto_quick_item(app: AppHandle, input: String) -> Result<TotoItem, String> {
    toto::add_quick_item_command(&app, &input)
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
    toto::update_item(&app, &id, content, planned_at, notes)
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
    toto::update_item_full(&app, &id, content, planned_at, reminder_at, notes)
}

#[tauri::command]
fn finish_toto_item(app: AppHandle, id: String, completed: bool) -> Result<(), String> {
    toto::finish_item(&app, &id, completed)
}

#[tauri::command]
fn finish_toto_item_with_note(
    app: AppHandle,
    id: String,
    completed: bool,
    note: String,
) -> Result<(), String> {
    toto::finish_item_with_note(&app, &id, completed, Some(note))
}

#[tauri::command]
fn load_toto_default_remind_minutes(app: AppHandle) -> Result<i64, String> {
    toto::load_default_remind_minutes(&app)
}

#[tauri::command]
fn save_toto_default_remind_minutes(app: AppHandle, minutes: i64) -> Result<(), String> {
    toto::save_default_remind_minutes(&app, minutes)
}

#[tauri::command]
fn load_toto_settings(app: AppHandle) -> Result<BTreeMap<String, String>, String> {
    toto::load_settings(&app)
}

#[tauri::command]
fn save_toto_settings(app: AppHandle, settings: BTreeMap<String, String>) -> Result<(), String> {
    toto::save_settings(&app, settings)
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
            if let WindowEvent::Moved(position) = event {
                if let Some(store) = window.app_handle().try_state::<WindowPositionStore>() {
                    if let Ok(mut positions) = store.0.lock() {
                        let scale = window.scale_factor().unwrap_or(1.0);
                        positions.insert(
                            window.label().to_string(),
                            WindowPosition {
                                x: position.x as f64 / scale,
                                y: position.y as f64 / scale,
                            },
                        );
                    }
                }
            }
        })
        .plugin(tauri_plugin_single_instance::init(|app, _, _| {
            show_main(app);
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(
            tauri_plugin_window_state::Builder::default()
                .with_denylist(&["main"])
                .skip_initial_state("settings")
                .skip_initial_state("toto")
                .skip_initial_state("toto-history")
                .skip_initial_state("toto-reminder")
                .skip_initial_state("tool-json")
                .skip_initial_state("tool-design-system")
                .skip_initial_state("tool-echo")
                .skip_initial_state("tool-kairos")
                .build(),
        )
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
        .invoke_handler(tauri::generate_handler![
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
            app.manage(WindowPositionStore::default());
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
            toto::archive_history(&app.handle().clone()).map_err(std::io::Error::other)?;
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

            app.state::<TaskScheduler>()
                .schedule_periodic(
                    "toto-reminders",
                    app.handle().clone(),
                    Duration::from_secs(15),
                    |app| {
                        match toto::mark_due_reminders(app) {
                            Ok(items) if !items.is_empty() => {
                                let message = items
                                    .iter()
                                    .map(|item| item.content.as_str())
                                    .collect::<Vec<_>>()
                                    .join("\n");
                                let _ = app.emit("toto:reminder", message.clone());
                            }
                            Ok(_) => {}
                            Err(error) => log::warn!("Could not check Toto reminders: {error}"),
                        }
                        match toto::check_daily_reviews(app) {
                            Ok(reviews) => {
                                for review in reviews {
                                    let _ = app.emit("toto:daily-review", review.clone());
                                }
                            }
                            Err(error) => log::warn!("Could not check Toto daily review: {error}"),
                        }
                    },
                )
                .map_err(std::io::Error::other)?;
            if settings.start_minimized {
                main_window.hide()?;
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while building Juju desktop application");
}
