use std::{str::FromStr, sync::RwLock};

use tauri::AppHandle;
use tauri_plugin_global_shortcut::{GlobalShortcutExt, Shortcut};
use thiserror::Error;

#[derive(Default)]
pub(crate) struct GlobalShortcutManager {
    registered: RwLock<Option<Shortcut>>,
}

impl GlobalShortcutManager {
    pub(crate) fn register_initial(
        &self,
        app: &AppHandle,
        accelerator: &str,
    ) -> Result<(), GlobalShortcutError> {
        let shortcut = Shortcut::from_str(accelerator)
            .map_err(|error| GlobalShortcutError::Invalid(error.to_string()))?;
        app.global_shortcut().register(shortcut)?;
        *self
            .registered
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(shortcut);
        Ok(())
    }

    pub(crate) fn unregister(&self, app: &AppHandle) -> Result<(), GlobalShortcutError> {
        let shortcut = self
            .registered
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .take();
        if let Some(shortcut) = shortcut {
            app.global_shortcut().unregister(shortcut)?;
        }
        Ok(())
    }
}

#[derive(Debug, Error)]
pub(crate) enum GlobalShortcutError {
    #[error("invalid global shortcut: {0}")]
    Invalid(String),
    #[error("global shortcut registration failed: {0}")]
    Plugin(#[from] tauri_plugin_global_shortcut::Error),
}
