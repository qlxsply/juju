# HTML 原型到 WPF 界面的映射

参考原型：`windows11_toolbox_demo.html`（用户提供的交互演示）。界面在 Juju 中由原生 WPF/XAML
绘制，不加载演示 HTML、浏览器 localStorage，也不依赖前端 UI 框架。

| HTML 原型 | Juju 代码 | 实际行为 |
| --- | --- | --- |
| `.launcher`、`.tool-row`、`.keycap` | `Views/LauncherWindow.xaml` | 浮窗、工具行、可配置单键、设置按钮 |
| `.settings-side`、`.subnav` | `Views/SettingsWindow.xaml(.cs)` | 应用分组导航、展开工具组、选中标识；左右独立滚动 |
| `.settings-main`、`.setting-card`、`.toggle` | `Settings/SettingsViewFactory.cs`、`Assets/ToolboxStyles.xaml` | 简洁分组卡片与真实 CheckBox 开关 |
| `settings.general` | `Settings/SystemSettingsSection.cs`、`Core/Settings/AppSettings.cs` | 托盘启动、开机启动、托盘提示、失焦隐藏、工具关闭后返回入口 |
| `settings.shortcut` | `Settings/ShortcutSettingsSection.cs`、`Views/LauncherWindow.xaml.cs` | 仅配置工具箱入口热键；工具直达键属于各自工具设置 |
| `settings.storage` | `Settings/StorageSettingsSection.cs` | 选择工具箱数据根，`config.json` 位于所选根目录 |
| `settings.display` | `Settings/DisplaySettingsSection.cs` | 主题设置和 Launcher 列表密度 |
| `settings.updates` | `Settings/AboutSettingsSection.cs` | 本地版本信息；没有在线更新服务时不展示虚构开关 |
| `settings.tools.code` | `Settings/JsonSettingsSection.cs` | 单键、可选直达键、独立目录、窗口行为及 JSON 编辑快捷键 |
| `settings.tools.toto` | `Settings/TotoSettingsSection.cs` | 在同一窗口管理 Toto 热键、提醒与法定节假日 |

原型中的六个工具仅用于演示。当前实际可用的是 JSON 与 Toto；EchoHttp、Kairos 仍明确显示为
“正在开发中”，不会把演示里的剪贴板历史、拾色器等当作真实功能添加。WinForms Toto 的事项、
后台调度与提醒窗体保持原有实现。

## 原型交互与桌面差异

1. 原型把设置当作同一个浏览器桌面里的叠层。WPF 用两个真实 `Window`：Launcher 是设置窗口的
   `Owner`，设置打开时 `SetSettingsOpen(true)` 阻止 Launcher 失焦隐藏；此时再按入口全局热键会
   先关闭设置、保留入口。关闭设置后仍可从原来的工具列表继续操作。
2. 原型 `localStorage` 中的开关即时生效。WPF 中 CheckBox/ComboBox 更改即时保存到 `<数据根>/config.json`；
   正在输入的热键/数字需要点击“保存更改”，避免半输入状态注册错误或写入无效配置。
3. `WindowChrome` + `WindowStyle=None` 替换系统标题栏；设置及 JSON 窗口保留系统缩放。原型用 CSS
   `border-radius`、`box-shadow`，WPF 使用 `Border.CornerRadius`、`ControlTemplate` 和画刷资源。
   外层不再保留 5 DIP 的模拟阴影留边；Windows 11 的 HWND 可通过 DWM 请求系统圆角。
   Launcher 固定 750 DIP 宽、禁用缩放；内容决定高度且最高 900 DIP。多于一屏的工具可通过滚轮
   滚动，但隐藏滚动条。窗口位置可记忆，旧版保存的宽高不会覆盖新布局。
4. 原型模拟任务栏/托盘。Juju 只用 `TrayService` 创建一个真正的 Windows `NotifyIcon`；
   左键显示 Launcher，右键显示真实工具菜单。Toto 隐藏后继续运行定时提醒。
5. JSON “关闭后保留会话”默认沿用原来的隐藏复用行为；关闭此选项后销毁窗口并在下次打开时重建。
   默认最大化和窗口位置记忆均由 `DesktopWindowManager`/`WindowStateService` 落地。

## 数据位置与迁移

默认数据根是 `<安装目录>/data`，应用设置是 `<数据根>/config.json`，JSON/Toto 的默认目录分别是
`<数据根>/tools/json` 和 `<数据根>/tools/toto`。每个工具可在自己的设置页选择独立目录。自定义
数据根时，安装目录的 `data-location.json` 仅存放“下次启动从哪里寻找数据根”的引导路径，
并非应用设置。旧版 `<安装目录>/config.json`、`data/json`、`data/toto` 的缺失文件会复制迁移，
不会删除原文件。运行时修改目录先排队，Juju 正常退出、窗口保存与提醒调度停止后完成复制，
在下一次启动时采用新目录。

## XAML 阅读顺序

从 `App.xaml` 合并资源开始，看 `Assets/ToolboxStyles.xaml` 的按钮模板、导航选中 Trigger 和
开关模板，再读 `LauncherWindow.xaml` 的 `DockPanel` 布局，最后读 `SettingsWindow.xaml.cs`
的 `Select` / `ExpandTools` / `SaveCurrentAsync`：这些方法对应 HTML 中的
`renderNav`、`renderSettingsMain`、`onSettingChange`，但保存落点是真实的 .NET 设置服务。
