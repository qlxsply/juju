//! Shared tool, runtime, settings, and window coordination.

mod app_state;
pub(crate) mod settings;

pub(crate) use app_state::{ActivationTarget, AppState};
