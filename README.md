# Juju Desktop

Juju 是使用 Rust、Tauri 2、React、TypeScript、Vite、Tailwind CSS v4、shadcn/ui 和 Zustand 构建的 Windows 桌面工具箱。

## 开发环境

- Windows 11
- Node.js 20.19+（或 22.12+）
- pnpm 12.3.4
- Rust stable，目标 `x86_64-pc-windows-msvc`
- Visual Studio C++ Build Tools 和 Windows SDK
- WebView2 Runtime

## 安装与运行

```powershell
pnpm install --frozen-lockfile
pnpm tauri:dev
```

## 检查与测试

```powershell
pnpm typecheck
pnpm lint
pnpm format:check
pnpm test
pnpm test:e2e
pnpm build
pnpm rust:check
pnpm rust:fmt:check
pnpm rust:test
```

## 打包

```powershell
pnpm package:exe
```

此命令生成独立 Windows 可执行文件，不生成 MSI/NSIS 安装包。

## 项目结构

- `src/`：React 前端、组件与测试
- `src-tauri/`：Rust/Tauri 后端、能力配置与应用资源
- `e2e/`：Playwright 端到端测试
- `scripts/`：构建辅助脚本
