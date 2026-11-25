import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
    invoke: vi.fn(),
    currentWindow: vi.fn(),
    currentMonitor: vi.fn(),
    windows: [] as Array<{
        label: string;
        options: { height: number };
        closeHandler?: (event: { preventDefault: () => void }) => void;
    }>,
}));

vi.mock("@tauri-apps/api/core", () => ({ isTauri: () => true, invoke: mocks.invoke }));
vi.mock("@tauri-apps/api/window", () => ({ getCurrentWindow: mocks.currentWindow, currentMonitor: mocks.currentMonitor }));
vi.mock("@tauri-apps/api/webviewWindow", () => ({
    WebviewWindow: class {
        static async getByLabel() { return null; }
        label: string;
        options: { height: number };
        closeHandler?: (event: { preventDefault: () => void }) => void;
        constructor(label: string, options: { height: number }) { this.label = label; this.options = options; mocks.windows.push(this); }
        async once(event: string, handler: () => void) { if (event === "tauri://created") handler(); }
        async onCloseRequested(handler: (event: { preventDefault: () => void }) => void) { this.closeHandler = handler; }
        async show() {}
        async unminimize() {}
        async setFocus() {}
    },
}));

import { openToolWindow, openTotoToolWindow } from "./desktop";

describe("tool window close requests", () => {
    beforeEach(() => {
        mocks.windows.length = 0;
        mocks.invoke.mockReset().mockResolvedValue(null);
        mocks.currentWindow.mockReset().mockReturnValue({ label: "main" });
        mocks.currentMonitor.mockReset().mockResolvedValue(null);
    });
    it("sizes Toto near the golden ratio while respecting the monitor work area", async () => {
        mocks.currentMonitor.mockResolvedValue({ scaleFactor: 2, workArea: { size: { height: 1400 } } });
        await openTotoToolWindow();
        expect(mocks.windows[0].options.height).toBe(676);
        mocks.windows.length = 0;
        mocks.currentMonitor.mockResolvedValue({ scaleFactor: 1, workArea: { size: { height: 1080 } } });
        await openTotoToolWindow();
        expect(mocks.windows[0].options.height).toBe(742);
    });

    it("closes the requested child rather than the opener", async () => {
        await openToolWindow("design-system", "设计系统工作台");
        const child = mocks.windows[0];
        const preventDefault = vi.fn();
        expect(child.label).toBe("tool-design-system");
        expect(child.closeHandler).toBeDefined();
        child.closeHandler!({ preventDefault });
        expect(preventDefault).toHaveBeenCalledOnce();
        expect(mocks.invoke).toHaveBeenCalledWith("close_window_command", { label: "tool-design-system" });
        expect(mocks.currentWindow).not.toHaveBeenCalled();
    });
});
