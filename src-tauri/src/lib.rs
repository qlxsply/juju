mod app;
mod core;
mod platform;
mod services;
mod tools;

use std::sync::Arc;

use app::{
    global_shortcut::GlobalShortcutManager, launcher::LauncherManager, lifecycle::AppLifecycle,
};
use core::{ActivationTarget, AppState};
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
        .setup(|app| {
            let config_dir = app.path().local_data_dir()?.join("juju");
            let config_path = config_dir.join("config.toml");
            let default_data_root = app.path().home_dir()?.join(".juju");
            let settings =
                core::settings::SettingsService::load_or_create(config_dir, default_data_root)?;
            let shortcut = settings.snapshot().launcher.shortcut;
            let state = AppState::new(
                Arc::new(AppLifecycle::default()),
                Arc::new(settings),
                Arc::new(GlobalShortcutManager::default()),
                Arc::new(LauncherManager::default()),
            );

            debug_assert_eq!(state.settings.config_path(), config_path);
            debug_assert_eq!(state.settings.snapshot().schema_version, 1);
            app.manage(state);
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
