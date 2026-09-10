mod app;
mod core;
mod platform;
mod services;
mod tools;

use std::sync::Arc;

use app::{
    global_shortcut::GlobalShortcutManager, launcher::LauncherManager, lifecycle::AppLifecycle,
};
use core::{
    registry::ToolRegistry, tool_manager::ToolManager, window_manager::WindowManager,
    window_state::WindowStateStore, ActivationTarget, AppState,
};
use tauri::Manager;
use tauri_plugin_global_shortcut::ShortcutState;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let builder = tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            if let Some(state) = app.try_state::<AppState>() {
                state.request_activation(app, ActivationTarget::Launcher);
            }
        }))
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, _shortcut, event| {
                    if event.state == ShortcutState::Pressed {
                        if let Some(state) = app.try_state::<AppState>() {
                            state.request_activation(app, ActivationTarget::Launcher);
                        }
                    }
                })
                .build(),
        )
        .plugin(tauri_plugin_autostart::init(
            tauri_plugin_autostart::MacosLauncher::LaunchAgent,
            None,
        ))
        .setup(|app| {
            let config_dir = app.path().local_data_dir()?.join("juju");
            let config_path = config_dir.join("config.toml");
            let default_data_root = app.path().home_dir()?.join(".juju");
            let window_state = Arc::new(WindowStateStore::load_or_create(&config_dir)?);
            let settings =
                core::settings::SettingsService::load_or_create(config_dir, default_data_root)?;
            let storage = Arc::new(services::storage::StorageManager::load_or_create(
                settings.snapshot().data_root,
            )?);
            let shortcut = settings.snapshot().launcher.shortcut;
            let registry = Arc::new(ToolRegistry::new()?);
            let windows = Arc::new(WindowManager::new(window_state));
            let tools = Arc::new(ToolManager::new(
                Arc::clone(&registry),
                Arc::clone(&windows),
            ));
            let state = AppState::new(
                Arc::new(AppLifecycle::default()),
                Arc::new(settings),
                Arc::clone(&storage),
                registry,
                tools,
                windows,
                Arc::new(GlobalShortcutManager::default()),
                Arc::new(LauncherManager::default()),
            );

            debug_assert_eq!(state.settings.config_path(), config_path);
            debug_assert_eq!(state.settings.snapshot().schema_version, 1);
            debug_assert!(state.registry.get(core::registry::ToolId::Json).is_some());
            app.manage(state);
            storage.start_watching(app.handle().clone())?;
            app.state::<AppState>()
                .global_shortcut
                .register_initial(app.handle(), &shortcut)?;
            app::tray::setup(app)?;

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            app::commands::window_ready,
            app::commands::launcher_close,
            app::commands::launcher_open_target,
            app::commands::json_list_documents,
            app::commands::json_create_document,
            app::commands::json_read_document,
            app::commands::json_write_document,
            app::commands::json_rename_document,
            app::commands::json_delete_document,
            app::commands::app_use_existing_data_root,
            app::commands::app_migrate_data_root,
            app::commands::app_get_settings,
            app::commands::app_update_launcher_settings,
            app::commands::app_set_autostart,
        ]);

    let app = builder
        .build(tauri::generate_context!())
        .expect("failed to build juju");

    app.run(|_app, event| {
        if let tauri::RunEvent::ExitRequested {
            code: None, api, ..
        } = event
        {
            api.prevent_exit();
        }
    });
}
