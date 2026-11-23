import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vitest/config";

export default defineConfig({
    plugins: [react(), tailwindcss()],
    clearScreen: false,
    resolve: {
        alias: {
            "@": fileURLToPath(new URL("./src", import.meta.url)),
        },
    },
    server: {
        strictPort: true,
    },
    test: {
        include: ["src/**/*.{test,spec}.{ts,tsx}"],
        environment: "jsdom",
        setupFiles: ["./src/test/setup.ts"],
        restoreMocks: true,
    },
});
