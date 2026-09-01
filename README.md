# juju

`juju` 是一个 Windows 常用工具的单仓库。每个一级目录都是独立工具工程，有自己的源代码、依赖说明和使用文档。

| 工具 | 用途 | 技术 |
|---|---|---|
| [`toto`](toto/README.md) | 常驻托盘的事项管理与定时提醒工具 | C#、.NET 10 WinForms、SQLite |
| [`ime_switch`](ime_switch/README.md) | 在切换前台窗口时将中文 IME 调整为英文输入模式 | AutoHotkey v2 |

## 目录约定

- 每个工具都放在仓库根目录下的独立目录中，避免工具之间共享构建输出或依赖。
- 工具内的 README 说明其架构、功能、开发和运行方式。
- 构建产物、IDE 状态、用户数据和本地数据库由根目录 `.gitignore` 排除，不应提交。

## 开发环境

- `toto`：Windows 11、.NET 10 SDK；使用 Rider 或 Visual Studio 时打开 `toto/Toto.sln`。
- `ime_switch`：Windows 11、AutoHotkey v2；运行 `ime_switch/IME_Switch.ahk`。
