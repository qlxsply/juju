# juju Windows 自动更新技术方案与开发计划

> 状态：开发实现完成；真实 GitHub Release 现场验证待执行
> 适用平台：Windows x64，当前通过 GitHub Releases 发布
> 最后更新：2026-11-01

## 1. 目标与已确认策略

为 juju 增加完整的 Windows 桌面应用更新体验：应用在启动时检查新版本，用户可手动检查；用户点击更新后，应用下载并验证更新包，安全保存数据并退出，安装新版本，启动失败时回退到上一版本，成功后启动新版本。

已确认的产品策略：

- 启动时检查更新，检查结果设置 24 小时冷却；提供手动检查入口。
- 只检查正式稳定版，忽略预发布版本。
- 更新包通过 HTTPS 下载，并以 SHA-256 校验；设计中预留 Authenticode 签名验证能力，当前不要求证书。
- 更新由用户确认启动，不强制安装。
- 必须在数据刷新和关停成功后才开始切换版本。保存失败时取消更新并保持应用运行。
- 安装或新版本启动失败时显示错误，并尝试恢复、启动旧版本。
- 使用“版本目录安装 + 稳定启动入口 + 独立更新助手”，不原地覆盖正在运行的程序。
- 不实现旧安装布局识别、旧版本兼容逻辑或旧数据自动迁移。

## 2. 当前实现基线与关键风险

- `.github/workflows/release.yml` 在 `juju_vX.Y.Z` tag 上构建自包含 Windows x64 应用、Inno Setup 安装包，并发布到 GitHub Release；目前没有更新包 manifest 或 SHA-256 资产。
- `installer/juju.iss` 使用固定 `AppId`，默认 per-user 安装目录为 `%LOCALAPPDATA%\Programs\juju`，安装文件直接写入该目录。
- `ApplicationPaths` 目前以 `AppContext.BaseDirectory` 推导默认数据目录，并在该目录读取 `data-location.json`。应用移入版本子目录后，必须改为通过稳定安装根目录定位数据，否则版本切换会使数据路径发生变化。
- `App` 已通过命名 Mutex 保证单实例，并使用命名管道激活既有进程。
- `DesktopAppLifecycle.ShutdownAsync` 执行窗口、设置、工具和 DI 服务清理，但会记录并吞掉一般异常。
- `JsonToolWindow.ShutdownAsync` 调用 `FlushCurrentAsync`，但没有检查其布尔结果。当前不能保证 JSON 内容刷新失败时阻止应用退出。

因此，**更新关停的可失败/可中止语义是实现的前置条件**。必须先修复保存失败被忽略的问题，再接入更新助手；不能在数据状态未知时强行结束进程或安装。

## 3. 总体架构

### 3.1 稳定根目录与版本目录

建议使用以下 per-user 目录结构：

```text
%LOCALAPPDATA%\Programs\juju\
├── Juju.Launcher.exe          稳定启动入口，桌面/开始菜单快捷方式始终指向它
├── Juju.Updater.exe           更新协调程序；运行副本可放在用户临时目录
├── state\
│   ├── active.json            当前版本及候选版本切换状态
│   ├── active.json.bak        原子状态备份
│   ├── update-check.json      上次检查时间、ETag 等检查缓存
│   └── *.log                  Launcher/App/Updater 故障诊断日志（不含用户文档内容）
├── versions\
│   ├── 1.2.0\                 不可变的版本文件
│   └── 1.3.0\
├── staging\                  下载和解包中的临时内容
├── data\                     应用配置和应用/工具数据
└── data-location.json         可选的外部数据根目录指针
```

规则：

1. 快捷方式只指向稳定 Launcher，不直接指向任何版本目录中的 `Juju.App.exe`。
2. 应用和编辑器资源放在各自的 `versions/<semver>/` 中；版本目录发布后不可修改。
3. 持久数据只在稳定根目录的数据目录或用户指定的外部目录，不得写入版本目录。
4. Launcher 将稳定根目录通过受控启动参数或环境变量传给版本化应用；`ApplicationPaths` 基于该根目录解析 `data` 和根级 `data-location.json`，不依赖版本程序的 `AppContext.BaseDirectory`。
5. 不实现旧平铺安装布局的探测或迁移。新布局不尝试解释旧版本文件结构。

> 迁移边界：新架构运行后的版本更新必须复用稳定数据目录。对于采用旧平铺布局的既有安装，本方案不提供自动布局转换或旧数据自动迁移；首次采用新布局时按“新布局安装”处理。若产品要求保留该次切换前的数据，需另行确认是否允许一次性手动备份/恢复流程，不能隐式加入旧布局兼容代码。

### 3.2 组件职责

**稳定 Launcher**

- 读取并验证 `state/active.json`，确定要启动的版本目录。
- 启动版本化应用并传递稳定根目录、启动参数及一次性启动确认 IPC 信息。
- 在候选版本试启动期间等待应用发出“核心启动完成”信号。
- 试启动成功后提交新活动版本；超时、退出或启动失败则选择上一稳定版本并尝试启动回退版本。
- 用户快捷方式及安装程序创建的入口始终调用 Launcher。

**版本化应用**

- 承担更新检查 UI、GitHub API 通信、下载进度展示和用户确认。
- 在开始退出前执行更新专用的保存/关停准备。
- 安全关停完成后启动独立 Updater，并退出自身。
- 新版本启动完成必要初始化后向 Launcher 确认启动成功。

**独立 Updater**

- 在应用进程之外运行，等待应用进程完全退出。
- 校验候选包、将其从 staging 安装到版本目录、写入待切换状态。
- 启动稳定 Launcher，并等待结果；失败时保留旧版本并尝试启动旧版本。
- 运行位置必须在被更新的版本目录之外。可将 Updater 自身复制到 `%LOCALAPPDATA%\Temp` 下的随机专属目录，避免自身文件被更新操作锁定。

## 4. Release 元数据与更新检查

### 4.1 Release 资产

每个稳定 Release 至少包含：

```text
juju_vX.Y.Z_setup_win64.exe       首次安装/修复用 Inno Setup 包
juju_vX.Y.Z_win-x64.zip           供已安装应用增量切换版本使用的自包含程序包
juju_vX.Y.Z_manifest.json         版本、平台、包名、SHA-256、最低 Launcher 协议版本等
juju_latest.json                  GitHub latest-release 下载入口指向的稳定版 manifest 副本
```

Manifest 建议字段：

```json
{
  "schemaVersion": 1,
  "version": "1.3.0",
  "channel": "stable",
  "rid": "win-x64",
  "packageAsset": "juju_v1.3.0_win-x64.zip",
  "packageSizeBytes": 123456789,
  "sha256": "<64 个十六进制字符>",
  "minimumLauncherVersion": "1.0.0",
  "releaseNotesUrl": "https://github.com/qlxsply/juju/releases/tag/juju_v1.3.0"
}
```

实际 schema 应使用明确的 JSON 命名约定和强类型 DTO。版本号必须与 tag、Release 名称及资产名一致。Workflow 在发布前计算 ZIP 的 SHA-256，并校验所有资产完整存在。

SHA-256 从同一 GitHub Release 通过 HTTPS 获取，可检测下载损坏和资产不匹配；在未签名阶段，它不能抵御 GitHub 账号、仓库权限或发布 workflow 被攻陷。日后增加 Authenticode 时，校验器通过独立接口接入签名和发布者证书策略，而不是把签名逻辑散落到下载流程中。

### 4.2 检查策略

- 从 `https://github.com/qlxsply/juju/releases/latest/download/juju_latest.json` 读取最新稳定版 manifest；不采用 prerelease 或 draft，也不依赖未认证额度较低的 GitHub REST API。
- 启动检查遵循 24 小时冷却。检查时间及 ETag 缓存在稳定状态目录；手动检查绕过冷却。
- 使用 `HttpClient`、请求超时、取消令牌和 ETag 条件请求；离线、限流或 GitHub 暂时不可用不得阻止应用启动。
- 使用 SemVer 比较版本，不用字符串排序；拒绝降级、无效 tag、错误 RID、包名不匹配及低于最低 Launcher 协议版本的 manifest。
- “关于”区域显示当前版本、检查状态、可用版本及 Release notes 链接。启动时发现更新采用非阻断提示，由用户点击“更新”后下载/安装。

## 5. 更新状态机与完整流程

建议显式建模状态，禁止用散落布尔值控制进度：

```text
Idle → Checking → UpdateAvailable → Downloading → Verifying → Staging
     → AwaitingUserConfirmation → PreparingShutdown → Applying
     → StartingCandidate → Completed
```

任一适用状态可进入 `Cancelled` 或 `Failed`；`StartingCandidate` 失败进入 `RollingBack`。取消在版本包验证/准备退出前均可用；进入 `PreparingShutdown` 后，由协调流程完成提交或返回可启动的旧版本状态。

### 5.1 检查、下载与验证

1. 检查新版本并显示版本、变更说明、下载大小。
2. 用户点击更新后下载到 `staging/<version>.partial`，展示字节/百分比和取消入口。
3. 下载完成后校验 SHA-256、manifest 版本、资产名和架构；失败删除临时包并显示明确错误。
4. 解压前拒绝绝对路径、`..` 路径穿越、重复路径、超出策略的解压体积和不受支持的重解析点；目标必须规范化后仍位于 staging 内。
5. 验证程序清单、主程序、关键资源和必需依赖，检查磁盘空间。验证通过后 staging 标记为只读/准备完成。

### 5.2 安全关停与版本提交

1. 更新前通知用户保存并关闭需要处理的编辑内容；允许取消。
2. 两阶段关停：先刷新所有脏编辑器及设置；任何刷新失败都保持窗口/服务运行并中止更新。刷新全部成功后，才捕获状态、停止定时器/工具、关闭窗口、释放托盘/DI/文件句柄。
3. 只有关停所有必要阶段成功后，应用才启动 Updater 并退出。Updater 等待原进程结束及单实例 Mutex 被释放，不使用强制杀进程作为正常路径。
4. Updater 将 staging 原子提升为 `versions/<version>`，然后将候选版本记录到待验证状态。旧版本保持完整，不进行覆盖。
5. Updater 启动稳定 Launcher；Launcher 以候选版本启动应用，等待启动确认。确认点设在 WPF 初始化、核心数据/设置载入、工具模块启动成功之后。
6. Launcher 收到确认后原子写入活动版本状态，清除 pending 状态。启动失败或超时则清除候选状态，启动旧活动版本并报告失败。

### 5.3 原子状态文件

- `active.json` 更新采用同目录临时文件、flush 到磁盘后原子替换。
- pending 状态记录 `previousVersion`、`candidateVersion`、更新事务 ID、创建时间和状态阶段。
- Launcher 启动时恢复未完成事务：pending 候选没有启动确认时默认回退，不猜测或删除用户数据。
- 活动版本指向的目录不存在或校验失败时，尝试最近的已验证版本；全部失败则显示修复指引，并保留现场供诊断。

## 6. 退出安全与数据保护要求

在接入 Updater 前，先建立可测试的更新关停契约：

- JSON 编辑器刷新必须检查 `FlushCurrentAsync` 结果。刷新失败不能继续关闭窗口或启动安装。
- 关停过程必须区分“数据刷新失败”“服务清理失败”“进程已完全退出”，向调用方返回明确结果，不能只记录并吞掉异常。
- 更新流程应能在安全退出前取消，且重复点击、重复托盘退出及窗口关闭事件只执行一次关停事务。
- Toto 提醒调度、后台管道、托盘图标、WebView2、文件 watcher、定时器及日志服务要在进程退出前完成停止/释放。
- 用户数据始终位于稳定数据目录；版本目录只放程序，不让版本清理代码触及数据目录或外部目录。
- 停止/释放阶段失败时记录足够诊断信息；只要数据安全或文件锁释放状态不能确认，就不得进入版本切换。
- 首装/卸载期间，如果应用运行中，Inno Setup 读取稳定根目录保存的实例 Mutex 名称，请求 Launcher 发送安全关闭命令并等待 Mutex 释放；保存失败或用户取消时终止安装/卸载流程。

## 7. 失败处理与恢复矩阵

| 失败点 | 预期行为 |
| --- | --- |
| GitHub 不可用、限流或离线 | 静默保留当前版本；手动检查显示可理解的错误，不影响启动 |
| 用户取消下载 | 删除 `.partial`，当前版本和应用运行状态不变 |
| 哈希、manifest 或架构不符 | 拒绝安装、删除无效 staging、记录诊断信息 |
| 磁盘空间不足或解压失败 | 清理本次 staging，不修改活动版本 |
| 编辑器/设置刷新失败 | 中止更新，窗口保持运行并允许用户处理、重试或取消 |
| 应用安全关停失败 | 不启动安装；报告失败，保留旧活动版本 |
| Updater 在切换前失败 | Launcher 仍按原活动版本启动 |
| 候选版本启动失败/无确认 | 保持旧活动版本，清除 pending 并尝试启动旧版 |
| 切换状态文件损坏 | 尝试最近的已验证版本；失败时进入修复提示，不清理数据 |
| 旧版本清理失败 | 不影响当前版本；保留目录并记录警告 |

保留当前活动版本和至少一个上一已验证版本。只有新版启动确认且状态提交成功后，才可清理更早版本。回滚仅切换程序版本，不回滚或删除共享用户数据。

## 8. 安全、隐私与运维

- 不请求管理员权限；网络请求只访问明确配置的 GitHub API 与该 Release 的 HTTPS 资产 URL。
- 不执行从 Release notes、文件名或 manifest 中解析出的 shell 命令。
- 版本号、资产名、下载目标、解压后路径均验证；所有子进程使用参数数组/安全 quoting，不拼接可执行命令字符串。
- Updater/Launcher 日志不记录用户文档内容、访问令牌或敏感配置。
- 发布包和安装包均由 workflow 从 tag 构建，workflow 校验 tag、版本、RID、文件名、hash 和资产清单。
- 未来 Authenticode 验证要求固定允许的发布者身份/证书策略，并支持证书更新策略；不能只检查“签名存在”。

## 9. 分阶段开发计划

各阶段完成后运行对应测试，并审阅差异；后续阶段依赖前序阶段通过。状态标记：`[ ]` 未开始、`[x]` 完成。

### 阶段 0：契约、模型与测试边界

- [x] 定义更新版本、Release manifest、状态机和活动/待验证版本状态 DTO。
- [x] 定义稳定安装根目录、版本目录、数据根目录及状态文件 DTO。
- [x] 定义关停结果、更新状态/错误处理、进度和取消接口（分别落在应用生命周期及 `Juju.Update`）。
- [x] 为 SemVer、manifest 校验、SHA-256、原子状态写入、版本回滚、状态机和安全解包建立单元测试。

**验收：**无 I/O 的版本/manifest/state 模型测试覆盖成功和畸形输入；状态转移非法时显式失败。

**实施记录：**新增 `Juju.Update` 和 `Juju.Update.Tests`；24 项测试通过。Launcher IPC 使用当前用户限定的随机命名管道。

### 阶段 1：可靠的更新安全关停

- [x] 修改 JSON 窗口关停接口，刷新失败时向上报告，阻止销毁窗口。
- [x] 将应用生命周期关停拆成刷新预检和实际停止/释放阶段。
- [x] 更新路径只在预检成功后启动 helper；关停失败时取消 helper，应用保持运行。
- [x] 保持退出清理幂等；应用测试项目通过，脏窗口/保存故障的桌面交互需在最终手动验收中覆盖。

**验收：**注入任一数据刷新失败时，应用不进入安装步骤且窗口仍可操作；完整关停完成后 Mutex 和服务资源释放。

### 阶段 2：稳定 Launcher 与版本化路径

- [x] 新增稳定 Launcher 项目，读取原子活动版本状态并启动指定版本。
- [x] 增加 Launcher 与版本化应用的当前用户随机管道启动确认和超时处理。
- [x] 让应用通过 `JUJU_INSTALL_ROOT` 环境变量获得稳定安装根目录。
- [x] 调整 `ApplicationPaths`，确保共享数据目录和外部目录指针不随版本变化。
- [x] 将安装快捷方式目标改为 Launcher；转发启动参数并保留主程序单实例激活。
- [x] 实现 pending 版本启动确认、提交、回退和活动状态备份恢复。

**验收：**两个版本并存时可启动指定版本；活动状态原子切换；候选版本崩溃或超时可回到旧版本；数据路径始终相同。

### 阶段 3：Release 包与首次安装结构

- [x] 调整发布 workflow，产出版本化 win-x64 ZIP、manifest（含 SHA-256）和首次安装 Inno Setup 包。
- [x] 校验发布 tag；根据 tag 构造 SemVer、RID、资产名并计算包 hash。
- [x] 调整 Inno Setup，建立稳定根目录、Launcher/Updater、版本目录、共享数据目录和 Launcher 快捷方式。
- [x] 加入 hash 验证、zip 路径/大小/链接检查、同目录 staging 和失败清理。
- [x] 保留 GitHub 自动 Release notes，并上传安装包、ZIP 和 manifest。

**验收：**干净安装后快捷方式启动 Launcher；首个版本位于 `versions/<version>`；配置/数据目录与程序目录分离。

**实施记录：**干净发布目录下 App、Launcher、Updater 均成功 `dotnet publish`；Inno Setup 6 成功编译版本目录安装包。未执行真实安装器运行，以避免覆盖当前用户环境。

### 阶段 4：版本检查、UI 与下载验证

- [x] 实现 GitHub 稳定 Release 查询、24 小时启动冷却、ETag 和手动检查。
- [x] 实现检查时间/ETag/Release 缓存和离线、限流降级。
- [x] 在“更新与版本”页面显示版本、检查状态、Release notes、下载/更新入口及进度。
- [x] 实现流式下载、取消、`.partial` 清理、SHA-256 校验和安全解包。
- [x] 抽象包签名验证器，当前使用 HTTPS + SHA-256，预留 Authenticode 校验。

**验收：**稳定版发现、预发布拒绝、冷却/手动绕过、ETag、离线、hash 校验、拒绝不可信重定向、安全解包、zip slip 和重复路径均有测试。桌面下载取消、下载中断、磁盘不足和超大包的 UI/进程集成测试留给最终独立 Windows 现场验收。

### 阶段 5：Updater 协调、更新提交与重启

- [x] 实现复制到临时目录运行的单文件 Updater helper 和单更新事务 Mutex。
- [x] 应用安全关停预检后启动 helper；helper 等待 PID 退出并使用一次性 GO gate 确认可安装。
- [x] helper 原子提升已验证版本并写入 pending 状态。
- [x] helper 启动 Launcher，等待候选版本启动确认、提交或自动回退。
- [x] 实现 helper 超时、启动异常、状态备份、失败回退和重复候选清理。
- [x] 保留活动及上一版本；安全清理仅作用于版本目录，不跟随重解析点。

**验收：**端到端成功升级后应用运行新版本且数据不变；模拟每个切换故障点均能启动旧版或明确进入修复提示，不丢失共享数据。

### 阶段 6：端到端验证、发布与运维

- [x] 增加版本/manifest/cache/hash/zip 安全、版本状态备份、回退及清理单元测试。
- [x] 执行全 solution 测试：JSON 41、Toto 77、架构 8、应用 4、更新 22 项均通过（152 项）。
- [x] 执行 clean app/launcher/updater 发布构建，并成功编译 Inno Setup 首装包。
- [x] 使用 `scripts/test-auto-update-e2e.ps1` 在隔离临时安装根目录完成真实版本包 staging、Updater、Launcher、候选应用启动确认、活动版本提交及旧版本保留。
- [ ] 在独立 Windows 测试环境完成 Inno 首装、公开 GitHub Release 下载、数据保存失败中止、候选版本崩溃回退等桌面验收。
- [x] 更新 README，说明新布局、发布资产、检查策略、SHA-256 和旧数据迁移边界。
- [ ] 创建测试 Release 验证公开 GitHub API/资产下载，再正式启用自动更新。

**验收：**154 项 solution 测试、隔离目录版本 A→B 真实进程端到端测试、clean App/Launcher/Updater 发布构建和 Inno Setup 编译均通过。真实公开 GitHub Release 下载及故障注入桌面验收仍待独立测试 Release。

**实施记录：**端到端测试发现并修复 Windows ZIP 目录项以反斜杠结尾时被误当成文件的问题；`Juju.Update.Tests` 新增对应回归覆盖。

在线发布限制：检查时 GitHub 最新稳定版 `juju_v2.0.3` 只含首次安装包，尚无版本 ZIP 和 manifest；因此公开 Release 下载验收必须等下一次包含新资产的测试 Release。

## 10. 建议测试清单

### 单元测试

- SemVer 比较、预发布拒绝、降级拒绝、版本/tag/资产不匹配。
- Manifest schema、SHA 格式、RID、最低 Launcher 版本验证。
- 活动/pending 状态原子保存、损坏状态恢复及所有状态机转移。
- zip slip、绝对路径、重复路径、符号链接/重解析点、解压容量限制。
- 下载更新校验、启动状态提交/回退、状态备份恢复和版本保留策略。

### 集成/桌面验收

- JSON 编辑器有未保存内容时点击更新，验证写盘后退出。
- 编辑器保存冲突/刷新失败，验证更新中止、应用保持打开。
- 下载期间取消、下载中断、hash 错误、manifest 被改、磁盘不足。
- 新版启动成功后活动指针切换；新版启动失败时旧版恢复。
- helper/launcher 在切换前后被强制结束，下一次启动仍可恢复。
- 更新前后配置、Toto 数据、JSON 文档及外部数据目录内容保持一致。

本地版本切换 smoke test：先将 App 发布到 `artifacts/e2e-win-x64`，Launcher/Updater 发布到 `artifacts/e2e-bootstrap`，然后运行：

```powershell
./scripts/test-auto-update-e2e.ps1
```

## 11. 当前执行状态

- [x] 需求策略确认：稳定版、24 小时启动冷却、手动检查、HTTPS + SHA-256、关停失败不安装、版本目录回退。
- [x] 自动更新技术架构与开发阶段写入本文档。
- [x] 阶段 0：契约、模型与测试边界（24 项 `Juju.Update.Tests` 通过）。
- [x] 阶段 1：可靠的更新安全关停。
- [x] 阶段 2：稳定 Launcher 与版本化路径。
- [x] 阶段 3：Release 包与首次安装结构。
- [x] 阶段 4：版本检查、UI 与下载验证。
- [x] 阶段 5：Updater 协调、更新提交与重启。
- [ ] 阶段 6：真实 GitHub 测试 Release、Inno 首装及桌面故障注入验收（隔离目录更新链路已通过）。

代码实现阶段已按顺序完成。隔离目录真实进程更新从 1.0.0 到 1.0.1 成功，Launcher 确认后提交新版本并保留旧版。剩余工作是使用公开测试 Release 在独立 Windows 环境验证 GitHub 资产、Inno 首装和故障回退；完成之前，不应把生产 Release 标记为自动更新已正式启用。
