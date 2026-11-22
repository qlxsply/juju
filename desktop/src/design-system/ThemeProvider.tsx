import { useEffect, type ReactNode } from "react";
import { useThemeStore } from "./theme-store";

export function ThemeProvider({ children }: { children: ReactNode }) {
    const mode = useThemeStore((state) => state.mode);

    useEffect(() => {
        const media = window.matchMedia("(prefers-color-scheme: dark)");
        const applyTheme = () => {
            const resolved =
                mode === "system" ? (media.matches ? "dark" : "light") : mode;
            document.documentElement.dataset.theme = resolved;
            document.documentElement.dataset.themeMode = mode;
            document.documentElement.style.colorScheme = resolved;
        };

        applyTheme();
        media.addEventListener("change", applyTheme);
        return () => media.removeEventListener("change", applyTheme);
    }, [mode]);

    return children;
}
