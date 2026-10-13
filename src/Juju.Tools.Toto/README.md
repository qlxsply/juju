# Toto（Juju 内置日程工具）

原独立 .NET 10 WinForms 日程管理工具的完整业务模块已迁入此项目。保留原事项窗体、快速输入、
CSV/INI 持久化、历史分页、节假日下载、锁屏/唤醒/时钟变化时重新调度、定时提醒弹窗、工作日提醒、
提醒状态先写回文件等逻辑。原独立 `Program` / `ApplicationContext` / 第二个托盘图标不再使用；
应用进程、单实例、唯一 Juju 托盘、自启动和全局快捷键由 Juju 宿主统一管理。

## 入口与快捷键

- 左键点击 Juju 托盘图标显示 Launcher；右键菜单可直接打开 JSON/Toto。
- 在 Launcher 点「Toto（日程提醒）」或按 `2` 打开 Toto 原主窗体。
- Toto 默认全局快捷键 `Ctrl+Alt+Space` 直接打开 Toto；在 Toto 自身「设置」中修改。
- JSON 默认全局快捷键 `Ctrl+Alt+J`，在 Juju「基础」设置中修改。
- Juju Launcher 使用自己的全局快捷键，默认 `Ctrl+Shift+Alt+Space`。
- Juju 托盘「Toto 操作」仍可直接进入新增、历史、设置、工作日管理。
- 主窗口内的新增 `Alt+A`、历史 `Alt+Q`、设置 `Alt+S`、刷新 `Alt+R`、详情 `Alt+D`、
  编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C` 维持原行为。

## 数据与生命周期

Toto 数据位于 `<Juju 安装目录>/data/toto/`：`config.ini`、`toto_ing.csv`、`toto_end.csv`、
`yyyy.json` 和 `logs/`。第一次运行时，对 `%USERPROFILE%/.toto/` 中的对应文件只复制缺失项，
不覆盖新目录已有文件，也不删除旧数据。Toto 自身窗口图标使用本项目嵌入的 `toto.ico`；
托盘图标始终为 Juju 的 `juju.ico`。
原配置启用了开机启动且首次成功迁移时，会同步启用 Juju 的开机启动；
只有确认原 `toto.lnk` 指向 `toto.exe` 才移除旧快捷方式，避免系统登录时出现两个托盘图标。

`TotoToolHost` 在 Juju 启动时构造并启动 `ReminderScheduler`。它即使在 Toto 主窗口未显示时
也运行；到期会创建原有的置顶 `ReminderForm`。Juju 退出时释放调度计时器、取消系统事件订阅并
关闭所有 Toto 窗口。Toto 设置中的开机启动开关被路由到 Juju 的统一自启动设置。
若日历首次下载失败，Toto 不会使用错误的节假日规则启动调度；会每五分钟重试，
并在用户尝试进入 Toto 时显示原因，Juju 和 JSON 仍可正常使用。

`Templates/` 和 `legacy-config.ini` 保留原独立项目的数据格式样例用于对照，实际首次运行的文件
由 `ItemRepository` 和 `SettingsRepository` 创建。

## 源码导航

- `TotoToolHost.cs`：Juju 与 Toto 的生命周期、窗口及旧数据迁移边界。
- `Domain/`、`Data/`：事项模型及 CSV/INI 仓储。
- `Services/ReminderScheduler.cs`：后台到期计算与提醒事件。
- `Services/HolidayCalendarDownloadService.cs`：节假日缓存及下载。
- `UI/`：原 WinForms 业务窗体与快捷键交互。
- `tests/Juju.Tools.Toto.Tests/`：事项并发操作与旧数据迁移的回归测试。
