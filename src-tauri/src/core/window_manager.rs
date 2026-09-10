use std::{io, path::PathBuf, sync::Arc};

use tauri::{
    AppHandle, Manager, PhysicalPosition, PhysicalSize, WebviewUrl, WebviewWindow,
    WebviewWindowBuilder, WindowEvent,
};
use thiserror::Error;

use crate::{
    core::{
        registry::WindowSpec, window_state::PersistedWindowState, window_state::WindowStateStore,
    },
    platform::windows::monitor::{centered_position, current_monitor_work_area},
};

const SETTINGS_WINDOW: WindowSpec = WindowSpec {
    label: "settings",
    title: "设置 - juju",
    route: "index.html?view=settings",
    default_width: 960,
    default_height: 700,
    min_width: 760,
    min_height: 520,
};

pub(crate) struct WindowManager {
    state: Arc<WindowStateStore>,
}

impl WindowManager {
    pub(crate) fn new(state: Arc<WindowStateStore>) -> Self {
        Self { state }
    }

    pub(crate) fn open_tool(
        &self,
        app: &AppHandle,
        spec: WindowSpec,
    ) -> Result<(), WindowManagerError> {
        self.open(app, spec)
    }

    pub(crate) fn open_settings(&self, app: &AppHandle) -> Result<(), WindowManagerError> {
        self.open(app, SETTINGS_WINDOW)
    }

    fn open(&self, app: &AppHandle, spec: WindowSpec) -> Result<(), WindowManagerError> {
        if let Some(window) = app.get_webview_window(spec.label) {
            window.unminimize()?;
            window.show()?;
            window.set_focus()?;
            return Ok(());
        }

        let window =
            WebviewWindowBuilder::new(app, spec.label, WebviewUrl::App(PathBuf::from(spec.route)))
                .title(spec.title)
                .inner_size(
                    f64::from(spec.default_width),
                    f64::from(spec.default_height),
                )
                .min_inner_size(f64::from(spec.min_width), f64::from(spec.min_height))
                .visible(false)
                .build()?;

        self.restore_or_center(app, &window, spec)?;
        self.track_window(window.clone(), spec.label);
        window.show()?;
        window.set_focus()?;
        persist_window(&self.state, &window, spec.label)?;
        Ok(())
    }

    fn restore_or_center(
        &self,
        app: &AppHandle,
        window: &WebviewWindow,
        spec: WindowSpec,
    ) -> Result<(), WindowManagerError> {
        if let Some(saved) = self.state.get(spec.label) {
            let monitors = app.available_monitors()?;
            if state_intersects_monitors(saved, &monitors) {
                window.set_position(PhysicalPosition::new(saved.x, saved.y))?;
                window.set_size(PhysicalSize::new(saved.width, saved.height))?;
                if saved.maximized {
                    window.maximize()?;
                }
                return Ok(());
            }
        }

        let work_area = current_monitor_work_area()?;
        let size = window.inner_size()?;
        let (x, y) = centered_position(work_area, size.width, size.height);
        window.set_position(PhysicalPosition::new(x, y))?;
        Ok(())
    }

    fn track_window(&self, window: WebviewWindow, label: &'static str) {
        let state = Arc::clone(&self.state);
        let tracked_window = window.clone();
        window.on_window_event(move |event| {
            if matches!(event, WindowEvent::Moved(_) | WindowEvent::Resized(_)) {
                let _ = persist_window(&state, &tracked_window, label);
            }
        });
    }
}

fn persist_window(
    state: &WindowStateStore,
    window: &WebviewWindow,
    label: &str,
) -> Result<(), WindowManagerError> {
    let position = window.inner_position()?;
    let size = window.inner_size()?;
    state.save(
        label,
        PersistedWindowState {
            x: position.x,
            y: position.y,
            width: size.width,
            height: size.height,
            maximized: window.is_maximized()?,
        },
    )?;
    Ok(())
}

fn state_intersects_monitors(saved: PersistedWindowState, monitors: &[tauri::Monitor]) -> bool {
    monitors.iter().any(|monitor| {
        let area = monitor.work_area();
        rectangles_intersect(
            Rectangle::new(saved.x, saved.y, saved.width, saved.height),
            Rectangle::new(
                area.position.x,
                area.position.y,
                area.size.width,
                area.size.height,
            ),
        )
    })
}

#[derive(Clone, Copy)]
struct Rectangle {
    x: i32,
    y: i32,
    width: u32,
    height: u32,
}

impl Rectangle {
    fn new(x: i32, y: i32, width: u32, height: u32) -> Self {
        Self {
            x,
            y,
            width,
            height,
        }
    }
}

fn rectangles_intersect(first: Rectangle, second: Rectangle) -> bool {
    let right = i64::from(first.x) + i64::from(first.width);
    let bottom = i64::from(first.y) + i64::from(first.height);
    let other_right = i64::from(second.x) + i64::from(second.width);
    let other_bottom = i64::from(second.y) + i64::from(second.height);

    i64::from(first.x) < other_right
        && right > i64::from(second.x)
        && i64::from(first.y) < other_bottom
        && bottom > i64::from(second.y)
}

#[derive(Debug, Error)]
pub(crate) enum WindowManagerError {
    #[error("window operation failed: {0}")]
    Tauri(#[from] tauri::Error),
    #[error("failed to determine fallback monitor: {0}")]
    Monitor(#[from] io::Error),
    #[error("failed to persist window state: {0}")]
    State(#[from] crate::core::window_state::WindowStateError),
}

#[cfg(test)]
mod tests {
    use super::{rectangles_intersect, Rectangle};

    #[test]
    fn accepts_partially_visible_rectangles() {
        assert!(rectangles_intersect(
            Rectangle::new(-400, 100, 1_400, 900),
            Rectangle::new(0, 0, 1_920, 1_040),
        ));
    }

    #[test]
    fn rejects_rectangles_outside_a_work_area() {
        assert!(!rectangles_intersect(
            Rectangle::new(-1_600, 0, 1_000, 800),
            Rectangle::new(0, 0, 1_920, 1_040),
        ));
    }
}
