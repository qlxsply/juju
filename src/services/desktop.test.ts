import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
    invoke: vi.fn(),
    currentWindow: vi.fn(),
    windows: [] as Array<{
        label: string;
        closeHandler?: (event: { preventDefault: () => void }) => void;
    }>,
}));

vi.mock("@tauri-apps/api/core", () => ({ isTauri: () => true, invoke: mocks.invoke }));
vi.mock("@tauri-apps/api/window", () => ({ getCurrentWindow: mocks.currentWindow }));
vi.mock("@tauri-apps/api/webviewWindow", () => ({
    WebviewWindow: class {
        static async getByLabel() { return null; }
        label: string;
        closeHandler?: (event: { preventDefault: () => void }) => void;
        constructor(label: string) { this.label = label; mocks.windows.push(this); }
        async once(event: string, handler: () => void) { if (event === "tauri://created") handler(); }
        async onCloseRequested(handler: (event: { preventDefault: () => void }) => void) { this.closeHandler = handler; }
        async show() {}
        async unminimize() {}
        async setFocus() {}
    },
}));

import { openToolWindow } from "./desktop";

describe("tool window close requests", () => {
    beforeEach(() => {
        mocks.windows.length = 0;
        mocks.invoke.mockReset().mockResolvedValue(null);
        mocks.currentWindow.mockReset().mockReturnValue({ label: "main" });
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
