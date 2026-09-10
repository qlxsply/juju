//! Platform-specific implementations behind stable service boundaries.

#[cfg(target_os = "windows")]
pub(crate) mod windows;
