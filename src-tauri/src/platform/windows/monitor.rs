use std::{io, mem::size_of};

use windows::Win32::{
    Foundation::{HWND, POINT, RECT},
    Graphics::Gdi::{GetMonitorInfoW, MonitorFromPoint, MONITORINFO, MONITOR_DEFAULTTONEAREST},
    UI::{
        Input::KeyboardAndMouse::{
            GetAsyncKeyState, VIRTUAL_KEY, VK_1, VK_CONTROL, VK_ESCAPE, VK_MENU, VK_NUMPAD1, VK_S,
            VK_SHIFT,
        },
        WindowsAndMessaging::{GetAncestor, GetCursorPos, GetForegroundWindow, GA_ROOT},
    },
};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(crate) struct WorkArea {
    pub(crate) left: i32,
    pub(crate) top: i32,
    pub(crate) right: i32,
    pub(crate) bottom: i32,
}

pub(crate) fn current_monitor_work_area() -> io::Result<WorkArea> {
    unsafe {
        let mut cursor = POINT::default();
        GetCursorPos(&mut cursor).map_err(io::Error::from)?;

        let monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        let mut info = MONITORINFO {
            cbSize: size_of::<MONITORINFO>() as u32,
            ..Default::default()
        };
        GetMonitorInfoW(monitor, &mut info)
            .ok()
            .map_err(io::Error::from)?;

        Ok(work_area_from_rect(info.rcWork))
    }
}

pub(crate) fn leader_modifiers_released() -> bool {
    !key_is_down(VK_CONTROL) && !key_is_down(VK_SHIFT) && !key_is_down(VK_MENU)
}

pub(crate) fn launcher_keys_down() -> [bool; 4] {
    [
        key_is_down(VK_1),
        key_is_down(VK_NUMPAD1),
        key_is_down(VK_S),
        key_is_down(VK_ESCAPE),
    ]
}

pub(crate) fn window_is_foreground(window: HWND) -> bool {
    unsafe {
        let foreground = GetForegroundWindow();
        !foreground.is_invalid() && GetAncestor(foreground, GA_ROOT) == window
    }
}

pub(crate) fn centered_position(work_area: WorkArea, width: u32, height: u32) -> (i32, i32) {
    let available_width = i64::from(work_area.right) - i64::from(work_area.left);
    let available_height = i64::from(work_area.bottom) - i64::from(work_area.top);
    let x = i64::from(work_area.left) + (available_width - i64::from(width)).max(0) / 2;
    let y = i64::from(work_area.top) + (available_height - i64::from(height)).max(0) / 2;

    (x as i32, y as i32)
}

fn key_is_down(key: VIRTUAL_KEY) -> bool {
    unsafe { (GetAsyncKeyState(key.0 as i32) as u16 & 0x8000) != 0 }
}

fn work_area_from_rect(rect: RECT) -> WorkArea {
    WorkArea {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
    }
}

#[cfg(test)]
mod tests {
    use super::{centered_position, WorkArea};

    #[test]
    fn centers_in_a_monitor_with_negative_coordinates() {
        let work_area = WorkArea {
            left: -1_920,
            top: 0,
            right: 0,
            bottom: 1_040,
        };

        assert_eq!(centered_position(work_area, 520, 320), (-1_220, 360));
    }

    #[test]
    fn anchors_oversized_windows_at_the_work_area_origin() {
        let work_area = WorkArea {
            left: 100,
            top: -900,
            right: 1_700,
            bottom: 0,
        };

        assert_eq!(centered_position(work_area, 2_000, 1_000), (100, -900));
    }
}
