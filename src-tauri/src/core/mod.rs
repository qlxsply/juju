//! Shared tool, runtime, settings, and window coordination.

mod app_state;
pub(crate) mod registry;
pub(crate) mod settings;
pub(crate) mod tool_manager;
pub(crate) mod window_manager;
pub(crate) mod window_state;

pub(crate) use app_state::{ActivationTarget, AppState};
