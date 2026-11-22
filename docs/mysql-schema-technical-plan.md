# MySQL Schema 工具：需求分析与技术实施方案

> 状态：供产品/技术审核；本文件只定义方案，不代表功能已经实现。  
> 交互基准：`MySQL_Schema_Connections_Fixed.html`、`MySQL_Schema_Backup_v4.html`、`MySQL_Schema_Compare_Row_Detail_v3.html`。  
> 原则：保留 Juju 现有桌面、工具模块和本地文件存储架构；HTML Demo 是交互参考，不作为最终运行界面或业务数据源。

## 1. 方案摘要

在 Juju 主界面新增一个 **MySQL Schema** 工具入口。工具内部包含连接管理、结构备份、结构对比三个页面，运行于现有 WPF 宿主中；业务数据、连接配置及历史结构文档均保存在当前应用数据根目录下的本地文件中。

新增真实 MySQL 访问适配器，使用它进行连接测试、数据库/表/视图发现、结构元数据读取及原始 DDL 获取。备份文档是独立、不可变的本地结构快照；对比服务只比较结构模型，不执行数据库写操作。生成的迁移 SQL 仅供审核、复制和导出，不在本阶段执行。

推荐新增 `Juju.Tools.MySqlSchema` 类库和配套测试项目，由 `Juju.App` 负责 WPF 界面和工具模块注册。连接凭据加密存储；备份不包含业务数据或密码。具体 MySQL 版本支持范围、TLS 选项、DDL 生成覆盖范围及导出格式列入待确认问题。

## 2. 当前项目分析

### 2.1 技术与项目结构

| 项目/目录 | 当前职责 | 对新工具的意义 |
|---|---|---|
| `src/Juju.App` | .NET 10 WPF 宿主、Launcher、设置窗口、工具窗口、托盘及组合根 | 实现 MySQL Schema 导航页、连接/备份/对比 UI 和 `IToolModule` 适配器 |
| `src/Juju.Core` | 工具稳定 ID、宿主设置、数据根定位、原子文件写入、共享契约 | 提供本地路径、单键入口及原子存储能力；不应依赖 WPF 或 MySQL 客户端 |
| `src/Juju.Tools.Json` | JSON 文档、元数据、修订校验、文件观察及 JSON 工具设置 | 文件存储的并发、安全和原子更新模式可参考；其 JSON 文档服务不直接适用于结构快照领域 |
| `src/Juju.Tools.Toto` | Toto 领域模型、CSV/JSON 仓储及后台提醒 | 展示独立工具业务层和本地仓储组织方式；本工具不需要后台服务 |
| `src/Juju.Platform.Windows` | Windows 托盘、热键、文件夹选择、工作区等系统适配 | 新工具的凭据保护或系统安全能力可以置于平台边界；数据库协议本身应留在工具层 |
| `src/Juju.Launcher` / `src/Juju.Updater` / `src/Juju.Update` | 稳定入口、版本切换、应用包更新 | 新工具随 App 一起发布，无需改变既有版本更新机制 |
| `tests/*` | xUnit 工具测试、App 路由/架构测试、更新测试 | 新增隔离的 MySQL Schema 测试项目；数据库集成测试与无数据库单元测试分开 |
| `editor/` | 本地 Monaco 编辑器，用于 JSON 工具 | 不是 MySQL Schema 的运行宿主；不建议把原型 HTML 放入 Editor/WebView2 |

主要技术事实：应用目标为 `net10.0-windows` 和 WPF；JSON/Toto 是独立的文件型工具项目；工具注册通过 `IToolModule`、`ToolRegistry`、`ToolManager` 完成。项目没有找到 MySQL Connector、ADO.NET MySQL Provider、Dapper 或数据库连接实现。`StorageCapability.Database` 枚举值虽已存在，但不代表当前已有数据库基础设施。

### 2.2 工具生命周期与 UI 复用

- 新工具应注册唯一稳定 `ToolId`，提供 Launcher 文案、图标、默认按键、设置页及打开窗口入口。
- 当前 `IToolModule` 包含检查就绪、打开、启动和停止接口；`ToolManager` 只启动声明为 `BackgroundCapability.Required` 的后台模块。MySQL Schema 初期不需要连接常驻或后台轮询，应为非后台模块，按一次用户操作按需开关连接。
- 设置窗口把模块提供的 `ISettingsSection` 汇总显示。MySQL Schema 的主要业务 UI 更适合一个独立主窗口中的三项内部导航，而不是把三套 HTML 页直接嵌入应用。
- 现有 App 的 DataGrid、卡片、工具标题栏、目录选择和状态反馈可作为 WPF 样式参考。Demo 布局需用 WPF 重建，HTML、浏览器 `localStorage` 和模拟 JS 不进入最终产品。

### 2.3 本地数据根与存储能力

- `ApplicationPaths` 以安装目录中的 `data-location.json` 指向实际数据根；工具目录通过 `ToolDataDirectory("<id>")` 固定为 `<数据根>/tools/<id>`。
- 首次运行由 `DataRootService` 初始化 `juju.json` 标记和目录；应用、JSON、Toto 分别创建自己的默认设置/业务文件。
- `IAtomicFileWriter` 先写同目录临时文件、落盘，再原子替换，适用于连接文件和单份结构快照。
- JSON 工具还有一个 `SemaphoreSlim` 保护多文件更新、元数据索引、修订冲突检测和 watcher 的完整示例。
- Toto 仓储说明了使用 JSON envelope 保存设置/运行状态、用 CSV 保存条目的本地文件方案。
- 更换应用数据根目录不会搬迁旧文件；本工具所有数据必须从启动时当前根目录重新读取和初始化。

### 2.4 当前与新工具相关的已有能力/缺口

**可复用：** WPF 应用外壳和目录/导航样式、稳定工具 ID/Launcher、模块路由、设置服务、数据根指针、原子写入、用户文件夹隔离、异常反馈、xUnit 测试约定。

**尚未实现：** MySQL 连接驱动与连接池、连接配置 UI/存储、凭据加密、连接/超时/权限处理、Information Schema 读取、`SHOW CREATE` 获取、结构模型、快照仓储、环境/版本备份树、唯一性并发保护、结构级差异算法、行级详情、结构迁移 SQL 生成。

**确认到的依赖差距：** 当前 `Juju.App.csproj` 的包依赖只有 WebView2、Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.Logging 等；不存在 MySQL 客户端 NuGet 包。需要选定并锁定实际数据库 Connector，并通过兼容性及许可证检查后接入。

## 3. 三个 HTML Demo 的需求及模拟行为

### 3.1 连接管理 Demo

**界面与流程：** 表格展示名称、主机、端口、用户名、默认数据库、连接状态及编辑/删除/测试按钮；工具栏有新增、导出、测试全部和搜索。新增/编辑弹窗含名称、主机、端口、用户名、可选密码、默认数据库；密码可显示/隐藏。删除有确认；编辑后状态重置为未测试；连接总数和筛选数量实时显示。

**原型模拟内容：** 数据存于浏览器 `localStorage`，含“本地开发/测试环境/预发布”三条样例。测试并未连接服务器：使用固定延迟后写入“模拟通过”，toast 明确说明“未建立数据库连接”。导出是显式确认后生成含明文密码的文本文件。三项侧边功能当前仅是占位。

**真实含义：** 表格状态应代表最近一次真实连接测试，不是永久连接状态；修改影响连接参数后要将该状态置为未测试。连接列表是唯一连接源，备份和实时对比不得临时创建私有连接。导出必须显式告知是否含密码。

### 3.2 结构备份 Demo

**界面与流程：** 左侧树按环境→版本→连接→数据库→对象显示；顶栏可新建环境、版本和展开/折叠；从环境、版本、连接、数据库节点均可发起备份，向导预填已知路径。环境和版本同级重名禁止。对象列表支持类型筛选、搜索、单选、多选、全选和清空，已备份对象不可选择；批量备份逐对象生成文档。可查看、导出、删除；删除父目录会提示级联删除数量；仅环境/版本可重命名。

**原型模拟内容：** 树和文档存在浏览器 `localStorage`。默认环境/版本是样例；连接读连接 Demo 的本地连接记录，数据库及对象从 `databases()`/`objects()` 固定数组“发现”；`schema()` 根据名称/版本拼出虚构字段、索引与 DDL，并且采集信息明确写着模拟。备份按钮只是把该虚构对象放入本地树。原型已经编码的关键规则：数据库下对象文档独立；表/视图是文档属性而非额外目录层；当前选定目标路径下按不区分大小写名称阻止重复/覆盖；删除后方可重备份。

**真实含义：** `databases()` 和 `objects()` 必须替换成从当前 MySQL 连接中真实枚举；结构文档必须来自真实服务端元数据与原始 DDL。目标路径中已经有对象文档时，在候选列表就排除；发现失败必须显示失败，不能显示空数据库或所有对象都“可备份”。批量部分失败时成功对象留下，失败对象可在确认后重试。

### 3.3 结构对比 Demo

**界面与流程：** 顶端 A/B 两个来源摘要和单独配置，支持交换；来源可选历史快照或实时连接。历史来源选择环境/版本/连接/数据库/对象，实时来源选择连接/数据库/对象。对象类型不同会拒绝对比。左侧概览、数据库、字段、主键、索引、外键、检查约束、分区、选项、视图分类；支持“仅差异”。差异包含相同、缺失、修改；字段/索引/外键修改行双击打开逐属性对比。另有 A/B 元数据查看和 A→B/B→A DDL 查看、复制、导出入口。

**原型模拟内容：** `catalog`、`snapshots` 和 `build()` 全是 JS 固定对象；所谓实时来源被强行映射为固定“版本 3”结构，版本之间预设少量差异。属性相等使用 `JSON.stringify`；原始 DDL 和 SQL 变更文本都是手工拼接示例，DDL 弹窗明确提示“原型脚本不完整，不可直接执行”。

**真实含义：** 历史 A/B 使用不可变快照，实时 A/B 每次对比重新读取服务端；差异算法比较结构化字段，而不是比较 DDL 字符串。SQL 生成必须和原始 DDL 分开。Demo 中当前 `ddlText()` 只演示简单列增删，并没有完整处理字段属性、索引、外键、约束、分区、视图、安全属性等，因此不能作为可执行 SQL 方案直接移植。

## 4. 统一领域与数据关系

### 4.1 领域实体

| 实体 | 责任/身份 | 说明 |
|---|---|---|
| `ConnectionProfile` | 本地连接配置的稳定 `Guid` | 名称可修改；认证信息加密；测试状态为临时状态，不作为连接可用性的永久事实 |
| `Environment` | 用户建立的组织节点 ID | 例如生产、演示；名称同级唯一 |
| `SchemaVersion` | 环境内版本节点 ID | 用户的归档标签，不代表服务器 MySQL 版本；名称只需在所属环境内唯一 |
| `DatabaseIdentity` | 来源连接 ID + 数据库名/规范化键 | 同名数据库可在不同连接中存在 |
| `SchemaObjectIdentity` | 数据库中的对象名及类型 | 表/视图；同一备份目录下唯一约束按对象名，不以类型允许重名 |
| `SchemaSnapshot` | 单个对象的一份完整历史结构 | 独立 JSON 文件；含快照时刻、来源信息、结构模型、原始 DDL、版本/采集告警 |
| `SchemaSource` | 对比 A 或 B 的来源判别 | `Snapshot` 或 `LiveConnection`；Live 的结构只在对比期间读取，不自动变成历史备份 |

连接 ID、环境 ID、版本 ID 在改显示名称时保持不变。快照保存连接 ID、当时的显示名、主机/端口/用户名等非密钥来源摘要；不存密码。这样连接改名/修改/删除后，历史快照仍能独立展示和对比，且来源可追溯。

### 4.2 唯一性与目录

**逻辑 UI 树严格保持**：环境 → 版本 → 连接 → 数据库 → 对象文档；不增加表/视图类型目录。对象类型是快照属性。

Windows 文件名有非法字符、保留名称、大小写和长度约束，且环境/版本/连接可重命名。建议物理目录用稳定 ID，显示名保存在本地 JSON 节点元数据：

```text
<数据根>/tools/mysqlschema/
  connections.json
  backups/
    <environment-id>/environment.json
    <environment-id>/<version-id>/version.json
    <environment-id>/<version-id>/<connection-id>/connection.json
    <environment-id>/<version-id>/<connection-id>/<database-key>/database.json
    <environment-id>/<version-id>/<connection-id>/<database-key>/objects/<object-id>.json
```

上面是逻辑层级的稳定本地布局；UI 显示的是用户名称/数据库名/对象名，不暴露 GUID。`database-key` 可用稳定编码或数据库节点 ID；建议以 ID 避免路径注入和 Windows 文件名问题。删除连接配置只删 `connections.json` 中的记录，不能递归删除备份树。

备份唯一键为：

```text
(environmentId, versionId, connectionId, databaseIdentity, canonicalObjectName)
```

`objectType` 不加入唯一键。进入执行阶段仍需在仓储写入锁内再次检查并使用“目标不存在才创建”的原子操作，不能只依赖 UI 的预检查。对象名的大小写规范要结合服务端 `lower_case_table_names` 处理；此项在第 12 节待确认。

### 4.3 结构模型

所有读取、持久化、查看、差异计算共用一个版本化强类型 Schema 模型。建议快照外层包含：

- `schemaVersion`、snapshot ID、捕获时间 UTC、工具/采集器版本。
- `source`: 连接 profile ID、捕获时连接名称/主机/端口/用户名（不含密码）、数据库名。
- `server`: MySQL 产品名/版本、`lower_case_table_names`、已知能力标志。
- `object`: 名称、`Table`/`View` 类型、数据库名。
- 各类别结构属性：数据库、字段、主键、索引、外键、检查约束、分区、表选项、视图属性。
- `originalDdl`：捕获时服务器原样返回的 CREATE DDL。
- `captureWarnings` / `unavailableAttributes`：说明未能读取或当前版本不支持的属性，不能用空值伪装。

属性应有明确类型和排序：字段序号、索引/主键字段序号、分区序号均保留。SQL `NULL`、没有 DEFAULT、字面默认值、表达式默认值（如 `CURRENT_TIMESTAMP`）应能区分。字段、索引表达式、字符集/排序规则、生成列表达式、可见性、注释和服务端返回的相关属性均按 Demo/需求映射。`null` 不应混淆“未采集/不适用”。

每个对象一份完整快照 JSON；原始 DDL 作为原文存储而非日后重建。文件原子写入；一份快照失败不能让其他对象的文件损坏。已有目标文档拒绝覆盖。

## 5. 整体架构方案

### 5.1 建议项目边界

1. 新建 `src/Juju.Tools.MySqlSchema/Juju.Tools.MySqlSchema.csproj`，平台无关 `net10.0`，引用 `Juju.Core`。内部按现有工具风格组织 `Domain`、`Application`、`Infrastructure`。
2. MySQL Connector 封装在该工具项目的 Infrastructure 中。方案候选为 `MySqlConnector` NuGet；在实施第一阶段验证 .NET 10 兼容性、MIT/依赖许可证、目标 MySQL 矩阵后固定版本。连接库不能泄漏到 App UI 或 Core。
3. `Juju.App` 新增 `MySqlSchemaToolModule`、注册扩展、独立 `MySqlSchemaWindow` 与 WPF 页面；工具模块提供一个 Launcher 卡片，窗口内以连接/备份/对比三个内页导航。
4. 新增 `tests/Juju.Tools.MySqlSchema.Tests`：大部分使用 fake `ISchemaReader` 和临时数据根测试；可选集成测试由环境变量启用，不依赖外网或默认数据库。
5. 更新 `Juju.sln`、`ToolId`、`ToolLaunchKeys`、`README` 项目结构/工具说明、发布 workflow 构建与架构测试的项目引用基线。

新模块描述可使用 `StorageCapability.File`（本地结构文档是持久化介质；外部 MySQL 是数据来源，不是本地存储类型），`BackgroundCapability.Unsupported`。本工具按用户操作短连接，不常驻连接池，不请求后台权限。

### 5.2 建议的应用层接口

工具层通过接口分隔 UI、业务和驱动实现，建议至少有：

- `IConnectionProfileRepository`: 列表、保存、删除、导入/导出；秘密以受保护形式读写。
- `IMySqlConnectionTester`: 按连接 profile 测试，返回明确结果/分类错误/耗时。
- `IMySqlSchemaReader`: 枚举可访问数据库、表/视图；捕获一个对象的结构模型与原始 DDL。
- `IBackupCatalogRepository`: 环境/版本节点 CRUD、路径树查询、列举快照、按唯一键检查、创建/删除/读取快照。
- `ISchemaComparisonService`: 对比两个 schema 模型，输出结构化分组/条目/属性级 delta。
- `IMigrationSqlGenerator`: 将受支持的 delta 转换成有方向标记的 SQL 草案及 warnings；不得执行。

业务服务只依赖这些抽象和本地 JSON 文件仓储，UI 依赖业务接口；Connector 的 `MySqlConnection`、ADO.NET 类型、SQL 结果解析只能出现在 Infrastructure。

### 5.3 本地文件目录建议

```text
<数据根>/tools/mysqlschema/
  connections.json                连接列表和密钥密文
  backups/                         环境/版本/连接/数据库/对象快照
  export/                          不作为长期自动缓存；用户选择保存位置
```

根目录启动时按现有 `DataRootService` 模式创建。写连接或备份文件使用 `IAtomicFileWriter`；创建/删除目录记录使用仓储级锁，避免 UI 重复点击造成唯一性竞态。快照文档不能被连接删除流程级联删除。

### 5.4 凭据安全

- 连接密码不写日志、错误消息、快照、备份 DDL、对比结果或默认导出。
- 本地保存推荐 Windows DPAPI CurrentUser 保护密码密文；如果采用 Credential Manager，需单独设计凭据目标名和删除行为。明文密码只在连接对象内存及建立连接时短暂使用。
- 导出连接时按 Demo 提示敏感内容，提供“包含密码”的明确确认/选项；如导出为明文，文件名/提示必须标示敏感，不能静默导出。待确认是否要支持仅导出不含秘密的分享文件/重新导入。
- 连接测试失败时展示用户可行动信息，但不展示密码；日志过滤连接串和秘密。

## 6. MySQL 元数据读取方案

### 6.1 连接

连接串由驱动按字段构造，不拼接用户输入 SQL。主机、端口、用户名必填；默认端口 3306；密码和默认数据库可选。设定连接与命令超时、取消令牌、SSL/TLS 选项；默认不让 UI 线程同步等待。

连接测试先建立连接并执行只读轻量查询（例如 `SELECT 1`），再读取服务端版本和必要能力。连接状态为 UI 临时值：编辑 host/port/user/password/database 后清成“未测试”；状态不能仅靠上次测试成功来保证当前可用。

### 6.2 枚举数据库/对象

- 根据已保存 profile 建立连接，实时从服务端枚举当前用户可见数据库，不能用默认数据库字段代替完整数据库列表。
- 默认数据库只用于预选，不得限制用户选择别的数据库。
- 从 `INFORMATION_SCHEMA.TABLES` 枚举表和视图；保留表类型信息；按目标版本和用户权限处理系统 schema。
- 列表请求失败、权限不足、连接失败与“数据库内确实没有对象”是不同结果状态。
- 连接应在每个测试/发现/捕获/对比操作完成后释放；对一次对象捕获尽量复用一个连接，整批备份不共享 UI 全局长期连接。

### 6.3 结构采集

`INFORMATION_SCHEMA` 用于结构化字段：

- `SCHEMATA`：字符集、默认排序规则。
- `TABLES`：表/视图类型、引擎、行格式、表级字符集/排序规则/注释、自增等可用表属性。
- `COLUMNS`：字段顺序、类型、可空、默认、额外属性、生成表达式、字符集/排序规则、注释、可见性等版本可用列。
- `STATISTICS`：索引名称、唯一性、顺序、表达式/字段、前缀长度、升降序、索引类型、可见性、解析器、注释等可用属性。
- `TABLE_CONSTRAINTS`、`KEY_COLUMN_USAGE`、`REFERENTIAL_CONSTRAINTS`：主键、外键、约束字段顺序和更新/删除规则。
- `CHECK_CONSTRAINTS`：检查表达式及执行状态（仅受支持版本）。
- `PARTITIONS`：分区方式/表达式/边界/分区属性。
- `VIEWS` 与 `SHOW FULL COLUMNS FROM <view>`：视图定义、算法、安全模式、定义者、检查选项和可见列。

对表调用 `SHOW CREATE TABLE`，对视图调用 `SHOW CREATE VIEW`，将返回 DDL 原样保存。对象/库/列标识符不能用 SQL 参数化时，统一使用驱动的 identifier escaping（MySQL 反引号并转义反引号）；所有数据值仍走参数化。元数据查询对 server version/权限差异使用能力探测或列存在检测，而不是假设每个 INFORMATION_SCHEMA 列都存在。

### 6.4 兼容性与一致性边界

没有单一元数据查询能跨所有 MySQL 版本返回完全一致的列集。首个实施阶段需确定最低支持版本；推荐先验证 MySQL 8.0/8.4，再决定是否覆盖 5.7 和 MariaDB。若支持 MariaDB，按产品类型拆能力矩阵，不能仅检查版本数字。

一个对象的元数据读取和 `SHOW CREATE` 不是跨对象的事务性快照。MySQL DDL 有 metadata lock 和权限约束，但应用不应为备份擅自执行锁表/写入操作。建议在对象读取期间按“元数据查询 → SHOW CREATE → 元数据关键签名复读”检测结构并发变化；不一致则重试一次，仍变化时将此对象标记失败并解释原因。

### 6.5 权限和失败语义

建议文档列明连接账号至少需要连接目标实例、查看目标 schema 对象、读取目标对象定义的只读权限；视图 `SHOW CREATE`、检查约束/分区等可能需要额外可见性。缺权限的类目不能记为“空”。目标属性无法采集时保存结构快照的 `captureWarnings`，并由产品确认是允许不完整快照还是整个对象失败。原始 DDL 读取失败时不应声称快照含有完整 DDL。

## 7. 模块详细实施方案

### 7.1 连接管理

**页面：** 按 Demo 做连接列表和新增/编辑模态窗口，字段、搜索、总数、测试/测试全部、删除确认及导出提示一致。所有数据来自 `connections.json`。

**连接配置：** `Id(Guid)`、`Name`、`Host`、`Port`、`UserName`、`PasswordCiphertext`、`DefaultDatabase`、TLS 选项、创建/更新时间。连接编辑仅更新 profile；改名不会改变 Id。状态在内存 ViewModel 中维护（未测试/测试中/成功/失败），测试完成时间可显示但每次启动默认视为未测试。

**列表行为：** 编辑保存后验证名/主机/端口和重名规则；改动任何连接参数使当前状态失效。测试单项和全部状态是异步操作，禁用重复操作，全部测试需汇总每一连接结果和错误。删除只删除 profile，不删除备份历史；确认框说明关联备份仍保留。

**导出：** 先实现 Demo 明确的导出操作，但采用具有字段转义能力的格式（建议版本化 JSON）；包含密码必须另行勾选并二次明确确认。产品待确认是否保留 Demo 的纯文本 TXT，或增加导入。

### 7.2 结构备份

**目录实体：** 环境/版本节点由用户创建；重名校验在同级完成（推荐忽略大小写以匹配 Windows 物理目录和 Demo）。环境/版本名可重命名，ID 保持不变。连接节点来自 profile；数据库节点来自实时发现；对象文档在第一次捕获成功时才创建，避免留下无内容的库/对象节点。

**从任意目录发起：** 在 `BackupViewModel` 创建带 `FixedEnvironmentId/FixedVersionId/FixedConnectionId/FixedDatabaseName` 的向导上下文；来自选中节点的上级级联项只读。未固定级别按环境→版本→连接→数据库顺序选择，上级变化时清除下级。数据库/对象请求支持取消、加载、空结果和错误状态。

**候选与去重：** 读取目标数据库表/视图，然后查询当前环境、版本、连接、数据库下已存在的对象快照。已备份对象在列表禁用并标记“已备份”；其余支持搜索、表/视图过滤、多选、全选可备份/清空和数量摘要。对象类型不能形成额外目录层。

**批量捕获：** 每对象独立捕获、结构校验、生成 JSON 和原子独占创建；目标文件已存在时永不覆盖。执行结果按对象列出成功/失败/原因；失败对象可重试，已成功文档不会回滚。重复唯一性检查必须在写入临界区重查，而不仅是向导候选过滤。

**查看/导出/删除：** 历史对象完全从文件读取，不要求有现存连接。查看各结构分类和原始 DDL；导出单份快照完整 JSON；删除前显示对象/后代文档计数并二次确认。删除后相同路径对象恢复可备份；只删除目标快照，不影响其他路径。

### 7.3 结构对比

**Source abstraction：** `SchemaSource` 明确 `SnapshotSource(snapshotId)` 或 `LiveSource(connectionId,database,objectName,kind)`。历史源读取冻结记录，不回源；实时源重新连接并捕获只读结构模型。源摘要在界面展示来源、路径和捕获/读取时刻。

**选择和生命周期：** A/B 独立级联选择；历史选择器由快照仓储提供，实时数据库/对象由驱动发现。切换上游时清空下游，并立即将已有 CompareResult 标为过期；只有对比成功后才能显示新结果。交换源后重新算差异或显式显示过期，不能交换标签却仍显示旧方向数据。必须限制表↔表和视图↔视图。

**差异计算：** 按 Schema 模型类别和条目稳定键匹配；条目仅 A/B 存在标缺失，两侧都有但属性值不同标修改，全等标相同。条目名称变化默认表现为 A 缺失 + B 新增（待确认是否应该推断为重命名）。差异比较字段语义，区分 null、没有 DEFAULT 和表达式；集合/列表属性排序按定义的语义规范化。原始 DDL 文本不同不能单独判定结构属性差异。

**界面：** 左侧各类别展示条目数量和差异数量；概览有总条目、缺失、属性变化等明确统计。字段、索引、外键等按条目横向 A/B 列表；双击修改条目打开 `属性 | A | B` 明细。数据库/主键/选项/视图等用逐属性表。仅差异勾选过滤所有类别，关闭显示相同条目。展示完整 A/B 元数据和 DDL。

**迁移 SQL：** 提供 `A→B`/`B→A` 文本草案、复制、另存为 `.sql`，清楚区分历史原始 CREATE DDL 和根据 Delta 生成的 ALTER/DROP/CREATE 语句。生成器分类型支持，输出每项 warnings/人工操作说明；不能确认安全性的变更不生成貌似完整 SQL。任何 SQL 不执行，不包含连接密码。

## 8. 产品规则与边界

### 8.1 连接/快照生命周期

- 连接名称修改不改 ID，快照保留捕获时来源摘要。
- 连接删除不级联删除任何备份、不使历史结构不可读；在线对比失去该连接后显示不可用，而不是破坏该 Profile ID 对应的快照。
- 删除/重命名备份环境或版本必须显示级联对象数；稳定 ID 保证名称更新无需移动/丢失引用。
- 单路径对象快照不可覆盖；要更新必须用户显式删除旧文档再重新采集。
- 备份不包含数据行、用户密码、连接串秘密。

### 8.2 MySQL 标识符/路径

数据库、表、列名是服务端标识符，不应假设仅字母数字。逻辑唯一性用 MySQL 名称及 `lower_case_table_names` 语义；Windows 文件目录只用 ID/安全编码键。UI/JSON 正文保存原名，SQL 标识符按反引号规则转义。环境/版本名做同级名称唯一、长度/空白验证；不可直接把任意用户名称拼入本地文件路径。

### 8.3 事务、并发及恢复

- Connection Profiles JSON 用 `IAtomicFileWriter`；修改由 repository lock 串行化。
- 各级 Catalog index/目录元数据使用同目录临时文件后原子替换，写入前备份/检查 SchemaVersion。
- 快照 JSON 使用 `CreateNew`/不覆盖的原子发布；备份批次按对象逐项提交。
- 删除目录优先通过已知 ID 清单级联，确认后只删除本工具管理的文件。损坏配置/快照不应自动改写成默认值；复制 `.corrupt-*` 后报告恢复工具。
- 大型树可从目录索引缓存加载，但快照记录仍为事实源；索引可重建。是否需要索引要以实际文档规模测试后决定。

## 9. 现状与需求差距

| 需求 | 当前实现 | 差距/需新增 |
|---|---|---|
| 主界面新工具入口 | 有模块 ID、Launcher 卡片和占位工具机制 | 新增独立 MySQL Schema ID、单键、图标、一个模块和设置/窗口贡献；更新 `ToolLaunchKeys` 默认值、Architecture Tests |
| 三模块内导航 | 有 WPF 工具设置分区、各工具独立窗口 | 新增统一 Schema 主窗口和连接/备份/对比内部页；复用 `SettingsViewFactory` 风格，不将 HTML 作为 WebView 最终界面 |
| 真实连接管理 | 当前没有 MySQL provider；Demo 的 localStorage 仅模拟 | 增加 Connector、 profile 仓储、DPAPI 密钥保护、真实测试、查询和导出 |
| 真实结构发现 | 无数据库 schema reader | 增加版本/权限感知 INFORMATION_SCHEMA 读取和 `SHOW CREATE` |
| 本地结构文档 | `AtomicFileWriter`、JSON metadata、文件 CRUD 有成熟实现 | 新增 Snapshot 文件模型、目录 Catalog、唯一约束和离线历史读取 |
| 结构比较 | 现有 JSON 文档差异只比较文本文件；不能复用为数据库 schema diff | 新增结构化模型差异服务、类别/属性细分、实时/历史统一源 |
| 原始 DDL/变更 SQL | 无数据库对象 DDL 采集或迁移 SQL 生成 | 两套分离的接口和数据：服务器原始 DDL 快照；有限且审查友好的 SQL 生成器 |
| 本地数据根 | 已有 `<root>/tools/<toolName>` 与本地 JSON/CSV | MySql Schema 使用 `tools/mysqlschema`；确保首次运行建目录和默认连接文件 |
| 自动化测试 | 分工具 xUnit、架构引用基线 | 添加纯模型/仓储/Reader fake 测试、架构边界测试和可选 MySQL 集成测试 |

## 10. 分阶段实施计划

### 阶段一：工具入口、连接模型与安全仓储

1. 增加 `ToolId.MySqlSchema`、默认单键（推荐 `M`，与 JSON/Toto/EchoHttp/Kairos 的 J/T/E/K 不冲突）、描述、顺序、图标资源及工具模块；更新默认键单键校验测试。
2. 新建 `Juju.Tools.MySqlSchema` 类库/测试项目，增加强类型 Connection Profile、列表/校验和本地 JSON repository。
3. 加入 DPAPI `ICredentialProtector` 实现；保存的密码使用保护密文。验证连接编辑重置状态、删除不触碰 `backups/`、导出秘密开关及敏感提示。
4. WPF 重建连接列表/新增编辑/确认对话框/搜索/测试状态，不使用浏览器 localStorage。
5. Spike 验证 MySqlConnector 版本、TLS、连接超时、连接测试错误映射和许可。

**验收：** profile CRUD、搜索、密码保护、仅用户明确选择才导出明文、普通 Windows 用户可读写数据根目录下文件。

### 阶段二：真实数据源与结构采集

1. 定义 `IMySqlConnectionTester`、`IMySqlSchemaReader` 和接口 fake。
2. 真实连接测试；分别列出允许访问的 database、表和 view。
3. 读取服务端版本/能力和对应类型结构字段；逐对象读取 SHOW CREATE。
4. 使用多版本 MySQL 测试矩阵验证字段、索引、FK、CHECK、partition、views、生成列、不可见属性和权限边界；未知列的处理要明确。
5. 对读取过程遇到结构变更时做 signature 复核/有限重试或返回明确 capture warning。

**验收：** 无 DB 或连接失败时看到错误，不误报空对象清单；只读权限下可成功的范围明示；没有 SQL 写入。

### 阶段三：环境/版本/连接/数据库本地 Catalog

1. 设计路径实体、稳定 ID、同级名称唯一规则和安全文件 key。
2. 实现环境/版本新增、重命名、树显示、搜索、展开/折叠和删除级联预览。
3. `Connection` 层只关联 Profile ID；显示名称从 profile/捕获摘要展示，不把连接名硬编码成备份目标。
4. 确定删除连接的无级联行为及连接清理 UI 的“仍有备份”提醒。

**验收：** 可在数据库目标无文档时表示空节点/自动首份文档前不留错误节点；同环境重名失败、不同环境同名版本成功；改名后 ID 不变，重启后树可重建。

### 阶段四：独立结构快照与批量备份

1. 建立版本化 schema record，确认 `null`、无默认值、literal default、expression default 的表示。
2. 根据选中环境/版本/连接/数据库生成上下文锁定的级联向导。
3. 实时发现候选对象，过滤同目标已备份对象，添加搜索、type filter、多选/全选/清空和数量。
4. 逐对象捕获；保存结构、原始 DDL、capture time/server/source summary/warnings；独立原子写入且创建前再次检查唯一键。
5. 批量返回逐项成功/失败；成功项保留；删除对象和文档前显示级联计数；文档查看只读和完整导出。

**验收：** 同路径对象禁止覆盖；删文档后可重备份；其他版本/环境同名快照不受影响；数据库掉线后快照仍可读；部分失败信息正确。

### 阶段五：统一的 Snapshot/Live Source 对比

1. Snapshot 源读取本地不可变记录，Live 源通过 Reader 即时抓取；来源配置编辑后标旧结果过期。
2. A/B 类型不匹配阻止对比；支持不同对象名同类型对比；swap 交换来源并刷新。
3. 实现键匹配、缺失/新增/修改/相同状态、类别统计和仅差异筛选。
4. WPF 对比页按 Demo 建对称 DataGrid、概览、属性详情双击和完整元数据查看。

**验收：** 历史/历史、历史/实时、实时/实时全组合；服务器离线不影响历史源；统计与表格 delta 一致；配置变化不能继续展示旧结果。

### 阶段六：DDL 与完善

1. 原始 DDL 使用快照中捕获的字符串，不重建。
2. `IMigrationSqlGenerator` 对支持的 Delta 生成方向明确的脚本及风险警告；暂不执行。
3. 支持复制、导出 SQL/完整快照/连接档案（按用户选项保护密码）。
4. 完成高 DPI、较小窗口/长名称、WebView 无关性、错误提示和性能验收。

**验收：** SQL 不会被执行；unsupported/危险 delta 清楚警告；原始 DDL 总与快照一致；无秘密泄漏。

### 阶段七：端到端及发布验收

- CI 执行所有 mock 单测、仓储测试、Architecture Tests、XAML/WPF build。
- 可选将环境变量指定的测试 MySQL 实例接入集成测试；默认 CI 不依赖外部数据库。
- 手工验证目标版本和最低支持版本、连接失败/权限不足、真实批量备份、重开离线快照、4 种来源组合、SQL 输出和 DPAPI 导出。
- 将新工程加入解决方案、release 还原；确认自包含/轻量包都包含正确的 MySQL 模块 assembly，不把连接配置或数据库快照放进安装程序/自动更新 ZIP。

## 11. 测试与验收细化

### 11.1 连接管理

- 必填项缺失、端口为 0/65536/非整数、名称同名、host/db 空值规范化。
- 新连接测试成功、认证失败、连接拒绝、DNS 失败、超时、取消；测试全部一个成功一个失败状态准确。
- 修改任意连接属性后 status 重置；改名保持 ID。
- 删除有历史快照的 profile，只删除连接，不删快照；历史打开/比较继续成功，实时来源提示 profile 不可用。
- 导出不含密钥默认行为、用户明确选择包含密钥前提示、输出字符转义/Unicode/换行完整。

### 11.2 结构备份

- 从环境/版本/连接/数据库发起的目标预填和锁定正确；级联选择上游改变时下游清空。
- 数据库/对象获取 error 不等同于空列表；系统 schema/无权限结果按确认规则显示。
- 对象过滤：已有目标对象 disabled，不同类型同名行为符合唯一规则；大小写依 `lower_case_table_names`。
- 逐对象保存含完整源摘要、server version、结构属性、DDL；同路径重复提交两次在竞争下只能有一份成功。
- 批次中一对象权限错误时其他成功对象保留，失败对象重试不覆盖成功文档。
- 删除/重命名环境、版本、文档只影响预期 ID 范围；删除 profile 不影响备份。
- 断开原连接后仍可查看并导出全部历史内容。

### 11.3 结构对比及 DDL

- Snapshot/Snapshot、Snapshot/Live、Live/Snapshot、Live/Live。
- table/view 混合报错；允许同类型不同名字。
- 字段新增/删除、类型改动、NULL/default/generated/comment/charset/collation 改动；索引字段顺序/唯一/可见性变化；外键规则变化；CHECK/partition/view 定义变化。
- `NULL`、空字符串、无 default、函数表达式 DEFAULT 的语义不被折叠。
- `onlyDiff` 过滤一致；概览总数/缺失/修改分别和底层 delta 对齐。
- A/B 交换及来源更改 invalidates old result；Live 重新读取，Snapshot 固定。
- 生成 SQL 包含方向，DROP 等危险项提示，视图安全定义/definer 差异的 SQL 显示不支持或人工步骤；从不自动执行。

## 12. 待确认问题与推荐

| 待确认问题 | 推荐初始处理 | 原因 |
|---|---|---|
| 最低 MySQL 版本/是否支持 MariaDB？ | 首期明确为 MySQL 8.0/8.4，经集成测试后再扩大 | 元数据列、CHECK/不可见索引/列、DDL 行为跨版本差异明显 |
| TLS 是否需要自定义 CA、证书校验忽略等？ | 默认启用 Connector 的安全默认值；提供 SSL mode 与 CA 配置，但不提供默认忽略证书验证 | 生产连接安全；Demo 未画 TLS |
| 是否允许查看系统 schema？ | 默认列全 connector 实际授权可见的库，系统 schema 可在 UI 隐藏/显示开关确认后处理 | `SHOW DATABASES` 权限与保留库策略不同 |
| 批量备份前目标路径/连接/数据库/对象重复的大小写规则？ | 采用服务器 `lower_case_table_names`；按 MySQL 名称语义建立唯一键 | Windows 本地文件名本身不区分大小写，但 MySQL 配置影响对象名语义 |
| 连接删除时必须确认“有关联快照”吗？ | 有备份时显示快照数量和“删除连接不删除快照”说明，profile 才可删除 | 满足历史独立性且避免用户误解 |
| 备份捕获发现结构读取过程并发 DDL 怎么办？ | 复读结构签名一次；再变化则该对象失败并保留旧备份 | 不依赖高权限锁库，避免不一致快照 |
| 元数据权限不足时是否保存不完整快照？ | 首期默认要求完整核心属性和原始 DDL，缺失核心 DDL 即该对象失败；可选属性用 warning/unknown 标记 | 避免伪造空属性和错误对比 |
| “结构差异数量”按对象、属性还是 SQL 操作计数？ | 概览展示三种清晰统计：变化对象数、缺失对象数、变化属性数 | Demo 统计写得简略，提前明确可避免测试/界面解释不一 |
| 不同表名/视图名允许对比的目的是什么？名称变化按“新增+缺失”还是推断重命名？ | 允许同类型不同名，首期按缺失+新增，不猜测重命名；名称差异在摘要显示 | 自动推断可能把两个不同对象错误配对 |
| 是否首发提供可执行性更强的 ALTER 生成？ | 首期只对安全覆盖的列/索引操作生成，其他显示人工操作提示，不执行 | 生成完整 MySQL Migration SQL 涉及依赖顺序和数据风险，Demo 明示其脚本只是示例 |
| 连接导出格式及是否支持导入？ | 采用版本化 JSON；默认不含密码，密码为可选明确确认，导入单独设计 | Demo TXT 对换行/转义/迁移恢复不够健壮 |
| 数据库查出的对象量很大时，是否允许筛选所有 system objects？ | 第一期先分页/异步加载和搜索，加入对象数上限/取消；过滤规则按实际测试再定 | 避免全量结构读取让 UI 卡住 |
| 删除环境/版本/数据库节点的范围 | 明确显示将删的快照总数并要求确认；支持取消；已删除的 Profile 不纳入级联 | 该操作不可恢复，物理文件删除需严格限定本工具数据根 |

## 13. 预期方案审核结论

方案按现有模块架构做增量：Juju 外壳和数据根保持不变；新工具拥有独立业务项目、WPF UI、MySQL Reader 和本地快照仓储。Demo 用来定界 UI/业务流程，模拟列表、假连接成功、虚构结构和不完整 DDL 一律替换成真实服务端读取和结构化本地文档。实施前优先确认第 12 节的 MySQL 支持矩阵、TLS、元数据缺失策略、导出格式与 SQL 生成范围；确认后从 Connection+密码保护和实际 Schema Reader Spike 开始，不先大规模铺界面。
