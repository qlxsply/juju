import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import tseslint from "typescript-eslint";

export default tseslint.config(
    {
        ignores: [
            "dist",
            "coverage",
            "playwright-report",
            "test-results",
            "src-tauri/target",
            "src-tauri/gen",
        ],
    },
    js.configs.recommended,
    ...tseslint.configs.recommended,
    {
        files: ["**/*.{ts,tsx}"],
        languageOptions: {
            globals: {
                console: "readonly",
                document: "readonly",
                window: "readonly",
            },
            parserOptions: {
                ecmaFeatures: { jsx: true },
            },
        },
        plugins: {
            "react-hooks": reactHooks,
            "react-refresh": reactRefresh,
        },
        rules: {
            "no-undef": "off",
            ...reactHooks.configs.recommended.rules,
            "react-refresh/only-export-components": [
                "warn",
                { allowConstantExport: true },
            ],
        },
    },
    {
        files: ["src/App.tsx", "src/features/**/*.{ts,tsx}"],
        rules: {
            "no-restricted-imports": [
                "error",
                {
                    patterns: [
                        {
                            group: [
                                "./components/ui/*",
                                "../components/ui/*",
                                "@/components/ui/*",
                            ],
                            message:
                                "Use Ju public components instead of shadcn/ui primitives in pages.",
                        },
                    ],
                },
            ],
        },
    },
    {
        files: ["*.js", "scripts/**/*.mjs"],
        languageOptions: {
            globals: {
                process: "readonly",
                URL: "readonly",
                console: "readonly",
            },
        },
    },
);
