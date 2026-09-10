use std::{
    io,
    path::PathBuf,
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};

use tauri::{
    webview::Webview, AppHandle, Emitter, EventTarget, Manager, PhysicalPosition, WebviewUrl,
    WebviewWindow, WebviewWindowBuilder, WindowEvent,
};
use thiserror::Error;

use crate::platform::windows::monitor::{
    centered_position, current_monitor_work_area, leader_modifiers_released,
};

pub(crate) const LAUNCHER_LABEL: &str = "launcher";
pub(crate) const LAUNCHER_ARMED_EVENT: &str = "launcher://armed";

const LAUNCHER_WIDTH: f64 = 520.0;
const LAUNCHER_HEIGHT: f64 = 326.0;
const MODIFIER_POLL_INTERVAL: Duration = Duration::from_millis(15);

#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
enum LauncherStatus {
    #[default]
    Closed,
    Creating,
    WaitingForModifiers,
    Armed,
}

#[derive(Debug, Default)]
struct LauncherInner {
    generation: u64,
    status: LauncherStatus,
    triggered_at: Option<Instant>,
}

#[derive(Default)]
pub(crate) struct LauncherManager {
    inner: Mutex<LauncherInner>,
}

impl LauncherManager {
    pub(crate) fn toggle(self: &Arc<Self>, app: &AppHandle) -> Result<(), LauncherError> {
        let (generation, should_create) = {
            let mut inner = self
                .inner
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            inner.generation = inner.generation.wrapping_add(1);

            if inner.status != LauncherStatus::Closed
                || app.get_webview_window(LAUNCHER_LABEL).is_some()
            {
                inner.status = LauncherStatus::Closed;
                inner.triggered_at = None;
                (inner.generation, false)
            } else {
                inner.status = LauncherStatus::Creating;
                inner.triggered_at = Some(Instant::now());
                (inner.generation, true)
            }
        };

        if !should_create {
            destroy_window(app)?;
            return Ok(());
        }

        record_milestone("Leader triggered", Duration::ZERO);
        let window = WebviewWindowBuilder::new(
            app,
            LAUNCHER_LABEL,
            WebviewUrl::App(PathBuf::from("index.html?view=launcher")),
        )
        .title("juju")
        .inner_size(LAUNCHER_WIDTH, LAUNCHER_HEIGHT)
        .resizable(false)
        .decorations(false)
        .always_on_top(true)
        .skip_taskbar(true)
        .visible(false)
        .build()?;

        if !self.is_current(generation, LauncherStatus::Creating) {
            window.destroy()?;
            return Ok(());
        }

        self.record_elapsed("Launcher WebView created");
        Ok(())
    }

    pub(crate) fn ready(
        self: &Arc<Self>,
        app: &AppHandle,
        window: &WebviewWindow,
    ) -> Result<bool, LauncherError> {
        if window.label() != LAUNCHER_LABEL {
            return Err(LauncherError::InvalidWindow(window.label().into()));
        }

        let generation = {
            let mut inner = self
                .inner
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if inner.status != LauncherStatus::Creating {
                return Ok(inner.status == LauncherStatus::Armed);
            }
            inner.status = LauncherStatus::WaitingForModifiers;
            inner.generation
        };

        let focus_target = window.clone();
        window.on_window_event(move |event| {
            if matches!(event, WindowEvent::Focused(true)) {
                let webview: &Webview = focus_target.as_ref();
                let _ = webview.set_focus();
            }
        });

        position_on_current_monitor(window)?;
        window.show()?;
        window.set_focus()?;
        let webview: &Webview = window.as_ref();
        webview.set_focus()?;
        self.record_elapsed("React mounted");

        let armed = leader_modifiers_released();
        if armed {
            self.arm(app, generation)?;
        } else {
            let manager = Arc::downgrade(self);
            let app = app.clone();
            thread::spawn(move || loop {
                thread::sleep(MODIFIER_POLL_INTERVAL);
                let Some(manager) = manager.upgrade() else {
                    return;
                };
                if !manager.is_current(generation, LauncherStatus::WaitingForModifiers) {
                    return;
                }
                if leader_modifiers_released() {
                    let _ = manager.arm(&app, generation);
                    return;
                }
            });
        }

        Ok(armed)
    }

    pub(crate) fn close(&self, app: &AppHandle) -> Result<(), LauncherError> {
        {
            let mut inner = self
                .inner
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            inner.generation = inner.generation.wrapping_add(1);
            inner.status = LauncherStatus::Closed;
            inner.triggered_at = None;
        }
        destroy_window(app)?;
        Ok(())
    }

    fn arm(&self, app: &AppHandle, generation: u64) -> Result<(), LauncherError> {
        {
            let mut inner = self
                .inner
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if inner.generation != generation || inner.status != LauncherStatus::WaitingForModifiers
            {
                return Ok(());
            }
            inner.status = LauncherStatus::Armed;
        }

        app.emit_to(
            EventTarget::webview_window(LAUNCHER_LABEL),
            LAUNCHER_ARMED_EVENT,
            (),
        )?;
        self.record_elapsed("Launcher ready for keyboard");
        Ok(())
    }

    fn is_current(&self, generation: u64, expected_status: LauncherStatus) -> bool {
        let inner = self
            .inner
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        inner.generation == generation && inner.status == expected_status
    }

    fn record_elapsed(&self, milestone: &str) {
        let elapsed = self
            .inner
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .triggered_at
            .map(|started| started.elapsed())
            .unwrap_or_default();
        record_milestone(milestone, elapsed);
    }
}

fn position_on_current_monitor(window: &WebviewWindow) -> Result<(), LauncherError> {
    let work_area = current_monitor_work_area()?;

    window.set_position(PhysicalPosition::new(work_area.left, work_area.top))?;
    let size = window.outer_size()?;
    let (x, y) = centered_position(work_area, size.width, size.height);
    window.set_position(PhysicalPosition::new(x, y))?;
    Ok(())
}

fn destroy_window(app: &AppHandle) -> Result<(), tauri::Error> {
    if let Some(window) = app.get_webview_window(LAUNCHER_LABEL) {
        window.destroy()?;
    }
    Ok(())
}

#[cfg(debug_assertions)]
fn record_milestone(milestone: &str, elapsed: Duration) {
    eprintln!("[launcher] {milestone}: {} ms", elapsed.as_millis());
}

#[cfg(not(debug_assertions))]
fn record_milestone(_milestone: &str, _elapsed: Duration) {}

#[derive(Debug, Error)]
pub(crate) enum LauncherError {
    #[error("launcher window operation failed: {0}")]
    Tauri(#[from] tauri::Error),
    #[error("failed to determine launcher monitor: {0}")]
    Monitor(#[from] io::Error),
    #[error("command is not available to window {0}")]
    InvalidWindow(String),
}
