use std::sync::Arc;

use tauri::AppHandle;
use thiserror::Error;

use crate::core::{registry::ToolId, registry::ToolRegistry, window_manager::WindowManager};

pub(crate) struct ToolManager {
    registry: Arc<ToolRegistry>,
    windows: Arc<WindowManager>,
}

impl ToolManager {
    pub(crate) fn new(registry: Arc<ToolRegistry>, windows: Arc<WindowManager>) -> Self {
        Self { registry, windows }
    }

    pub(crate) fn open(&self, app: &AppHandle, tool: ToolId) -> Result<(), ToolManagerError> {
        let descriptor = self
            .registry
            .get(tool)
            .ok_or(ToolManagerError::NotRegistered)?;
        self.windows.open_tool(app, descriptor.window)?;
        Ok(())
    }
}

#[derive(Debug, Error)]
pub(crate) enum ToolManagerError {
    #[error("tool is not registered")]
    NotRegistered,
    #[error("failed to open tool window: {0}")]
    Window(#[from] crate::core::window_manager::WindowManagerError),
}
