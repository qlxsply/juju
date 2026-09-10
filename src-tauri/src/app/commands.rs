use std::path::PathBuf;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, State, WebviewWindow};
use tauri_plugin_autostart::ManagerExt;

use crate::core::settings::AppSettings;
use crate::{
    app::launcher::LAUNCHER_LABEL,
    core::{ActivationTarget, AppState},
    services::storage::{JsonDocument, JsonDocumentSummary, StorageError},
};

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct ApiError {
    code: &'static str,
    message: String,
}

impl From<StorageError> for ApiError {
    fn from(error: StorageError) -> Self {
        let code = match &error {
            StorageError::InvalidDocumentName(_) => "INVALID_DOCUMENT_NAME",
            StorageError::DocumentNotFound(_) => "DOCUMENT_NOT_FOUND",
            StorageError::DocumentAlreadyExists(_) => "DOCUMENT_ALREADY_EXISTS",
            StorageError::ExternalModificationConflict => "EXTERNAL_MODIFICATION_CONFLICT",
            StorageError::DataRootUnavailable(_) => "DATA_ROOT_UNAVAILABLE",
            StorageError::InvalidDataRoot(_) => "DATA_ROOT_INVALID",
            StorageError::Io { .. } | StorageError::Watcher(_) => "STORAGE_IO_ERROR",
        };
        Self {
            code,
            message: error.to_string(),
        }
    }
}

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

#[tauri::command]
pub(crate) fn json_list_documents(
    state: State<'_, AppState>,
) -> Result<Vec<JsonDocumentSummary>, ApiError> {
    state.storage.list_documents().map_err(ApiError::from)
}

#[tauri::command]
pub(crate) fn json_create_document(state: State<'_, AppState>) -> Result<JsonDocument, ApiError> {
    state.storage.create_document().map_err(ApiError::from)
}

#[tauri::command]
pub(crate) fn json_read_document(
    state: State<'_, AppState>,
    document_id: String,
) -> Result<JsonDocument, ApiError> {
    state
        .storage
        .read_document(document_id)
        .map_err(ApiError::from)
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn json_write_document(
    state: State<'_, AppState>,
    document_id: String,
    content: String,
    expected_revision: String,
) -> Result<JsonDocument, ApiError> {
    state
        .storage
        .write_document(document_id, content, expected_revision)
        .map_err(ApiError::from)
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn json_rename_document(
    state: State<'_, AppState>,
    document_id: String,
    new_name: String,
) -> Result<(), ApiError> {
    state
        .storage
        .rename_document(document_id, new_name)
        .map_err(ApiError::from)
}

#[tauri::command]
pub(crate) fn json_delete_document(
    state: State<'_, AppState>,
    document_id: String,
) -> Result<(), ApiError> {
    state
        .storage
        .delete_document(document_id)
        .map_err(ApiError::from)
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn app_use_existing_data_root(
    app: AppHandle,
    state: State<'_, AppState>,
    data_root: PathBuf,
) -> Result<(), ApiError> {
    let previous_root = state.storage.root();
    state
        .storage
        .use_existing_root(data_root.clone())
        .map_err(ApiError::from)?;
    if let Err(error) = state.settings.update_data_root(data_root) {
        let _ = state.storage.use_existing_root(previous_root);
        return Err(ApiError {
            code: "STORAGE_IO_ERROR",
            message: error.to_string(),
        });
    }
    state.storage.start_watching(app).map_err(ApiError::from)
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn app_migrate_data_root(
    app: AppHandle,
    state: State<'_, AppState>,
    data_root: PathBuf,
) -> Result<(), ApiError> {
    let previous_root = state.storage.root();
    state
        .storage
        .migrate_to(data_root.clone())
        .map_err(ApiError::from)?;
    if let Err(error) = state.settings.update_data_root(data_root) {
        let _ = state.storage.use_existing_root(previous_root);
        return Err(ApiError {
            code: "STORAGE_IO_ERROR",
            message: error.to_string(),
        });
    }
    state.storage.start_watching(app).map_err(ApiError::from)
}

#[tauri::command]
pub(crate) fn app_get_settings(state: State<'_, AppState>) -> AppSettings {
    state.settings.snapshot()
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn app_update_launcher_settings(
    app: AppHandle,
    state: State<'_, AppState>,
    shortcut: String,
    timeout_ms: u64,
) -> Result<(), ApiError> {
    state
        .global_shortcut
        .replace(&app, &shortcut)
        .map_err(|error| ApiError {
            code: "SHORTCUT_REGISTRATION_FAILED",
            message: error.to_string(),
        })?;
    state
        .settings
        .update_launcher(shortcut, timeout_ms)
        .map_err(|error| ApiError {
            code: "STORAGE_IO_ERROR",
            message: error.to_string(),
        })
}

#[tauri::command(rename_all = "camelCase")]
pub(crate) fn app_set_autostart(
    app: AppHandle,
    state: State<'_, AppState>,
    enabled: bool,
) -> Result<(), ApiError> {
    if enabled {
        app.autolaunch().enable()
    } else {
        app.autolaunch().disable()
    }
    .map_err(|error| ApiError {
        code: "STORAGE_IO_ERROR",
        message: error.to_string(),
    })?;
    state
        .settings
        .update_autostart(enabled)
        .map_err(|error| ApiError {
            code: "STORAGE_IO_ERROR",
            message: error.to_string(),
        })
}
