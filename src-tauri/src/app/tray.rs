use std::error::Error;

use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    App, AppHandle, Manager,
};

use crate::core::{ActivationTarget, AppState};

const OPEN_LAUNCHER_ID: &str = "tray-open-launcher";
const OPEN_JSON_ID: &str = "tray-open-json";
const OPEN_SETTINGS_ID: &str = "tray-open-settings";
const EXIT_ID: &str = "tray-exit";

pub(crate) fn setup(app: &App) -> Result<(), Box<dyn Error>> {
    let open_launcher = MenuItem::with_id(app, OPEN_LAUNCHER_ID, "打开启动器", true, None::<&str>)?;
    let open_json = MenuItem::with_id(app, OPEN_JSON_ID, "JSON", true, None::<&str>)?;
    let open_settings = MenuItem::with_id(app, OPEN_SETTINGS_ID, "设置", true, None::<&str>)?;
    let separator = PredefinedMenuItem::separator(app)?;
    let exit = MenuItem::with_id(app, EXIT_ID, "退出 juju", true, None::<&str>)?;
    let menu = Menu::with_items(
        app,
        &[
            &open_launcher,
            &open_json,
            &open_settings,
            &separator,
            &exit,
        ],
    )?;
    let icon = app
        .default_window_icon()
        .cloned()
        .ok_or_else(|| std::io::Error::other("default application icon is missing"))?;

    TrayIconBuilder::new()
        .icon(icon)
        .tooltip("juju")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id().as_ref() {
            OPEN_LAUNCHER_ID => request_activation(app, ActivationTarget::Launcher),
            OPEN_JSON_ID => request_activation(app, ActivationTarget::Json),
            OPEN_SETTINGS_ID => request_activation(app, ActivationTarget::Settings),
            EXIT_ID => request_exit(app),
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                request_activation(tray.app_handle(), ActivationTarget::Launcher);
            }
        })
        .build(app)?;

    Ok(())
}

fn request_activation(app: &AppHandle, target: ActivationTarget) {
    if let Some(state) = app.try_state::<AppState>() {
        state.request_activation(app, target);
    }
}

fn request_exit(app: &AppHandle) {
    let Some(state) = app.try_state::<AppState>() else {
        return;
    };

    if state.lifecycle.begin_exit() {
        let _ = state.global_shortcut.unregister(app);
        let _ = state.launcher.close(app);
        app.exit(0);
    }
}
