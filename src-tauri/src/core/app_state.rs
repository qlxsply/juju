use std::sync::Arc;

use tauri::AppHandle;

use crate::{
    app::{
        global_shortcut::GlobalShortcutManager, launcher::LauncherManager, lifecycle::AppLifecycle,
    },
    core::{
        registry::{ToolId, ToolRegistry},
        settings::SettingsService,
        tool_manager::ToolManager,
        window_manager::WindowManager,
    },
    services::storage::StorageManager,
};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) enum ActivationTarget {
    Launcher,
    Json,
    Settings,
}

pub(crate) struct AppState {
    pub(crate) lifecycle: Arc<AppLifecycle>,
    pub(crate) settings: Arc<SettingsService>,
    pub(crate) storage: Arc<StorageManager>,
    pub(crate) registry: Arc<ToolRegistry>,
    pub(crate) tools: Arc<ToolManager>,
    pub(crate) windows: Arc<WindowManager>,
    pub(crate) global_shortcut: Arc<GlobalShortcutManager>,
    pub(crate) launcher: Arc<LauncherManager>,
}

impl AppState {
    pub(crate) fn new(
        lifecycle: Arc<AppLifecycle>,
        settings: Arc<SettingsService>,
        storage: Arc<StorageManager>,
        registry: Arc<ToolRegistry>,
        tools: Arc<ToolManager>,
        windows: Arc<WindowManager>,
        global_shortcut: Arc<GlobalShortcutManager>,
        launcher: Arc<LauncherManager>,
    ) -> Self {
        Self {
            lifecycle,
            settings,
            storage,
            registry,
            tools,
            windows,
            global_shortcut,
            launcher,
        }
    }

    pub(crate) fn request_activation(&self, app: &AppHandle, target: ActivationTarget) {
        if self.lifecycle.is_exiting() {
            return;
        }

        let app = app.clone();
        match target {
            ActivationTarget::Launcher => {
                let manager = Arc::clone(&self.launcher);
                tauri::async_runtime::spawn(async move {
                    if let Err(error) = manager.toggle(&app) {
                        #[cfg(debug_assertions)]
                        eprintln!("[launcher] failed to toggle launcher: {error}");
                    }
                });
            }
            ActivationTarget::Json => {
                let manager = Arc::clone(&self.tools);
                tauri::async_runtime::spawn(async move {
                    if let Err(error) = manager.open(&app, ToolId::Json) {
                        #[cfg(debug_assertions)]
                        eprintln!("[tool] failed to open JSON: {error}");
                    }
                });
            }
            ActivationTarget::Settings => {
                let manager = Arc::clone(&self.windows);
                tauri::async_runtime::spawn(async move {
                    if let Err(error) = manager.open_settings(&app) {
                        #[cfg(debug_assertions)]
                        eprintln!("[settings] failed to open settings: {error}");
                    }
                });
            }
        }
    }
}
