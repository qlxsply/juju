# Juju Desktop

Tauri 2 + React + TypeScript + Vite desktop application. This project lives in
`desktop/` alongside the existing .NET application; it does not replace the
repository-root `src/` or `tests/` directories.

## Requirements

- Windows 11
- Node.js 20.19+ (or 22.12+)
- pnpm 12.3.4
- Rust stable with `x86_64-pc-windows-msvc`
- Visual Studio C++ build tools and Windows SDK
- WebView2 Runtime

## Commands

Run these commands from this directory:

```powershell
pnpm install --frozen-lockfile
pnpm tauri:dev
pnpm typecheck
pnpm lint
pnpm format:check
pnpm test
pnpm test:e2e
pnpm build
pnpm package:exe
pnpm rust:check
pnpm rust:fmt:check
pnpm rust:test
```

The initial window is a stage-zero product shell. Tool functionality and the
design system are intentionally delivered in later stages of
[`../docs/desktop-rewrite-development-plan.md`](../docs/desktop-rewrite-development-plan.md).

`pnpm package:exe` produces a standalone Windows executable at
`desktop/artifacts/Juju.exe`. It deliberately skips MSI/NSIS installer
generation and must be run after each completed development stage.
