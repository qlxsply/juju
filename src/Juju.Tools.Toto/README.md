# Toto（ToolBox 内置日程工具）

Toto 的主窗口、新增/编辑/完成/取消事项窗口、详情、历史查询和提醒窗口均位于
`src/Juju.App/Views/Toto*.xaml`，采用 ToolBox 的 WPF 工具窗口外壳。
`Juju.Tools.Toto` 保留事项模型、CSV/INI 仓储、节假日与工作日规则、快速输入解析和独立于窗口的提醒调度。

## 快捷键与设置

- ToolBox 主界面可打开 Toto；工具列表单键默认 `T`，全局直达键默认 `None`。
- Toto 主窗口内：新增 `Alt+A`、历史 `Alt+Q`、刷新 `Alt+R`、详情 `Alt+D`、
  编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C`。这些按键在 ToolBox 的 Toto 工具设置页配置，
  不注册成系统全局热键。没有独立的 Toto 设置窗口或 `Alt+S` 入口。
- 默认提醒提前 5 分钟；新增事项支持 `内容 @时间`，其中时间可为 `HHmm`、`ddHHmm`、
  `MMddHHmm`、`yyyyMMddHHmm`、连续 `+` 加天或连续 `W` 加周后的星期几。

## 数据与调度

默认数据路径为 `<工具箱数据目录>/tools/toto/`，保留 `config.ini`、`toto_ing.csv`、
`toto_end.csv`、`yyyy.json` 与 `logs/` 的既有格式。首次运行只从 `%USERPROFILE%/.toto/`
复制目标目录中缺失的文件，不覆盖现有数据。原版自启动配置可迁移到 ToolBox；
只有确定原启动快捷方式指向 `toto.exe` 才移除它。

`TotoToolHost` 启动后持有唯一的 `ReminderScheduler`，窗口关闭、隐藏或最小化都不停止提醒。
调度器先持久化“已提醒”状态，再将到期事项通过 ToolBox 的 WPF 同步上下文交给提醒窗口；
上/下班提醒、休眠唤醒、锁屏和系统时间变化继续按原业务规则处理。法定节假日及调休仍在
ToolBox 的 Toto 设置页管理，日历初始化失败时保持五分钟重试，不会影响其他工具启动。
