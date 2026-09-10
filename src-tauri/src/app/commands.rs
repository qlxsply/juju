use serde::{Deserialize, Serialize};
use tauri::{AppHandle, State, WebviewWindow};

use crate::{
    app::launcher::LAUNCHER_LABEL,
    core::{ActivationTarget, AppState},
};

#[derive(Clone, Copy, Deserialize)]
#[serde(rename_all = "lowercase")]
pub(crate) enum LauncherSelection {
    Json,
    Settings,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct LauncherReady {
    timeout_ms: u64,
    armed: bool,
}

#[tauri::command]
pub(crate) fn window_ready(
    app: AppHandle,
    window: WebviewWindow,
    state: State<'_, AppState>,
) -> Result<LauncherReady, String> {
    ensure_launcher(&window)?;
    let armed = state
        .launcher
        .ready(&app, &window)
        .map_err(|error| error.to_string())?;
    Ok(LauncherReady {
        timeout_ms: state.settings.snapshot().launcher.timeout_ms,
        armed,
    })
}

#[tauri::command]
pub(crate) fn launcher_close(
    app: AppHandle,
    window: WebviewWindow,
    state: State<'_, AppState>,
) -> Result<(), String> {
    ensure_launcher(&window)?;
    state
        .launcher
        .close(&app)
        .map_err(|error| error.to_string())
}

#[tauri::command]
pub(crate) fn launcher_open_target(
    app: AppHandle,
    window: WebviewWindow,
    state: State<'_, AppState>,
    target: LauncherSelection,
) -> Result<(), String> {
    ensure_launcher(&window)?;
    let target = match target {
        LauncherSelection::Json => ActivationTarget::Json,
        LauncherSelection::Settings => ActivationTarget::Settings,
    };
    state.request_activation(&app, target);
    state
        .launcher
        .close(&app)
        .map_err(|error| error.to_string())
}

fn ensure_launcher(window: &WebviewWindow) -> Result<(), String> {
    if window.label() == LAUNCHER_LABEL {
        Ok(())
    } else {
        Err(format!(
            "launcher command is not available to window {}",
            window.label()
        ))
    }
}
