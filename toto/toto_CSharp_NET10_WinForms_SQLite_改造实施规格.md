# toto 定时任务提醒工具改造实施规格

**目标技术栈：C# .NET 10 WinForms + SQLite**  
**版本：v1.0**  
**日期：2026-08-27**

## 0. 给执行 AI 的总指令

本项目不是“参考原程序重新设计”，而是**对现有 AutoHotkey v2 程序进行等价迁移并扩展功能**。现有 `toto.ahk` 是旧版本行为的基准（behavioral baseline）。除本规格明确要求新增或调整的内容外，原有用户可见功能、默认值、快捷键语义、排序、提醒补发逻辑、托盘行为、编辑/完成/取消流程均必须保持一致。

必须采用以下技术方案：

- C#，目标框架 `net10.0-windows`。
- WinForms，不使用 WPF、WinUI、Avalonia、MAUI、WebView2、Tauri、Electron 或浏览器 UI。
- SQLite，使用 `Microsoft.Data.Sqlite` 直接访问，不引入 EF Core/ORM。
- 主列表和历史列表使用 `DataGridView`。
- **禁止为了网格线使用 CellPainting、Paint、GDI/GDI+、P/Invoke 等方式自行画横线/竖线。** 网格线只使用 DataGridView 原生 `CellBorderStyle` / `ColumnHeadersBorderStyle`。
- 历史事项必须分页查询，默认每页 200 条；不能一次把全部历史记录加载到内存和 DataGridView。
- 不使用 1 秒轮询。提醒与工作日弹窗统一采用“计算下一触发点 + 单次 Timer”的调度方式。
- 常驻进程必须以低资源占用为优先：不启动本地服务、不使用 Web runtime、不使用通用 Host/DI 框架、不维持高频数据库扫描。
- 所有数据库查询必须参数化，不能拼接用户输入形成 SQL。
- 迁移必须保留旧数据；首次迁移前自动备份 CSV/INI，迁移失败时不得破坏原文件。

实现时应先完成数据库与迁移，再完成原功能等价迁移，最后实现新增功能。不要在功能尚未等价时删除旧 AHK 数据文件。

---

## 1. 改造目标与范围

### 1.1 主要目标

1. 解决现有 AHK `ListView + NM_CUSTOMDRAW + GDI` 自绘网格在数据量增大、滚动、DPI 缩放和重绘时出现横线错位的问题。
2. 将 CSV 全量读取/排序/全量重写的数据方式迁移为 SQLite 增量读写。
3. 保持现有功能和使用习惯不变。
4. 增加全字段查询、时间范围查询和历史分页。
5. 增加工作日/休息日管理。
6. 增加“工作日上班时间/下班时间自动弹出全部进行中事项”的定时提醒。
7. Windows 11 常驻运行时保持低 CPU、低 I/O、可控内存占用。

### 1.2 明确不做

- 不改造成多人/网络/云同步系统。
- 不增加 Web 服务或后台 Windows Service。
- 不引入 EF Core。
- 不把 SQLite 替换为 SQL Server/MySQL/PostgreSQL。
- 不为了视觉效果重做 DataGridView 网格绘制。
- 不在第一版增加复杂全文检索引擎；事项内容和备注使用 SQLite `LIKE` 做包含式模糊搜索。
- 不改变现有事项状态语义。

---

## 2. 现有功能基线：必须完整保留

以下行为来自原 `toto.ahk`，是迁移验收基线。

### 2.1 运行与单实例

- 应用常驻托盘。
- 同一 Windows 用户会话只允许一个实例运行。
- 再次启动程序时，新实例立即退出，并通知已运行实例显示主窗口。
- 主窗口关闭或按 Esc 时仅隐藏，不退出应用。
- 托盘菜单至少保留：打开 toto、新增事项、历史事项、设置、退出。
- 支持“登录 Windows 后自动启动 toto”。

### 2.2 全局与应用内快捷键

默认全局唤醒快捷键：`Ctrl+Alt+Space`，可配置，并允许包含 Win 修饰键。

主窗口激活时默认应用内快捷键：

| 功能 | 默认快捷键 |
|---|---|
| 新增 | Alt+A |
| 历史 | Alt+Q |
| 设置 | Alt+S |
| 刷新 | Alt+R |
| 详情 | Alt+D |
| 编辑 | Alt+E |
| 完成 | Alt+F |
| 取消 | Alt+C |

要求：

- 八个应用内快捷键可配置。
- 应用内快捷键只允许 Ctrl / Alt / Shift 等，不允许 Win 修饰键。
- 八个快捷键不能重复。
- 设置保存后立即生效，不要求重启。
- 历史窗口激活时仍允许使用“详情”快捷键查看历史事项详情。

### 2.3 进行中事项主列表

显示列保持：

1. 事项内容
2. 计划时间
3. 提醒时间
4. 提醒状态
5. 创建时间
6. 备注

排序规则保持：

- 有计划时间的事项优先；计划时间升序。
- 无计划时间的事项排在有计划时间事项之后。
- 计划时间相同时按创建序号 `created_seq` 升序。

颜色规则保持：

- 计划时间距离当前时间小于等于 1 小时（包含已过期）时：红色文字。
- 否则，计划时间为今天：绿色文字。
- 否则，计划时间为明天：黄色/棕黄色文字。
- 其他事项使用默认文字颜色。
- 选中行时不得因为自定义颜色导致文字不可读。

DataGridView 只能通过 `CellFormatting` 或行/单元格 Style 修改文字颜色；**不能为了网格线进行自绘**。

### 2.4 新增事项

保留快速输入格式：

`事项内容[@计划时间[@提前提醒分钟数]]`

计划时间支持：

- `HHmm`
- `ddHHmm`
- `MMddHHmm`
- `yyyyMMddHHmm`
- `+HHmm`
- `++HHmm`
- 更多 `+` 表示再增加对应天数

规则保持：

- 事项内容不能为空、不能包含换行。
- 快速新增模式中的事项内容不能包含 `@`。
- 若设置提前提醒，必须先有计划时间。
- 省略提前提醒分钟数时使用设置中的默认值；默认 5 分钟。
- 提前提醒分钟数为非负整数。
- 快速新增生成的提醒时间 = 计划时间 - 提前提醒分钟数。
- 计划时间必须是有效未来时间；相对数字格式不自动滚动到下一日/下一月/下一年。
- 新事项生成 GUID 作为事项 ID。
- 创建时间记录到秒。
- 创建序号单调增加，用于稳定排序。
- 无提醒时提醒状态为“无提醒”；有提醒时初始为“未提醒”。

### 2.5 编辑事项

编辑时保留独立字段：事项内容、计划时间、提醒时间、备注。

- 编辑界面中的时间格式为 `yyyy-MM-dd HH:mm:ss`。
- 计划时间和提醒时间均可为空。
- 编辑时事项内容允许包含 `@`。
- 编辑提醒时间不会随着计划时间自动联动。
- 将提醒时间清空：提醒状态变为“无提醒”，响铃时间清空。
- 修改提醒时间：提醒状态重新变为“未提醒”，响铃时间清空。
- 仅修改事项内容、计划时间或备注且提醒时间未变化时，不重置已经存在的提醒状态。

### 2.6 详情

进行中事项详情至少显示：

- 事项内容
- 计划时间
- 提醒时间
- 提醒状态
- 响铃时间
- 创建时间
- 备注

历史事项详情至少显示：

- 事项内容
- 计划时间
- 提醒时间
- 结束状态
- 结束时间
- 创建时间
- 备注

双击列表行可打开详情。

### 2.7 完成与取消

- 进行中事项可以标记为“已完成”或“已取消”。
- 操作前弹出确认窗口，并允许补充/修改备注。
- 备注输入框默认带入事项当前备注。
- 确认后事项从进行中列表转为历史事项。
- 结束时间使用当前本地时间，精确到秒。
- 结束后需要立即从普通提醒队列移除并刷新相关窗口。
- 数据库实现必须在**同一个 SQLite transaction** 中完成状态和结束时间更新，不能再使用“先写历史 CSV 再删进行中 CSV”的双文件补偿逻辑。

### 2.8 历史事项

历史显示列保持：

1. 事项内容
2. 计划时间
3. 提醒时间
4. 结束状态
5. 结束时间
6. 创建时间
7. 备注

排序规则保持：

- 结束时间降序。
- 结束时间相同则创建序号降序。

新增要求：历史必须数据库分页，默认 200 条/页，并显示总条数、当前页/总页数，提供首页、上一页、下一页、末页或等价操作。

### 2.9 单事项定时提醒

- 只有提醒状态为“未提醒”且提醒时间非空的进行中事项进入调度。
- 到达提醒时间后先持久化为“已提醒”并记录响铃时间，再显示提醒窗口，以防异常退出后重复提醒。
- 一次到期的多条事项合并到同一提醒窗口。
- 提醒窗口置顶、不自动关闭，关闭窗口不代表完成事项。
- 提醒窗口显示事项内容、计划时间、提醒时间。
- 支持在提醒窗口完成选中事项。
- 提醒时保留声音提示和窗口闪烁/激活行为。
- 锁屏期间不把到期事项标记为已提醒；解锁后补发。
- 系统休眠恢复、系统时间变化、解锁后重新计算到期提醒和下一次触发点。
- 调度不能高频轮询；继续采用一次性 Timer，并允许每次最长等待 6 小时后低频校准。

---

## 3. 目标技术架构

### 3.1 项目结构

建议 solution：

```text
Toto.sln
└─ src/
   └─ Toto.App/
      ├─ Program.cs
      ├─ TotoApplicationContext.cs
      ├─ AppPaths.cs
      ├─ Domain/
      │  ├─ TodoItem.cs
      │  ├─ ItemStatus.cs
      │  ├─ ReminderStatus.cs
      │  ├─ DayType.cs
      │  └─ QueryCriteria.cs
      ├─ Data/
      │  ├─ TotoDatabase.cs
      │  ├─ DatabaseInitializer.cs
      │  ├─ LegacyMigrationService.cs
      │  ├─ ItemRepository.cs
      │  ├─ SettingsRepository.cs
      │  ├─ WorkCalendarRepository.cs
      │  └─ ScheduledPopupLogRepository.cs
      ├─ Services/
      │  ├─ ReminderScheduler.cs
      │  ├─ WorkCalendarService.cs
      │  ├─ HotkeyService.cs
      │  ├─ SingleInstanceService.cs
      │  ├─ StartupService.cs
      │  └─ SystemEventService.cs
      └─ UI/
         ├─ MainForm.cs
         ├─ ItemEditForm.cs
         ├─ ItemDetailForm.cs
         ├─ EndItemForm.cs
         ├─ HistoryForm.cs
         ├─ ReminderForm.cs
         ├─ WorkdaySummaryForm.cs
         ├─ SettingsForm.cs
         ├─ WorkCalendarForm.cs
         └─ Controls/
            └─ TimeRangeFilterControl.cs
```

不要为了“分层”引入大量抽象接口、Mediator、消息总线或通用 DI 容器。该程序是单进程小工具，优先保持代码直接、可读、低开销。

### 3.2 WinForms 生命周期

使用自定义 `ApplicationContext` 管理托盘与窗口生命周期：

- 启动后创建托盘图标和后台调度器。
- 主窗口可以被隐藏/销毁并重新创建，但隐藏主窗口不能导致进程退出。
- 只有用户选择“退出”才调用 `Application.Exit()`。
- 托盘图标建议嵌入 exe 资源，减少外部文件依赖。

### 3.3 DataGridView 要求

- `AutoGenerateColumns = false`，显式定义业务列。
- `ReadOnly = true`（主列表/历史列表/提醒列表）。
- 单选行，FullRowSelect。
- 禁止用户直接在表格单元格中编辑；编辑继续通过编辑窗口。
- 使用原生网格：如 `CellBorderStyle = DataGridViewCellBorderStyle.Single`。
- 不实现 `CellPainting` 画网格线。
- 不实现 GDI/GDI+ 横线修正。
- 不基于像素坐标手工计算行底部。
- 允许使用 `CellFormatting` 设置紧急程度文字颜色。
- DPI 缩放由 WinForms/系统处理，不写 `-1px`、`rowBottom-1` 等视觉补偿逻辑。

---

## 4. 数据目录与 SQLite

### 4.1 数据目录

继续沿用：

`%USERPROFILE%\.toto`

新数据库：

`%USERPROFILE%\.toto\toto.db`

旧文件：

- `config.ini`
- `toto_ing.csv`
- `toto_end.csv`

首次成功迁移后保留旧文件，不主动删除。建议在迁移前再创建一次只读备份副本：

`%USERPROFILE%\.toto\legacy_backup\yyyyMMdd_HHmmss\`

### 4.2 SQLite 连接设置

推荐：

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA busy_timeout = 5000;
```

应用是单实例，但仍应把一次业务操作包装在 transaction 中。

### 4.3 items 表

统一用一张表保存进行中和历史事项，避免双文件/双表搬移。

```sql
CREATE TABLE IF NOT EXISTS items (
    id              TEXT PRIMARY KEY,
    content         TEXT NOT NULL,
    planned_at      TEXT NULL,
    remind_at       TEXT NULL,
    created_at      TEXT NOT NULL,
    created_seq     INTEGER NOT NULL,
    status          INTEGER NOT NULL,
    remind_status   INTEGER NULL,
    reminded_at     TEXT NULL,
    ended_at        TEXT NULL,
    note            TEXT NOT NULL DEFAULT '',
    CHECK (status IN (0, 1, 2)),
    CHECK (remind_status IS NULL OR remind_status IN (0, 1, 2))
);
```

枚举：

- `status = 0`：进行中
- `status = 1`：已完成
- `status = 2`：已取消
- `remind_status = 0`：无提醒
- `remind_status = 1`：未提醒
- `remind_status = 2`：已提醒
- `remind_status = NULL`：仅用于无法从旧历史 CSV 恢复提醒状态的迁移数据

时间统一保存为固定本地时间文本：`yyyy-MM-dd HH:mm:ss`。原因：原 AHK 使用本地墙钟时间，固定 ISO 格式既能保持旧语义，又可直接进行 SQLite 字符串排序与范围比较。

### 4.4 items 索引

至少创建：

```sql
CREATE INDEX IF NOT EXISTS idx_items_active_plan
ON items(status, planned_at, created_seq);

CREATE INDEX IF NOT EXISTS idx_items_history_end
ON items(status, ended_at DESC, created_seq DESC);

CREATE INDEX IF NOT EXISTS idx_items_reminder
ON items(status, remind_status, remind_at);

CREATE INDEX IF NOT EXISTS idx_items_created_at
ON items(created_at);

CREATE INDEX IF NOT EXISTS idx_items_planned_at
ON items(planned_at);

CREATE INDEX IF NOT EXISTS idx_items_remind_at
ON items(remind_at);

CREATE INDEX IF NOT EXISTS idx_items_reminded_at
ON items(reminded_at);

CREATE INDEX IF NOT EXISTS idx_items_ended_at
ON items(ended_at);
```

内容和备注第一版不建立普通 B-Tree 索引用于 `%关键字%` 查询，因为前导通配符无法有效使用该索引。先使用参数化 `LIKE`；只有实际数据规模证明需要时再评估 FTS/trigram，不要为了“可能更快”增加第一版复杂度。

### 4.5 settings 表

```sql
CREATE TABLE IF NOT EXISTS app_settings (
    key     TEXT PRIMARY KEY,
    value   TEXT NOT NULL
);
```

迁移并保存：

- `hotkey`
- `shortcut_add`
- `shortcut_history`
- `shortcut_settings`
- `shortcut_refresh`
- `shortcut_detail`
- `shortcut_edit`
- `shortcut_complete`
- `shortcut_cancel`
- `default_remind_minutes`
- `auto_start`
- `work_start_popup_enabled`
- `work_end_popup_enabled`
- `work_start_time`
- `work_end_time`

新功能推荐默认值：

- `work_start_popup_enabled = 0`
- `work_end_popup_enabled = 0`
- `work_start_time = 09:00`
- `work_end_time = 18:00`

默认关闭新弹窗功能，保证升级后不会突然改变用户行为；用户显式启用后生效。

### 4.6 工作日特殊设置表

```sql
CREATE TABLE IF NOT EXISTS work_calendar_exceptions (
    date        TEXT PRIMARY KEY,             -- yyyy-MM-dd
    day_type    INTEGER NOT NULL,             -- 1=工作日, 0=休息日
    name        TEXT NOT NULL DEFAULT '',     -- 如 国庆节、春节调休
    note        TEXT NOT NULL DEFAULT '',
    source      TEXT NOT NULL DEFAULT 'manual',
    updated_at  TEXT NOT NULL,
    CHECK (day_type IN (0, 1))
);
```

### 4.7 工作日弹窗去重表

用于解决应用重启、系统时间变化、休眠/解锁导致的重复触发：

```sql
CREATE TABLE IF NOT EXISTS scheduled_popup_log (
    trigger_date  TEXT NOT NULL,              -- yyyy-MM-dd
    trigger_kind  INTEGER NOT NULL,           -- 1=上班, 2=下班
    shown_at      TEXT NOT NULL,
    PRIMARY KEY (trigger_date, trigger_kind),
    CHECK (trigger_kind IN (1, 2))
);
```

仅在弹窗真正进入显示流程后写入日志，保证同一工作日同一时段最多弹一次。

### 4.8 数据库版本

增加：

```sql
CREATE TABLE IF NOT EXISTS schema_info (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
```

至少保存 `schema_version` 和 `legacy_migration_completed`。以后数据库结构变化必须通过顺序 migration 升级，不能删库重建。

---

## 5. 全字段查询功能

### 5.1 原则

“所有列都要作为查询条件”解释为：**所有业务字段均可参与筛选**。内部 SQLite `rowid`、迁移标记等实现字段不属于业务查询条件。

支持字段：

| 字段 | 查询方式 |
|---|---|
| 事项ID `id` | 精确或包含匹配，至少支持精确匹配 |
| 事项内容 `content` | 包含式模糊搜索 |
| 计划时间 `planned_at` | 时间范围 |
| 提醒时间 `remind_at` | 时间范围 |
| 创建时间 `created_at` | 时间范围 |
| 创建序号 `created_seq` | 最小值/最大值范围 |
| 事项状态 `status` | 下拉多选或单选：进行中/已完成/已取消 |
| 提醒状态 `remind_status` | 全部/无提醒/未提醒/已提醒/未知 |
| 响铃时间 `reminded_at` | 时间范围 |
| 结束时间 `ended_at` | 时间范围 |
| 备注 `note` | 包含式模糊搜索 |

主窗口固定业务范围为 `status=进行中`；历史窗口固定业务范围为 `status IN (已完成, 已取消)`，但历史窗口仍应允许按结束状态进一步筛选。

### 5.2 模糊搜索

事项内容和备注必须是“包含”搜索：

```sql
content LIKE @content ESCAPE '\'
note LIKE @note ESCAPE '\'
```

参数值格式：`%用户关键字%`。

必须转义用户输入中的 `%`、`_`、`\`，避免其意外成为通配符；所有参数使用 `SqliteParameter`。

- 内容条件和备注条件同时填写时，默认使用 AND。
- 空字符串表示“不使用该条件”。
- 不做自动分词，不偷偷改成前缀查询。

### 5.3 查询 UI

主窗口与历史窗口均增加可折叠“查询条件”区域。为避免大量控件常驻占用界面空间，可设计为：

- 默认折叠，只显示“查询条件”按钮、当前条件摘要、查询、重置。
- 展开后显示全部业务条件。
- 时间字段使用统一 `TimeRangeFilterControl`。
- “查询”执行数据库查询；“重置”清空条件并恢复默认列表。
- 查询条件变更不会每敲一个字就自动访问数据库；由用户点击“查询”或按 Enter 后执行，减少 I/O。
- 执行较慢查询时不能冻结 UI；允许在用户发起查询时用短生命周期后台任务执行，并支持取消上一条尚未结束的查询。

### 5.4 动态 SQL 规则

只对**字段是否有值**进行动态拼装；列名和 SQL 结构来自代码白名单，不由用户提供。

示意：

```sql
SELECT ...
FROM items
WHERE status = @activeStatus
  AND (@id IS NULL OR id = @id)
  AND (@content IS NULL OR content LIKE @content ESCAPE '\')
  AND (@plannedFrom IS NULL OR planned_at >= @plannedFrom)
  AND (@plannedTo IS NULL OR planned_at < @plannedTo)
  ...
ORDER BY
  CASE WHEN planned_at IS NULL OR planned_at = '' THEN 1 ELSE 0 END,
  planned_at,
  created_seq;
```

实际实现建议只追加存在的条件，而不是大量 `@x IS NULL OR`，有利于 SQLite 使用索引。

---

## 6. 时间范围查询

### 6.1 适用字段

以下字段均必须支持时间范围：

- 计划时间
- 提醒时间
- 创建时间
- 响铃时间
- 结束时间

### 6.2 预定义范围

每个时间条件均提供：

- 本周
- 上周
- 本月
- 上月
- 本年
- 去年
- 自定义

周的定义固定为：**周一 00:00:00 到下周一 00:00:00**，不依赖 Windows 区域设置的“每周第一天”。

数据库查询统一采用：

- 起点包含：`>= from`
- 终点不包含：`< toExclusive`

不要生成“23:59:59.999”式结束时间。

### 6.3 预设计算规则

假设当前本地日期为 2026-08-27（周四），则：

| 预设 | from（含） | to（不含） |
|---|---|---|
| 本周 | 2026-08-24 00:00:00 | 2026-08-31 00:00:00 |
| 上周 | 2026-08-17 00:00:00 | 2026-08-24 00:00:00 |
| 本月 | 2026-08-01 00:00:00 | 2026-09-01 00:00:00 |
| 上月 | 2026-07-01 00:00:00 | 2026-08-01 00:00:00 |
| 本年 | 2026-01-01 00:00:00 | 2027-01-01 00:00:00 |
| 去年 | 2025-01-01 00:00:00 | 2026-01-01 00:00:00 |

### 6.4 自定义范围

- “自定义”允许只填开始、只填结束或两者都填。
- 建议使用带 CheckBox 的 DateTimePicker，格式 `yyyy-MM-dd HH:mm:ss`。
- from 和 to 都填写时必须校验 `from < to`。
- 空时间字段默认不匹配任何已指定时间范围；只有不设置该时间条件时才包含空时间事项。
- 如未来需要“时间为空”查询，可增加独立的“为空”选项，但不应把它和时间范围混在一起。

---

## 7. 历史记录分页

### 7.1 分页规则

- 默认 page size：200。
- 可选 page size：100 / 200 / 500。
- 查询条件改变后回到第 1 页。
- 查询前先执行相同 WHERE 条件的 `COUNT(*)` 得到总记录数。
- 数据查询使用 `LIMIT @pageSize OFFSET @offset`。
- 历史排序固定为 `ended_at DESC, created_seq DESC`，与旧版本一致。
- 切页时只替换当前页 DataGridView 数据，不缓存所有历史记录。

### 7.2 查询与分页一致性

分页与筛选必须共用同一个 `QueryCriteria`，COUNT 和 SELECT 使用完全一致的 WHERE 逻辑。任何时间预设、内容模糊、备注模糊、结束状态条件都必须同时作用于总数和分页数据。

---

## 8. 工作日管理

### 8.1 规则模型

基础规则：

- 周一至周五：工作日。
- 周六、周日：休息日。

特殊日期规则优先级最高：

1. 若 `work_calendar_exceptions` 中存在该日期，直接采用特殊设置。
2. 否则按普通星期规则判断。

因此可以表达：

- 周一法定节假日 → 特殊设置为“休息日”。
- 周六调休上班 → 特殊设置为“工作日”。

核心接口：

```csharp
bool IsWorkday(DateOnly date)
```

### 8.2 工作日管理窗口

新增“工作日管理”入口，建议位于设置窗口或托盘/设置子菜单。

窗口至少提供：

- 年份选择。
- 特殊日期 DataGridView：日期、类型、名称、备注、来源。
- 新增。
- 编辑。
- 删除。
- 导入。
- 导出。
- 可选：显示“按基础规则计算”的星期信息，便于核对。

### 8.3 手工新增/编辑

字段：

- 日期（必填，yyyy-MM-dd）
- 类型（工作日 / 休息日）
- 名称（可空）
- 备注（可空）

同一天只能有一条特殊规则。编辑同日记录应 UPDATE，不新增重复记录。

### 8.4 导入/导出特殊日期

鉴于需求中需要节假日和调休日特殊设置，新版本必须同时提供**导入和导出**，便于维护和备份。

CSV 建议 UTF-8 with BOM，列固定为：

```text
日期,类型,名称,备注
2026-01-01,休息日,元旦,
2026-02-14,工作日,春节调休,周六上班
```

导入规则：

- 日期必须为 `yyyy-MM-dd`。
- 类型只接受“工作日”“休息日”（可额外兼容 `work/rest`，但导出统一中文）。
- 导入前先解析和验证全部记录；有格式错误时展示行号和原因。
- 验证通过后在一个 transaction 中批量 upsert。
- 同日期冲突时以导入文件为准覆盖现有特殊设置。
- 导入成功后立即重新计算下一次工作日上/下班弹窗调度。

导出规则：

- 可导出当前年份或全部特殊日期。
- 按日期升序。
- 不导出“普通周一至周五/周末”的基础规则，只导出异常/特殊日期。

---

## 9. 工作日上班/下班自动弹窗

### 9.1 设置项

在设置中新增“工作日提醒”区域：

- [ ] 上班时间自动弹出全部进行中事项
- 上班时间：`HH:mm`，默认 `09:00`
- [ ] 下班时间自动弹出全部进行中事项
- 下班时间：`HH:mm`，默认 `18:00`
- “工作日管理...”按钮

要求：

- 上班/下班功能可分别开关。
- 时间必须是合法 24 小时时间。
- 设置保存后立即重新计算调度，不重启。
- 非工作日不触发。

### 9.2 弹窗内容

分别使用：

- `toto - 上班事项提醒`
- `toto - 下班事项提醒`

弹窗必须展示**全部进行中事项**，排序与主列表一致。建议列保持：

- 事项内容
- 计划时间
- 提醒时间
- 提醒状态
- 创建时间
- 备注

行为：

- 置顶。
- 不自动关闭。
- 关闭窗口不改变事项状态。
- 允许双击查看详情。
- 建议保留“完成选中”按钮，与原提醒窗口体验一致。
- 可以播放一次系统提示音并闪烁窗口。
- 若当前没有进行中事项，仍显示窗口，并明确显示“当前无进行中事项”，以确认定时任务正常执行。

**重要：**工作日上/下班弹窗是“汇总提醒”，不能修改事项自己的 `remind_status`、`reminded_at`，也不能占用单事项提醒队列。

### 9.3 去重与补发

每个工作日每类弹窗最多显示一次，使用 `scheduled_popup_log` 去重。

系统休眠、锁屏或应用在触发点没有机会执行时：

- 在恢复/解锁/重新启动后重新计算当天应触发的工作日弹窗。
- 若当天同类弹窗尚未记录且当前时间已经超过触发时间，允许补发。
- 若恢复时已经跨过同一天的下一工作时段，只补发**最近一个尚未执行的时段**，避免 19:00 同时连续弹出“上班”和“下班”两个旧窗口。
  - 例：09:00 上班、18:00 下班；12:00 解锁 → 可补发上班提醒。
  - 例：19:00 解锁 → 只补发下班提醒，不再补发早上的上班提醒。
- 跨日后不补发前一天的工作日汇总弹窗。

这套补发规则仅针对工作日汇总提醒；单事项到期提醒仍按原规则在解锁后补发所有尚未标记为已提醒的到期事项。

---

## 10. 统一调度器设计

### 10.1 一个调度器处理两类事件

`ReminderScheduler` 同时计算：

1. 下一条“单事项提醒”。
2. 下一次“工作日上班弹窗”。
3. 下一次“工作日下班弹窗”。

取三者中最早的时间作为下一触发点，只保留一个 one-shot `System.Threading.Timer`（或等价低频单次 Timer）。

### 10.2 禁止高频轮询

禁止：

- 每秒扫描数据库。
- 每分钟全表扫描提醒。
- 为每条事项创建一个 Timer。

允许：

- DB 数据变更时重新计算一次下一事件。
- Timer 到期后处理到期事件并计算下一个。
- 系统时间变化、系统恢复、解锁时重新计算。
- 延续旧逻辑：一次 Timer 最长可只等待 6 小时，到期后重新校准，避免系统时间变化导致长期漂移。

### 10.3 单事项提醒查询

```sql
SELECT id, remind_at
FROM items
WHERE status = 0
  AND remind_status = 1
  AND remind_at IS NOT NULL
ORDER BY remind_at
LIMIT 1;
```

到期处理必须在 transaction 中：

1. 找到所有 `remind_at <= now` 且仍为未提醒的进行中事项。
2. 更新 `remind_status = 2`、`reminded_at = now`。
3. COMMIT。
4. 再把这些事项加入 UI 提醒队列并弹窗。

### 10.4 系统事件

至少处理：

- 系统时间变化。
- 休眠恢复。
- 锁屏。
- 解锁。

可用 WinForms/Windows 系统事件或消息窗口实现，但必须在退出时正确注销事件，避免静态事件引用导致进程无法释放。

锁屏时：

- 暂停用户可见提醒触发。
- 不把单事项到期提醒提前标记成“已提醒”。

解锁/恢复时：

- 处理到期的单事项提醒。
- 处理当天可补发的最近一个工作日汇总提醒。
- 刷新主列表状态。
- 重新设置下一次 Timer。

---

## 11. 设置窗口改造

建议将旧设置窗口升级为 TabControl，以避免窗口过长：

### 11.1 “常规”页

- 全局唤醒快捷键。
- 是否包含 Win。
- 默认提前提醒分钟。
- 登录 Windows 自动启动。

### 11.2 “快捷键”页

保留八个应用内快捷键配置及原校验规则。

### 11.3 “工作日提醒”页

- 上班弹窗开关。
- 上班时间。
- 下班弹窗开关。
- 下班时间。
- 工作日管理按钮。

设置保存采用“先验证全部 → 尝试注册新快捷键/更新自启动 → transaction 保存 DB → 重新调度”的顺序。任何一步失败时不要留下半更新状态。

---

## 12. CSV/INI 首次迁移

### 12.1 迁移触发

如果 `toto.db` 不存在，或 `legacy_migration_completed != 1`，执行迁移。

### 12.2 迁移步骤

1. 创建 `legacy_backup/yyyyMMdd_HHmmss/`。
2. 将存在的 `config.ini`、`toto_ing.csv`、`toto_end.csv` 原样复制到备份目录。
3. 创建/升级 SQLite schema。
4. 读取旧 `config.ini`，迁移旧设置。
5. 按旧 AHK CSV 兼容规则读取进行中 CSV，包括旧格式中的“提前提醒分钟数”兼容列。
6. 读取历史 CSV。
7. 对重复 ID 按旧规则处理：历史记录优先，进行中重复记录忽略。
8. 在一个 transaction 中批量插入 SQLite。
9. `created_seq` 必须原值保留；后续新增使用 `MAX(created_seq)+1`。
10. 写入 `legacy_migration_completed=1`。
11. COMMIT。
12. 迁移结束后重新查询 SQLite 数量并核对。

### 12.3 历史提醒状态特殊处理

旧历史 CSV 没有“提醒状态/响铃时间”字段，因此迁移历史事项时：

- `remind_status = NULL`
- `reminded_at = NULL`

不要猜测“历史事项一定已提醒”，也不要根据结束时间和提醒时间推断。

### 12.4 失败处理

- 任意解析/写库失败：ROLLBACK。
- 原 CSV/INI 不修改、不删除。
- 显示明确错误信息并记录 migration log。
- 允许用户修复数据后再次启动重试。
- 格式异常记录的处理至少要达到旧版能力：指出跳过/失败条数。更推荐导出带行号和原因的错误报告，而不是静默忽略。

---

## 13. Repository 行为要求

### 13.1 新增

单 transaction 插入一条 `status=0` 事项。

### 13.2 编辑

只 UPDATE 当前事项需要修改的字段。提醒时间变化时按旧逻辑更新 `remind_status/reminded_at`。

### 13.3 完成/取消

不要物理移动记录：

```sql
UPDATE items
SET status = @status,
    ended_at = @endedAt,
    note = @note
WHERE id = @id AND status = 0;
```

受影响行必须为 1；否则提示“事项不存在或已被处理”。

### 13.4 查询进行中

固定排序：

```sql
ORDER BY
  CASE WHEN planned_at IS NULL OR planned_at = '' THEN 1 ELSE 0 END,
  planned_at ASC,
  created_seq ASC
```

### 13.5 查询历史

```sql
ORDER BY ended_at DESC, created_seq DESC
LIMIT @pageSize OFFSET @offset
```

---

## 14. 低资源占用约束

以下约束属于架构验收项：

- 不引入 EF Core。
- 不引入 ASP.NET Core Host。
- 不引入 WebView2/Electron。
- 不使用持续 1 秒/数秒级轮询。
- 不为每个事项创建独立 Timer。
- 主窗口隐藏时不执行每分钟 UI 重绘；紧急颜色刷新 Timer 仅在主窗口可见时运行，隐藏时停止。
- 历史只保留当前页数据对象。
- 查询仅由用户操作触发；不做“输入每个字符都搜索”。
- 数据库写入使用行级 INSERT/UPDATE，不做全量重写。
- 工作日表仅保存特殊日期，不展开存储所有普通工作日。
- 可复用一个 SQLite 连接或采用短连接 + provider pooling，但不要为每次表格绘制访问数据库。
- UI 显示数据应先查询成当前结果集，再绑定 DataGridView；`CellFormatting` 不能执行 SQL。

资源观察标准：

- 托盘空闲时 CPU 应接近 0；若持续可观察到 CPU 占用，应视为需要排查的缺陷。
- 空闲时不应持续产生 SQLite 查询或磁盘写入。
- 内存应随“当前页面/当前窗口数据量”而不是“全部历史记录数”线性增长。

---

## 15. UI 与 DPI 验收

必须在 Windows 11 测试至少：100%、125%、150%、175%、200% 缩放。

验收：

- DataGridView 横线/竖线不随滚动发生错位。
- 不存在旧版 `rowBottom - 1`、`lineY`、header rect 等绘制补偿代码。
- 列标题、行高和选中状态没有裁切。
- 窗口可调整大小时，最后一列应合理填充剩余空间。
- 备注可以显示截断预览；详情窗口显示完整备注。
- 紧急颜色只影响文字/Style，不影响网格线位置。

---

## 16. 错误处理与日志

该工具不需要重型 logging 框架。建议提供简单滚动文本日志：

`%USERPROFILE%\.toto\logs\toto-yyyyMM.log`

记录：

- 启动/退出。
- 数据库 schema 升级。
- CSV/INI 迁移摘要和错误。
- 快捷键注册失败。
- ReminderScheduler 异常。
- 工作日导入错误。
- 未处理异常。

不要记录完整事项内容/备注到常规日志，避免私人数据泄露；业务错误只记录事项 ID 和错误上下文。

---

## 17. 核心测试用例

### 17.1 原功能回归

- [ ] 第二实例启动后第一实例显示主窗口，新实例退出。
- [ ] Ctrl+Alt+Space 默认唤醒。
- [ ] 八个默认应用内快捷键全部有效。
- [ ] 自定义快捷键保存后立即生效，重复快捷键拒绝保存。
- [ ] 主窗口关闭/Esc 只隐藏。
- [ ] 托盘所有菜单正常。
- [ ] 开机启动开关正常。
- [ ] 快速新增所有时间格式均与旧程序一致。
- [ ] 编辑时允许事项内容含 `@`。
- [ ] 修改/清空提醒时间时提醒状态与旧程序一致。
- [ ] 完成/取消备注默认带入现有备注。
- [ ] 主列表排序与颜色规则一致。
- [ ] 历史排序一致。
- [ ] 单事项提醒先落库再弹窗。
- [ ] 多条同时到期合并显示。
- [ ] 锁屏不标记已提醒，解锁后补发。
- [ ] 休眠恢复和系统时间变化后重新调度。

### 17.2 查询

- [ ] 每个业务字段都能单独作为条件。
- [ ] 多条件默认 AND。
- [ ] 事项内容支持包含式模糊搜索。
- [ ] 备注支持包含式模糊搜索。
- [ ] `%`、`_`、`\` 作为普通字符搜索时不会被误当通配符。
- [ ] 所有 SQL 均参数化。
- [ ] 清空条件恢复默认列表。
- [ ] 历史 COUNT 与分页列表使用相同条件。

### 17.3 时间范围

- [ ] 本周从周一开始。
- [ ] 上周、本月、上月、本年、去年边界正确。
- [ ] 月末、年末、闰年正确。
- [ ] 自定义 only-from、only-to、from+to 均可用。
- [ ] `from >= to` 被拒绝。
- [ ] 使用 `[from, toExclusive)`，边界秒不会重复或遗漏。

### 17.4 工作日

- [ ] 普通周一至周五为工作日。
- [ ] 普通周六周日为休息日。
- [ ] 周一可被特殊设置为休息日。
- [ ] 周六可被特殊设置为工作日。
- [ ] 同日期特殊记录唯一。
- [ ] 导入合法 CSV 成功并覆盖同日期规则。
- [ ] 导入非法日期/类型给出明确行号。
- [ ] 导出内容可再次导入并得到相同规则。

### 17.5 上/下班汇总弹窗

- [ ] 默认关闭，升级后不产生新增弹窗。
- [ ] 启用上班提醒后仅工作日触发。
- [ ] 启用下班提醒后仅工作日触发。
- [ ] 弹窗展示所有进行中事项并按主列表规则排序。
- [ ] 0 条事项也显示“当前无进行中事项”。
- [ ] 关闭汇总弹窗不改变事项状态/提醒状态。
- [ ] 同一工作日同一时段重启应用不会重复弹。
- [ ] 12:00 解锁可补发 09:00 上班提醒。
- [ ] 19:00 解锁只补发 18:00 下班提醒，不同时补发 09:00。
- [ ] 非工作日不会因为恢复/解锁而补发工作日汇总提醒。

### 17.6 数据量与 UI

建议准备测试库：

- 进行中 1,000 条。
- 历史 100,000 条。
- 特殊工作日 1,000 条。

验证：

- [ ] 打开历史窗口只加载 200 条（默认页大小）。
- [ ] 切页不会随着历史总量持续增长内存。
- [ ] 时间索引查询响应稳定。
- [ ] 模糊搜索执行期间 UI 不冻结。
- [ ] 反复滚动 DataGridView 不出现横线错位。
- [ ] 100%~200% DPI 均无自绘错位。
- [ ] 托盘空闲时无周期性全库扫描。

---

## 18. 建议实施顺序

### Phase 1：骨架与数据库

1. 建立 `net10.0-windows` WinForms 项目。
2. 实现 ApplicationContext、托盘、单实例。
3. 建立 SQLite schema、repository、schema migration。
4. 实现旧 CSV/INI 备份和一次性迁移。
5. 使用测试程序核对迁移后的记录数量和字段。

**Phase 1 完成前不要删除/覆盖旧 CSV。**

### Phase 2：原功能等价迁移

1. 主 DataGridView。
2. 新增/编辑。
3. 详情。
4. 完成/取消。
5. 历史窗口。
6. 设置与快捷键。
7. 开机启动。
8. 单事项提醒、锁屏/恢复补发。
9. 紧急时间文字颜色。

完成后跑“原功能回归”全部用例。

### Phase 3：查询与分页

1. `QueryCriteria`。
2. 参数化动态 WHERE builder。
3. 五个时间字段的统一 TimeRangeFilterControl。
4. 六个时间预设。
5. 内容/备注模糊搜索。
6. 历史分页与 COUNT。
7. 查询取消/防 UI 卡死。

### Phase 4：工作日管理

1. 特殊日期表与 Repository。
2. `WorkCalendarService.IsWorkday()`。
3. 工作日管理 UI。
4. CSV 导入/导出。
5. 单元测试覆盖周末和特殊覆盖规则。

### Phase 5：工作日汇总提醒

1. 新设置项。
2. `scheduled_popup_log`。
3. Scheduler 合并工作日触发点。
4. 上班/下班汇总窗口。
5. 休眠/锁屏/重启补发与去重。

### Phase 6：稳定性与发布

1. 100%~200% DPI 测试。
2. 100,000 条历史压力测试。
3. 空闲 CPU/I/O 检查。
4. 未处理异常日志。
5. 发布 Windows x64 framework-dependent 版本；如需要免安装 Runtime，可另提供 self-contained 包，但不要把 Electron/Web runtime 引入项目。

---

## 19. Definition of Done / 最终验收标准

只有以下全部满足，才能认为改造完成：

1. 代码已经完全不依赖 AutoHotkey 运行时。
2. 使用 C# .NET 10 WinForms。
3. 使用 SQLite，旧 CSV 仅作为迁移/备份数据，不再作为日常主存储。
4. 原功能基线全部通过。
5. 主列表和历史列表均为 DataGridView。
6. 任何地方都没有自绘网格线代码。
7. 历史默认分页 200 条。
8. 所有业务字段可作为查询条件。
9. 事项内容、备注均支持包含式模糊查询。
10. 所有时间业务字段均支持范围查询。
11. 时间范围包含：本周、上周、本月、上月、本年、去年、自定义。
12. 工作日判断为“周一至周五基础规则 + 特殊日期覆盖”。
13. 支持手工维护、导入、导出特殊工作日/休息日。
14. 支持工作日上班/下班两个独立自动弹窗开关和时间设置。
15. 工作日汇总弹窗展示全部进行中事项，且不修改单事项提醒状态。
16. 工作日弹窗有持久化去重，并正确处理休眠/锁屏/重启补发。
17. 单事项提醒仍正确处理锁屏/解锁、休眠恢复、系统时间变化。
18. SQLite 所有用户条件查询均参数化。
19. 托盘空闲时无高频轮询和全库扫描。
20. Windows 11 100%~200% DPI 下表格网格线稳定，无错位。
21. 迁移失败不会破坏旧数据，迁移成功前有完整备份。
22. 至少完成本规格第 17 节测试清单，并保留测试结果。

---

## 20. 可直接给编码 AI 的执行提示

请读取现有 `toto.ahk`，把它视为行为基线，并严格按照本规格将程序迁移为 C# .NET 10 WinForms + SQLite。不要逐行机械翻译 AHK，而要按本规格的 Domain/Data/Services/UI 结构重构；但所有未被本规格明确修改的用户可见行为必须与旧版一致。

优先保证：数据不丢失、提醒不重复/不漏发、快捷键行为一致、历史分页、全字段查询、工作日规则和低资源占用。禁止通过 DataGridView 自绘、GDI/GDI+ 或 P/Invoke 绘制网格线。数据库使用 Microsoft.Data.Sqlite 直接编写参数化 SQL，不要使用 EF Core。

每完成一个 Phase，先编译并运行对应回归测试，再进入下一个 Phase。不要以“代码已生成”代替验收；最终必须通过 Definition of Done 中全部条目。
