use std::sync::{Arc, Mutex};

use tauri::AppHandle;

use crate::{
    app::{
        global_shortcut::GlobalShortcutManager, launcher::LauncherManager, lifecycle::AppLifecycle,
    },
    core::settings::SettingsService,
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
    pub(crate) global_shortcut: Arc<GlobalShortcutManager>,
    pub(crate) launcher: Arc<LauncherManager>,
    pending_activation: Mutex<Option<ActivationTarget>>,
}

impl AppState {
    pub(crate) fn new(
        lifecycle: Arc<AppLifecycle>,
        settings: Arc<SettingsService>,
        global_shortcut: Arc<GlobalShortcutManager>,
        launcher: Arc<LauncherManager>,
    ) -> Self {
        Self {
            lifecycle,
            settings,
            global_shortcut,
            launcher,
            pending_activation: Mutex::new(None),
        }
    }

    pub(crate) fn request_activation(&self, app: &AppHandle, target: ActivationTarget) {
        if self.lifecycle.is_exiting() {
            return;
        }

        if target == ActivationTarget::Launcher {
            let manager = Arc::clone(&self.launcher);
            let app = app.clone();
            tauri::async_runtime::spawn(async move {
                if let Err(error) = manager.toggle(&app) {
                    #[cfg(debug_assertions)]
                    eprintln!("[launcher] failed to toggle launcher: {error}");
                }
            });
            return;
        }

        self.queue_activation(target);
    }

    fn queue_activation(&self, target: ActivationTarget) {
        if self.lifecycle.is_exiting() {
            return;
        }

        *self
            .pending_activation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(target);
    }

    #[cfg(test)]
    pub(crate) fn take_pending_activation(&self) -> Option<ActivationTarget> {
        self.pending_activation
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .take()
    }
}

#[cfg(test)]
mod tests {
    use std::{path::PathBuf, sync::Arc};

    use crate::{
        app::{
            global_shortcut::GlobalShortcutManager, launcher::LauncherManager,
            lifecycle::AppLifecycle,
        },
        core::{settings::SettingsService, ActivationTarget},
    };

    use super::AppState;

    #[test]
    fn ignores_activation_after_exit_begins() {
        let lifecycle = Arc::new(AppLifecycle::default());
        let settings = Arc::new(SettingsService::new_for_test(PathBuf::from("config.toml")));
        let state = AppState::new(
            Arc::clone(&lifecycle),
            settings,
            Arc::new(GlobalShortcutManager::default()),
            Arc::new(LauncherManager::default()),
        );

        state.queue_activation(ActivationTarget::Json);
        assert_eq!(
            state.take_pending_activation(),
            Some(ActivationTarget::Json)
        );

        lifecycle.begin_exit();
        state.queue_activation(ActivationTarget::Settings);
        assert_eq!(state.take_pending_activation(), None);
    }
}
