# Toto（ToolBox 内置日程工具）

Toto 的主窗口、新增/编辑/完成/取消事项窗口、详情、历史查询和提醒窗口均位于
`src/Juju.App/Views/Toto*.xaml`，采用 ToolBox 的 WPF 工具窗口外壳。
`Juju.Tools.Toto` 保留事项模型、CSV/JSON 仓储、节假日与工作日规则、快速输入解析和独立于窗口的提醒调度。

## 快捷键与设置

- ToolBox 主界面可打开 Toto；工具列表单键默认 `T`，全局直达键默认 `None`。
- Toto 主窗口内：新增 `Alt+A`、历史 `Alt+Q`、刷新 `Alt+R`、详情 `Alt+D`、
  编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C`。这些按键在 ToolBox 的 Toto 工具设置页配置，
  不注册成系统全局热键。没有独立的 Toto 设置窗口或 `Alt+S` 入口。
- 默认提醒提前 5 分钟；新增事项支持 `内容 @时间`，也可用 `内容 @时间@分钟数` 单独覆盖提前量。
  时间可为 `HHmm`、`ddHHmm`、`MMddHHmm`、`yyyyMMddHHmm`、连续 `+` 加天或连续 `W` 加周后的星期几。

## 数据与调度

默认数据路径为 `<工具箱数据目录>/tools/toto/`，设置保存在 `config.json`，事项保存在
`toto_ing.csv`、`toto_end.csv`、`yyyy.json` 与 `logs/`。每月启动时将上一个完整月份及更早的
历史记录按结束月份写入 `toto_end_yyyyMM.csv`，并在设置 JSON 的 `archiveProgressMonth` 中
记录归档进度；当前历史文件只保留进度月份之后的记录。历史查询会读取相关月份归档和当前文件，
合并后按结束时间倒序筛选、分页。应用不导入旧安装目录、旧配置或旧 CSV 格式。

`TotoToolHost` 启动后持有唯一的 `ReminderScheduler`，窗口关闭、隐藏或最小化都不停止提醒。
调度器先持久化“已提醒”状态，再将到期事项通过 ToolBox 的 WPF 同步上下文交给提醒窗口；
上/下班提醒、休眠唤醒、锁屏和系统时间变化继续按原业务规则处理。法定节假日及调休仍在
ToolBox 的 Toto 设置页管理，日历初始化失败时保持五分钟重试，不会影响其他工具启动。
