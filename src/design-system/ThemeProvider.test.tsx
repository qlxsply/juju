import { cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ThemeProvider } from "./ThemeProvider";
import { useThemeStore } from "./theme-store";

describe("ThemeProvider", () => {
    afterEach(() => {
        cleanup();
        useThemeStore.setState({ mode: "system" });
        document.documentElement.removeAttribute("data-theme");
        document.documentElement.removeAttribute("data-theme-mode");
        document.documentElement.style.colorScheme = "";
        Reflect.deleteProperty(window, "matchMedia");
    });

    it("applies explicit theme changes to the application root", async () => {
        Object.defineProperty(window, "matchMedia", {
            configurable: true,
            value: vi.fn(() => ({
                matches: false,
                addEventListener: vi.fn(),
                removeEventListener: vi.fn(),
            })),
        });
        useThemeStore.setState({ mode: "dark" });

        render(<ThemeProvider>content</ThemeProvider>);

        await waitFor(() => {
            expect(document.documentElement.dataset.theme).toBe("dark");
            expect(document.documentElement.dataset.themeMode).toBe("dark");
        });
    });
});
