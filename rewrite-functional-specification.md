# Juju 功能与数据规范（重写依据）

本文按当前源码整理现有 Juju 的可见功能、操作流程、配置含义和本地数据格式，供使用其他技术方案重写时逐项对照。规格中的路径以“数据根目录”为基准；根目录可由用户配置。此文覆盖 Juju 应用、Toto、JSON、MySQL Schema 四部分。按要求不介绍更新检测和自动升级。

## 第一章：Juju 应用

### 1.1 应用定位、启动和生命周期

Juju 是 Windows 桌面工具箱。主界面（Launcher）提供应用设置、工具入口和系统托盘协作；Toto、JSON、MySQL Schema 各自拥有业务窗口和数据目录。关闭/隐藏 Launcher 只会隐藏入口，不等于退出进程。

启动及激活流程：

1. 以安装根目录构造单实例标识并创建系统 Mutex。后续重复启动实例通过本机激活管道请求已运行实例显示 Launcher，然后退出。
2. 读取安装根目录下的 `data-location.json`；没有指针时使用 `<安装根目录>/data`。校验或初始化数据根目录标记 `juju.json`，确保工具目录存在。
3. 加载 `<数据根目录>/config.json`，初始化 JSON 和 MySQL Schema 服务，初始化 Toto 存储及后台运行时，然后注册工具模块、窗口工厂、全局快捷键和托盘。
4. Toto 提醒运行时由应用启动，不依赖 Toto 主窗口是否显示。加载 Launcher 时根据启动偏好显示或先隐藏到托盘。
5. 主界面热键、托盘“显示主界面”和第二实例激活均复用同一个 Launcher 窗口。

Launcher 的使用流程：

- 点击工具行打开工具；每行显示工具名称、说明、图标/字形和可配置的单键提示。
- Launcher 支持方向键在工具行间循环移动焦点；无修饰单键打开对应工具；`Escape` 隐藏入口。
- `Ctrl+Alt+Space` 默认显示/隐藏 Launcher。组合键注册在 Windows 原生全局热键层。
- 失去焦点是否自动隐藏及按住主界面组合键后单键入口等待时长由应用设置控制。
- 工具打开前经过工具目录 readiness 检查；JSON、Toto、MySQL Schema 可用。EchoHttp、Kairos 目前是占位入口，打开时显示“正在开发中”。
- Launcher 顶部齿轮打开设置中心；工具打开时 Launcher 和设置遮罩会收起。应用托盘支持显示 Launcher、打开设置、帮助、退出等操作；系统托盘还可依据设置显示工具隐藏提示。

工具窗口现有关闭行为不同：JSON 是否隐藏复用或实际关闭由 JSON 设置决定；Toto 普通关闭是隐藏，只有应用退出路径会真正关闭；MySQL Schema 关闭后由窗口工厂释放窗口实例，再次打开重新创建。`ReturnToLauncherOnToolClose` 当前用于收到 JSON/Toto 隐藏事件后重新显示 Launcher；不要假设所有工具具有相同的 close/hidden 事件。

### 1.2 应用设置

应用配置是不可变设置快照，修改通常先验证/注册热键，再落盘；失败时恢复控件和之前注册的快捷键。常用开关、下拉列表在控件变化时立即保存；快捷键在录制弹窗确认时提交；数据根目录先暂存，重启时应用。

持久化格式是缩进 JSON，属性名采用 camelCase（例如 `leaderShortcut`、`startMinimized`）；首次运行生成完整默认值。`toolKeys` 是工具 ID 到单字符的字典，`toolGlobalShortcuts` 是占位工具 ID 到组合键的字典。

| 字段/设置项 | 默认值 | 含义与效果 |
| --- | --- | --- |
| `StartAtLogin` / 开机自动启动 | `false` | 通过 Windows 当前用户启动项控制 Juju 登录 Windows 后自动启动。 |
| `LeaderShortcut` / 显示或隐藏主界面 | `Ctrl+Alt+Space` | Windows 全局唤醒组合键；必须可注册且不得与工具全局直达热键冲突。 |
| `LauncherTimeoutMilliseconds` / 组合键超时 | `2000` | 唤醒键尚未完全释放时等待用户输入工具单键的最长时间；有效范围 250–30000 毫秒。 |
| `Theme` / 应用主题 | `System` | `System` 跟随 Windows 应用主题，`Light` 强制浅色，`Dark` 强制深色。主题服务更新 WPF 资源并通知 Monaco 编辑器。 |
| `StartMinimized` / 启动时仅显示托盘图标 | `false` | 为 `true` 时启动后创建 Launcher/注册热键，但初始保持隐藏。 |
| `ShowToolNotifications` / 显示工具状态提示 | `true` | 工具隐藏/关闭时允许托盘显示状态气泡。 |
| `AutoHideLauncher` / 点击入口以外区域时自动隐藏 | `true` | Launcher 失去激活时隐藏；设置窗口打开期间暂停该行为。 |
| `ReturnToLauncherOnToolClose` / 工具关闭后重新显示入口 | `false` | JSON/Toto 窗口触发隐藏事件后显示 Launcher。 |
| `CompactLauncher` / 工具列表密度 | `false` | `false` 行高 53 DIP；`true` 行高 45 DIP。 |
| `ToolKeys` / 工具列表单键 | JSON `J`、Toto `T`、EchoHttp `E`、Kairos `K`、MySQL Schema `M` | 每项必须为唯一的单个字母或数字；Launcher 无修饰键按该单键打开工具。 |
| `ToolGlobalShortcuts` / 占位工具全局直达键 | EchoHttp、Kairos 均为 `None` | 可选全局热键；JSON 和 Toto 的全局直达键分别保存在各自工具配置中。 |
| `PendingDataRoot` / 待应用的数据根目录 | `null` | 用户选定新根目录后暂存；退出流程完成数据刷新后提交根目录指针。 |

快捷键弹窗支持录制、清除（如该项允许 None）、恢复默认、冲突验证。应用主快捷键不能清除、不能为单键、不能使用序列。录制过程中暂时注销系统热键，保存成功后恢复。

设置中心分为“常用设置、快捷键、存储设置、显示设置、更新与版本”以及工具设置。这里需要重写的非更新项如下：

- **常用设置**：开机启动、启动最小化、工具状态提示、Launcher 失焦自动隐藏、工具隐藏后重显 Launcher、唤醒组合键超时。
- **快捷键**：编辑 Launcher 唤醒组合键。JSON、Toto 和占位工具的配置分别在各自工具设置页管理。
- **显示设置**：主题和工具列表密度。
- **存储设置**：查看/打开或选择应用数据根目录；选择后提醒重启，不会复制旧根目录数据。
- **工具设置**：Toto、JSON、EchoHttp、Kairos。MySQL Schema 当前不提供单独设置页，连接配置由工具窗口管理。
- “更新与版本”页显示当前版本号；更新检测、下载和自动升级按本文范围不作说明。

### 1.3 应用数据根目录、配置与关停

默认数据根目录是 `<安装根目录>/data`。安装根目录只保存小型路径指针，不保存工具业务数据：

```text
<安装根目录>/data-location.json    当前数据根目录绝对路径
<数据根目录>/juju.json             根目录标记，当前 schemaVersion=1
<数据根目录>/config.json           Juju 应用设置
<数据根目录>/app/window-state.json Launcher、设置、JSON、Toto 的窗口几何信息
<数据根目录>/app/logs/             应用日志
<数据根目录>/tools/json/            JSON 工具数据
<数据根目录>/tools/toto/            Toto 数据
<数据根目录>/tools/mysqlschema/     MySQL Schema 数据
```

数据根目录切换规则：路径需为绝对路径；不能选在现用根目录的子目录；已有目标目录必须为空或带有效 `juju.json` 标记；拒绝符号链接/联接目录。切换时不会迁移或复制当前文件。正常退出时先初始化目标目录、清空旧根目录 `PendingDataRoot`，再写安装根目录指针；指针提交失败会保留原路径并尽量恢复设置，以便稍后重试。重写时必须显式告知用户切根不会搬数据。

窗口状态用键值形式存放 Left/Top/Width/Height/Maximized。恢复前检查保存矩形仍与显示器工作区相交；Launcher 只恢复位置（尺寸由当前工具列表控制），普通工具窗口可恢复大小和最大化状态。当前持久化键包括 `launcher`、`settings`、`json`、`toto`；MySQL Schema 不进入该窗口状态文件。

应用退出顺序：保存应用设置；请求 JSON 刷新当前文档并进行安全关停；如刷新失败则取消退出；捕获窗口状态、关闭提醒和工具窗口、停止后台模块；最后应用待切换数据根目录并释放托盘、服务和单实例资源。普通关闭请求不应跳过 JSON 保存失败处理。

### 1.4 可见工具目录与非持久数据

当前工具目录顺序至少包括 JSON、Toto、MySQL Schema 和占位的 EchoHttp、Kairos。工具入口的图标、显示名、说明、键位提示来自模块元数据；数据和设置由各工具自己管理。MySQL Schema 连接/快照、Toto 事项、JSON 文档均不保存在 Launcher 配置文件中。

## 第二章：Toto 日程提醒

### 2.1 功能组成和用户工作流

Toto 有进行中主窗、事项编辑/详情弹窗、历史分页窗和后台提醒/盘点弹窗。后台调度器由应用启动，用户隐藏或关闭 Toto 主窗不停止提醒。

**快速新增**：在主窗按“新增”或使用配置热键，输入 `事项内容@计划时间[@提前提醒分钟]`。输入框实时显示解析预览/错误；确认后生成活动事项。没有 `@` 的事项无计划时间和提醒；有计划但不指定提前量时采用 `default_remind_minutes`。快速新增不提供备注输入，备注默认空。

**时间解析**（按本机当前时间解释，时间精度到分钟）：

| 格式 | 解释 | 示例 |
| --- | --- | --- |
| `HHmm` | 今天该时刻 | `写周报@1630` |
| `ddHHmm` | 本月指定日 | `评审@221600` |
| `MMddHHmm` | 本年指定月日 | `评审@11221600` |
| `yyyyMMddHHmm` | 指定年月日 | `发布@202612221600` |
| `+…HHmm` | 每个 `+` 向后推一天 | `复盘@+1600` 为明天 16:00；`++1600` 为后天 16:00 |
| `W…[D]HHmm` | 每个 `W` 向后推一周；可加星期 `D=1…7`（周一至周日），省略 D 沿用今天星期 | `周会@W11600` 为下周一 16:00；`复盘@W1600` 为一周后同星期 |
| `H小时[M分钟]` 或 `M分钟` | 从当前时刻增加相对时间 | `短休@H2M30`、`稍后@M45` |

时间字母不区分大小写。第三个 `@` 段必须是非负整数，作为提醒提前分钟数；负数、无效日期、超过支持段数、多行内容会报错。相对时间的小时/分钟数必须大于零。未填写提醒提前分钟时使用配置默认值 5 分钟。

**进行中列表**：按计划时间升序显示无计划事项在后，计划相同再按创建序号排序。列为事项内容、计划时间、提醒时间、提醒状态、创建时间、备注。提醒状态可见“无提醒、未提醒、已提醒”；状态颜色区分待提醒/已提醒。双击或快捷键查看详情；选择行后可编辑、完成、取消、刷新。

**编辑**：修改内容、计划时间、提醒时间、备注；内容不可空且不能含换行。可分别关闭计划和提醒。编辑计划时间时，若已启用计划，系统按默认提醒提前分钟数自动重算并启用提醒时间。若提醒时间本身未改变，保留原提醒状态；改变提醒时间会重置为“未提醒”，清除上次响铃时间；关闭提醒后状态为“无提醒”。

**完成/取消**：先打开确认及备注窗口；确认后记录结束状态、结束时间和备注，并将事项从 `toto_ing.csv` 移至历史记录。完成和取消都能在历史中查询，历史详情只读。

**历史查询**：按事项内容/备注包含匹配、全部/已完成/已取消状态和结束时间范围筛选；默认状态为全部、时间范围为本月。常用结束时间预设为本周、上一周、本月、上一月、本年、上一年或自定义日期。查询以结束时间为范围，开始日包含、结束日包含（底层用次日排他上界）。分页页大小 10/20/50/100/200/500，默认 10；按结束时间倒序，再按创建序号倒序。双击/Enter 查看详情。

历史查询应用契约另有 ID、单独内容/备注筛选、提醒状态、创建序号区间及计划/提醒/创建/响铃/结束时间区间；当前 WPF 历史页只暴露统一文本搜索、结束状态和结束日期范围。

### 2.2 提醒和每日事项盘点流程

1. Toto 后台启动时确保活动 CSV、当前历史 CSV 和配置文件存在；尝试归档历史；加载当前年以及未来七天跨到的下一年份日历数据。
2. 若日历初始化失败，后台记录错误并每五分钟重试；日历 ready 后启动一次性调度器。即使 Toto 主窗未打开，该调度器仍运行。
3. 调度器挑选最早待提醒事项和下一次每日盘点时间，并附加最长六小时兜底唤醒时间。到期提醒先写入 CSV（Reminded 状态、响铃时间），再通过 App Dispatcher 显示提醒窗，避免恢复后重复提醒。
4. 每日开始/结束盘点分别按日期和类别持久化“已显示”标记，避免重复弹出。开始盘点窗展示进行中事项、对当天事项用绿色强调；结束复盘窗展示进行中和当天已完成事项，并对当天事项使用红色强调。
5. Windows 会话锁定暂停计时器；解锁、电源恢复、系统时间变化时重试日历并立即重新检查/调度。提醒窗播放提示音并闪烁任务栏窗口；应用退出释放系统事件订阅和计时器。

**当前行为注意**：设置 `daily_review_workdays=1` 时，`ReminderScheduler` 直接跳过周六、周日。虽然另有节假日/调休数据管理和 `WorkCalendarService` 工作日判断，当前提醒调度器没有调用该覆盖判断；不能把已编辑的法定节假日理解为当前必然改变盘点日期。

### 2.3 Toto 设置项

Toto 设置位于 `<数据根目录>/tools/toto/config.json` 的 `settings` 对象，JSON 根对象属性采用 camelCase；`settings` 内部以 snake_case 字符串键和字符串值持久化。首次加载缺失键补默认值，未知旧键清除。大部分字段各自立即提交；快捷键在弹窗确认时提交。

| 键 | 默认 | 含义和效果 |
| --- | --- | --- |
| `hotkey` | `None` | Toto 全局直达热键；`None` 只禁用直达，不禁用 Launcher 中 `T` 单键入口。注册失败时不保存并保留旧键。 |
| `shortcut_add` | `Alt+A` | 主窗新增事项。 |
| `shortcut_history` | `Alt+Q` | 打开历史窗口。 |
| `shortcut_refresh` | `Alt+R` | 重新读取进行中列表。 |
| `shortcut_detail` | `Alt+D` | 查看当前选中事项详情。 |
| `shortcut_edit` | `Alt+E` | 编辑选中事项。 |
| `shortcut_complete` | `Alt+F` | 完成选中事项并填写完成备注。 |
| `shortcut_cancel` | `Alt+C` | 取消选中事项并填写取消备注。 |
| `default_remind_minutes` | `5` | 快速新增的默认提前提醒分钟；允许非负整数。 |
| `daily_review_start_time` | 空字符串 | 每日工作开始盘点时间；`HH:mm`，空值表示关闭。 |
| `daily_review_end_time` | 空字符串 | 每日工作结束复盘时间；`HH:mm`，空值表示关闭。 |
| `daily_review_workdays` | `0` | `1` 仅周一至周五触发每日盘点，`0` 周末也允许。 |

日历页可选择年份（2020 年起；进入当前年 12 月后可选择下一年），新增/编辑/删除某日名称及“休息日/调休工作日”类型，或下载该年上游文件。年历文件是按日期排序的 JSON 数组，字段为 `name`、ISO `date` 和布尔 `isOffDay`。

此外，序列化属性使用 camelCase；`config.json` 根对象还保存内部运行状态：`scheduledPopups` 记录按日期与 `work_start` / `work_end` 去重的弹窗已展示时间；`archiveProgressMonth` 记录已完成的历史归档截止月（`yyyyMM`）。它们不是用户设置项，但属于重写时需要保留的行为状态。

Toto 窗口内快捷键必须使用格式有效且互不重复的修饰组合，当前设置不允许 `None`；其余全局快捷键不能与 Launcher、JSON、其他占位工具热键冲突。Toto 工具列表单键 `T` 单独存在于 Juju 的 `ToolKeys`，不是 Toto `config.json` 中的键。

### 2.4 Toto 持久化格式和路径

```text
<数据根目录>/tools/toto/config.json       设置、盘点去重标记、归档进度
<数据根目录>/tools/toto/toto_ing.csv     进行中事项
<数据根目录>/tools/toto/toto_end.csv     尚未归档的历史事项
<数据根目录>/tools/toto/toto_end_yyyyMM.csv 按事项结束月份归档的历史
<数据根目录>/tools/toto/yyyy.json        指定年份的节假日/调休数据缓存
<数据根目录>/tools/toto/logs/            Toto 日志
```

两个事项 CSV 都是 UTF-8（带 BOM）简化 CSV、含表头、通过 `.tmp` 文件写完后替换原文件。引用字段中的逗号、引号、换行按 CSV 双引号规则转义。时间固定格式 `yyyy-MM-dd HH:mm:ss`，空可选时间为空字段。

活动 CSV 九列：`事项ID、事项内容、计划时间、提醒时间、创建时间、创建序号、提醒状态、响铃时间、备注`。提醒状态文本为 `无提醒/未提醒/已提醒`。历史 CSV 九列：`事项ID、事项内容、计划时间、提醒时间、创建时间、创建序号、结束状态、结束时间、备注`，状态文本为 `已完成/已取消`。

每次 Toto runtime 启动时将上一个完整月份及更早历史移入 `toto_end_yyyyMM.csv`，按结束年月分组；月档案内以事项 ID 去重。历史查询从与筛选范围相交的月份档案及当前 `toto_end.csv` 读取并按 ID 合并。历史归档通过 `archiveProgressMonth` 防止每次启动重复扫描；归档失败记录日志，下次启动重试。事项 ID 是随机 GUID 文本；`创建序号` 在现存活动、当前历史和月档案中的最大值基础上递增，用于稳定排序。

年历文件为按日期排序的 JSON 数组，每项包含 `name/date/isOffDay`。缺文件时从 `https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json` 下载，校验年份、日期和休息日字段后原子写入；编辑/删除某日会更新本地年份文件，下载会替换该年份完整缓存。

## 第三章：JSON 格式化

### 3.1 编辑器与文档工作流

JSON 工具是 WPF 窗口与本地 Monaco Editor（WebView2）组合。文档文本保存在独立 `.json` 文件；WPF 负责文件列表、业务命令、保存状态、历史/冲突和剪贴板，编辑器负责 JSON 语言服务、光标、格式化和折叠。

**打开**：首次打开 JSON 窗口时初始化 WebView2 和本地 Monaco 资源，列出文档并选中当前列表首项；列表为空时自动创建 `{}` 新文档。切换文档前先停止自动保存计时并刷新当前编辑器内容；当前文档保存失败或冲突未解决时恢复原选择。文档窗口可隐藏后复用或按设置真正关闭。

**编辑/保存**：编辑器每次内容变化将 `contentChanged` 消息传给 WPF，状态变为 Dirty；停止输入 `AutosaveDelayMilliseconds` 后保存。手动保存、切换文件、关闭窗口、进入文本对比及应用关停也会先刷新当前内容。内容即使不是有效 JSON 也允许存储；无效时显示校验状态，但格式化操作拒绝执行。保存状态至少区分 Clean、Dirty、Saving、SaveFailed、Conflict。

**外部并发修改**：打开文件时计算版本戳（长度、最后写入 UTC 时间、内容 SHA-256）。保存时磁盘版本必须仍匹配打开时版本；不匹配则不覆盖并进入冲突状态。文件观察器对 `.json` 文件名、最后写入、大小变化进行约 150ms 去抖并抑制本应用自身原子写入。文档干净时外部修改自动重新加载；编辑器有未保存内容时弹出三选项：重载磁盘版本、确认覆盖磁盘版本、复制当前文本后重载磁盘版本。

**文档列表**：按文件名筛选（不区分大小写），选择即打开；可以新建、重命名、删除、拖放排序。重命名是列表内联编辑，Enter/失焦提交，Escape 取消。删除将文件移动到 `.trash`，而非立即永久删除。拖动列表条目调整持久化顺序。删除当前项后打开邻近文档；没有文档时列表刷新会自动创建一个。

**导入/导出与复制**：将一个或多个 `.json` 文件拖入窗口即可导入，其他扩展名忽略；内容按文本导入，不要求导入时一定是合法 JSON。重名时加 `-2`、`-3` 等后缀。复制文本复制当前源文本；压缩复制用词法 minifier 去除 JSON 结构空白；复制文件将当前已保存内容写到应用管理的临时文件并通过 Windows FileDrop 剪贴板提供给 Explorer/其他应用。临时 FileDrop 文件超过三天会在编辑器初始化时清理。

**文本对比**：先完成当前保存，再进入选择模式；在左侧列表点两份不同文档指定 A/B，再点击“开始对比”。重复点击已选项会取消该侧选择；两侧均选好后可对比、交换 A/B、重新选择或退出。对比窗口双栏只读，Monaco Diff Editor 按 JSON 解析后以双空格缩进投影；解析失败时用原文本。普通编辑/列表快捷键与 Diff 快捷键分上下文隔离；`Escape` 退出对比。

**格式/编辑命令**：格式化（先要求 JSON 语法正确）、复制文本、复制压缩文本、复制为文件、全部折叠、全部展开、逐层展开。窗口显示有效性、保存状态、光标行列；超大于 10 MiB 的文档显示大文件提示。

### 3.2 JSON 配置项

文件：`<数据根目录>/tools/json/settings.json`。属性名为 camelCase；`shortcuts` 字典键是命令名字符串。保存会规范化已知命令快捷键、字符串枚举和全局热键；新增字段采用默认值，未知快捷键 enum 项会被过滤。开关、数字和主题变化通常立即保存；快捷键通过验证/录制对话框单项提交。

| 字段/设置项 | 默认值 | 含义与效果 |
| --- | --- | --- |
| `AutosaveDelayMilliseconds` / 自动保存延迟 | `800` | 内容停止变化多久触发自动保存；有效范围 100–10000 毫秒。 |
| `IndentSize` / 缩进空格数 | `2` | Monaco 新模型的 tab size，且 `insertSpaces=true`；有效范围 1–8。 |
| `KeepWindowOnClose` / 关闭时保留窗口会话 | `true` | `true` 关闭时隐藏并复用同一窗口/WebView；`false` 真正关闭，重开重新创建。 |
| `OpenMaximized` / 默认最大化打开工具 | `false` | JSON 窗口从隐藏转为打开时设置最大化。 |
| `RememberWindowPlacement` / 记住窗口大小和位置 | `true` | 捕获 `json` 窗口几何状态并在下次创建时恢复；关闭后可禁用恢复。 |
| `GlobalShortcut` / JSON 全局直达热键 | `None` | 可选 Windows 全局热键；`None` 仅禁用直达。不能与 Juju 主唤醒、Toto 或占位工具热键冲突。 |
| `Shortcuts` / JSON 命令快捷键 | 见下表 | 每条命令有独立设置。规范化修饰键顺序为 Ctrl、Alt、Shift；支持单步或两步序列；拒绝保留组合和同一生效上下文重复绑定。 |

默认 JSON 工具列表单键为 Juju 应用配置中的 `ToolKeys.Json = J`，不能与其他工具单键重复。JSON 命令默认快捷键：

| 命令 | 默认 | 处理方/可用上下文 |
| --- | --- | --- |
| 新建 | `Alt+A` | Juju；普通窗口/编辑/列表 |
| 保存 | `Alt+S` | Monaco；普通窗口和编辑器 |
| 格式化 | `Alt+F` | Monaco；普通窗口和编辑器 |
| 复制文本 | `Ctrl+C` | Juju；编辑器 |
| 复制压缩文本 | `Ctrl+Shift+C` | Juju；普通窗口/编辑/列表 |
| 复制文件 | `Ctrl+Alt+Shift+C` | Juju；普通窗口/编辑/列表 |
| 全部折叠 / 全部展开 | `Alt+Q` / `Alt+W` | Monaco；普通窗口和编辑器 |
| 逐层展开 | `Alt+E` | Juju；普通窗口/编辑/列表 |
| 进入对比 | `Alt+D` | Juju；普通窗口/编辑/列表 |
| 退出对比 | `Escape` | Juju；Diff |
| 重命名 / 删除 | `Alt+R` / `Delete` | Juju；文件列表 |

`Window` 绑定在普通编辑或列表上下文内都可生效，另加对应 `Editor` 或 `List` 绑定；Diff 只处理 `Diff` 绑定。文本编辑器自身仍保留其原生按键语义。

### 3.3 JSON 文件数据和恢复

```text
<数据根目录>/tools/json/settings.json           编辑器设置/快捷键
<数据根目录>/tools/json/metadata.json           文档序号与列表元数据
<数据根目录>/tools/json/documents/*.json        JSON 文本文件
<数据根目录>/tools/json/.trash/                 删除文档的带时间戳副本
<数据根目录>/tools/json/cache/clipboard/*.json  “复制文件”临时 FileDrop 文件（超过 3 天清理）
```

文档文件名默认以 UTC `yyyyMMdd` 加当日递增至少三位序号生成（如 `20261120001.json`）；新文件内容 `{}`。元数据 `metadata.json` 使用 schemaVersion=1，包含 UTC 日序列和 `documents[]`；每项有 `id`（GUID）、`fileName`、零基 `order`、`createdAtUtc`、`updatedAtUtc`、`source.kind`（`created/imported/recovered`）及导入时的 `source.originalFileName`。

列表前以磁盘文件为事实做协调：磁盘上缺失的文档从元数据移除，未知 `.json` 文件登记为 recovered，随后连续重排 order；当天默认文件名的最高序号用于修复序列。元数据损坏时尝试复制到 `metadata.json.corrupt-时间戳`，服务使用空元数据扫描 documents 恢复。删除时将文件移动到 `.trash/yyyyMMdd-HHmmss__原文件名`；当前没有 UI 恢复/永久清除入口。启动编辑器只清理 clipboard cache 中超过三天的文件，不自动清理 `.trash`。

多文件更新通过原子写入器和操作锁保护；创建、导入、重命名、删除失败时尽力补偿文件。重命名校验 `.json` 扩展、非法/目录字符、`..` 和 Windows 保留设备名；路径组合后再做 documents 目录边界检查。保存只保存原文，不把内容限制为有效 JSON。

## 第四章：MySQL Schema

### 4.1 入口、连接与配置字段

Launcher 默认单键 `M` 打开 MySQL Schema 主窗口；它没有单独工具设置页，窗口内三页为连接管理、结构备份、结构对比。关闭窗口后下次重新创建。主窗保留应用工具图标、窗口控制和底部状态栏；连接列表统计写入状态栏。

连接管理按名称显示配置，实时搜索名称/主机；新增/编辑共用相同字段布局，编辑时先解密回填密码。表格行操作：编辑、测试、删除、复制；双击只读详情。测试使用真实数据库连接，展示未测试/测试中/连接成功/连接失败，失败详情可通过状态提示查看。删除连接只删除 profile，不级联删除备份树里的历史快照。复制到剪贴板的多行文本包括名称、主机、端口、用户名、明文密码、默认数据库和状态；不得将密码写日志。当前 UI 不提供连接导入/导出按钮；底层 workspace/repository 保留一个可选含密码的 JSON 导出 API，但没有用户可见导入流程。

| 连接字段 | 默认/规则 | 效果 |
| --- | --- | --- |
| 连接名称 | 必填，最多 100 字 | 连接列表展示名；配置中忽略大小写不得重复。 |
| 主机 | 必填 | MySQL server 主机名或地址。 |
| 端口 | `3306`，范围 `1–65535` | MySqlConnector 端口。 |
| 用户名 | 必填 | MySQL 登录用户。 |
| 密码 | 可为空 | 永不明文写 `connections.json`；以 Windows DPAPI `CurrentUser` 范围、固定 entropy 保护。编辑会预填明文、再次保存时重新保护。 |
| 默认数据库 | 可选 | 连接测试和未显式指定数据库的连接采用该默认库；结构向导仍可选择其他有权限的库。 |
| TLS / SSL | `Preferred` | 可选四种 MySqlConnector 模式：`Preferred`、`Required`、`VerifyCA`、`VerifyFull`。 |

连接测试的连接超时 10 秒、命令超时 30 秒；成功执行 `SELECT VERSION()` 取得服务端版本。常见认证失败、数据库不存在、网络/超时、权限不足会转成可读错误说明。读取库列表从 `INFORMATION_SCHEMA.SCHEMATA` 获取，选择库后从 `INFORMATION_SCHEMA.TABLES` 枚举 Base Table 和 View。

### 4.2 结构备份流程

备份树层级为 **环境 → 版本 → 连接 → 数据库 → 表/视图快照文档**。环境可在根节点末尾直接原位新增并 Enter 保存；环境和版本在树节点内原位重命名，Enter/失焦保存、Escape 取消。版本新增仍要求选中环境后输入名称。环境/版本名称在同级忽略大小写唯一。

备份向导根据启动节点预选并锁定相应路径字段：环境/版本/连接/数据库节点能逐步固定路径；未固定时可选现存环境、版本、连接和数据库。选择连接后实时读取数据库；选择数据库后重新读取真实表/视图对象。支持按对象名搜索、按表/视图筛选、勾选对象、全选/清空。该路径下已备份过的同名对象标记且不可覆盖；想重新备份必须先删除旧快照。一个批次按对象逐项读取元数据和 DDL 并独立保存，允许部分成功，最后显示成功/失败及逐项错误；无论连接是否仍存在，历史快照可从树查看、详情对比和导出 JSON。

每个快照从 MySQL 读取数据库 charset/collation、服务端版本、`@@lower_case_table_names`、表/视图类型、列/索引/外键/CHECK/分区/表属性/视图属性、`SHOW CREATE TABLE|VIEW` 原始 DDL 和采集警告。当前只枚举 Base Table 和 View，不枚举存储过程、函数、触发器或事件。MySQL 低于 8.0.16 时 CHECK 元数据会记录采集不完整警告。除结构元数据及 DDL 外不读取或保存行数据。

删除环境或版本会删除其整个备份文件子树；删除备份树中的连接目录、数据库目录或快照文档只清理该树结构快照，不删除“连接管理”中的真实连接配置。删除真实 profile 也保留历史快照。

### 4.3 结构对比和迁移建议

对比 A/B 各自选择来源类型：历史快照，或实时数据库对象。历史来源按环境、版本、备份连接、数据库、文档逐级选择；实时来源按连接、数据库、表/视图选择。每侧保留已选来源，支持交换 A/B。执行时从快照离线读取，或对实时来源重新连接并读结构。仅能表对表、视图对视图；类型不一致时拒绝比较。

结果按概览、数据库、字段、主键、索引、外键、检查约束、分区、选项、视图分类；属性级列出 A/B 值及“相同/缺失/修改”。“仅差异”默认开启；概览给出缺失对象、修改对象和变化属性数。双击对象可看完整属性明细；“元数据 A/B”可查看结构 JSON；“原始 DDL / 变更 SQL”窗口分别显示两侧原始 DDL 和方向可切换的建议 SQL。

SQL 草案是纯文本导出/复制，不会执行到 MySQL。当前仅为部分表变更生成 SQL：新增/修改/删除列；简单 BTREE/HASH 索引新增/修改/删除；标识符反引号转义。索引含表达式、空列、不可见、非 BTREE/HASH、parser/comment 等不支持属性时只警告，不输出危险的半截替换语句。外键、CHECK、分区、表选项变化只生成警告；视图定义变化不自动生成 DDL，提示人工检查 definer、security、algorithm 和权限。SQL 始终附“仅供审阅、不执行、执行前备份并测试”提示。方向 A→B 表示将 A 结构作为目标结构应用到 B 对象；B→A 反向。

### 4.4 MySQL Schema 本地存储

```text
<数据根目录>/tools/mysqlschema/connections.json       MySQL 连接 profile 数组
<数据根目录>/tools/mysqlschema/backup-catalog.json    环境/版本/连接/数据库/快照索引，schemaVersion=1
<数据根目录>/tools/mysqlschema/backups/<envId>/<versionId>/<profileId>/<databaseKey>/<snapshotId>.json
```

`connections.json` 用 camelCase JSON 保存 profile ID、名字、主机、端口、用户、DPAPI 密文、默认库、SSL 模式、创建/更新时间；空密码仍保存 DPAPI 保护后的密文字段。密文以当前 Windows 用户保护，复制整个数据目录给另一个 Windows 账户不会保证可解密。

Catalog 保存独立稳定的环境/版本/连接 profile ID、显示名称、数据库 `lowerCaseTableNames` 及文档引用列表。数据库目录名是数据库名 UTF-8 SHA-256 前 24 个十六进制字符加 `db-` 前缀，避免直接把数据库标识放入磁盘路径。快照 JSON schemaVersion=1，含 snapshot ID/捕获时间、捕获时环境/版本/连接名称、连接 host/port/user 和完整 `SchemaObject`（含原始 DDL、警告）；不含密码。路径引用相对 MySQL Schema 根目录，并在读取时校验不能越出此目录。

同一路径同名结构对象不可重复备份；name 大小写按 MySQL 的 `lower_case_table_names` 规则对比。删除级联先保存更新后的 Catalog，再删除文档文件。备份树与 connection profiles 相互独立，Catalog 的环境/版本 ID 稳定，因此更名不会移动磁盘文件。

## 当前源码定位（重写实施参考）

本文以当前源码和自动化测试为准。按章节快速定位：

- Juju 应用：`src/Juju.App/App.xaml.cs`、`Views/LauncherWindow.xaml(.cs)`、`Views/SettingsWindow.xaml(.cs)`、`Bootstrap/DesktopWindowManager.cs`、`Bootstrap/DesktopAppLifecycle.cs`、`Bootstrap/WindowStateService.cs`、`src/Juju.Core/Settings/AppSettings.cs`、`src/Juju.Core/Storage/`、`src/Juju.App/Settings/`。
- Toto：`src/Juju.Tools.Toto/Application/`、`Domain/Models.cs`、`Infrastructure/Items/`、`Infrastructure/Settings/SettingsRepository.cs`、`Infrastructure/Calendar/`、`Views/TotoMainWindow.xaml(.cs)`、`TotoItemWindow.xaml(.cs)`、`TotoHistoryWindow.xaml(.cs)`、`TotoReminderWindow.xaml(.cs)`、`src/Juju.App/Settings/TotoSettingsSection.cs`。
- JSON：`src/Juju.Tools.Json/Settings/JsonToolSettings.cs`、`Documents/`、`Metadata/`、`Storage/`、`Minify/`、`src/Juju.App/Views/JsonToolWindow.xaml(.cs)`、`src/Juju.App/Services/JsonFileClipboard.cs`、`editor/src/`、`src/Juju.App/Settings/JsonSettingsSection.cs`。
- MySQL Schema：`src/Juju.Tools.MySqlSchema/Domain/`、`Application/`、`Infrastructure/`、`src/Juju.App/Views/MySqlSchemaWindow*.cs`、`src/Juju.App/Views/MySqlSchemaWindow.xaml`、`src/Juju.App/Tools/MySqlSchema/`、`src/Juju.App/Settings/StorageSettingsSection.cs`。

重写时若要改变本规范列出的状态流、字段、归档规则、冲突处理或密码保护策略，应作为有意的产品变更单独确认。
