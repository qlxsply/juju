import { useCallback, type MouseEvent } from "react";
import { isTauri } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";

/** Shared drag gesture for borderless desktop windows; controls remain clickable. */
export function useWindowDrag() {
    return useCallback((event: MouseEvent<HTMLElement>) => {
        if (!isTauri() || event.button !== 0) return;
        const target = event.target;
        if (
            target instanceof Element &&
            target.closest("button, a, input, textarea, select, [role=button]")
        ) {
            return;
        }
        event.preventDefault();
        window.dispatchEvent(new CustomEvent("juju:window-drag", { detail: true }));
        void getCurrentWindow()
            .startDragging()
            .catch(() => window.dispatchEvent(new CustomEvent("juju:window-drag", { detail: false })));
    }, []);
}
