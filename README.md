# juju Toolbox

**juju Toolbox 是面向 Windows 的轻量桌面工具箱。** 它用一个常驻的启动器和系统托盘入口，统一管理 JSON 格式化与 Toto 日程提醒等独立工具；工具共享一致的窗口与设置体验，同时各自维护业务和数据。

[获取 Windows x64 安装包](https://github.com/qlxsply/juju/releases) · [Toto 详细说明](src/Juju.Tools.Toto/README.md)

## 设计理念

- **一个入口，按需打开工具**：启动器提供统一的工具列表、单键入口和设置入口；关闭或隐藏启动器不等于退出应用。
- **工具模块各司其职**：宿主负责窗口、托盘、全局快捷键、应用设置和生命周期；每个工具保有独立的业务模型、界面和数据目录。
- **后台工作不依赖窗口**：Toto 提醒调度由宿主持续运行，即使 Toto 主窗口隐藏也能发送提醒。
- **本地优先、数据可管理**：配置和工具数据保存在本地数据目录，可在设置中查看或更改目录。JSON 文档保存时会检测外部修改，降低无意覆盖的风险。
- **原生桌面与合适的 Web 技术协作**：主界面、菜单和工具窗口使用 WPF；JSON 工具在本地 WebView2 中运行 Monaco 编辑器，编辑器资源随应用打包。

## 工具

### JSON 格式化

用于创建、整理、编辑和对比本地 JSON 文档。

- Monaco 编辑器提供语法高亮、JSON 校验、格式化、自动保存、全部折叠/展开和逐层展开。
- 文档列表支持筛选、重命名、排序、删除和导入 JSON 文件；删除的文档进入回收目录。
- 支持两个 JSON 文档的并排差异对比。
- 保存时检查文档修订版本；若文件已被外部程序修改，会提示处理冲突，避免静默覆盖。
- 快捷键可在 **设置 → 工具设置 → JSON 格式化** 中修改。默认值：

| 操作 | 默认快捷键 |
| --- | --- |
| 新建 | `Alt+A` |
| 保存 | `Alt+S` |
| 格式化 | `Alt+F` |
| 复制文本 | `Ctrl+C` |
| 复制压缩文本 | `Ctrl+Shift+C` |
| 复制文件 | `Ctrl+Alt+Shift+C` |
| 全部折叠 / 全部展开 | `Alt+Q` / `Alt+W` |
| 逐层展开 | `Alt+E` |
| 进入 / 退出对比 | `Alt+D` / `Escape` |
| 重命名 / 删除文档 | `Alt+R` / `Delete` |

### Toto 日程提醒

用于管理有计划时间、提醒和备注的待办事项。

- 支持新增、编辑、完成、取消、详情查看，以及按条件搜索和分页浏览历史事项。
- 到期提醒由后台调度器负责；窗口隐藏时仍可提醒。
- 可设置每日开始/结束盘点时间、仅工作日提醒，以及节假日和调休日历。
- 历史事项按结束月份归档；跨月历史仍可查询。
- 快速输入示例：

  ```text
  写周报 @1630       # 今天 16:30，使用设置中的默认提醒提前量
  评审 @10221600     # 本月 22 日 16:00
  复盘 @+1600        # 明天 16:00
  会议 @1600@10      # 今天 16:00，单独设置提前 10 分钟提醒
  ```

  支持 `HHmm`、`ddHHmm`、`MMddHHmm`、`yyyyMMddHHmm`、连续 `+` 表示增加天数，以及连续 `W` 加星期数字表示增加周数并指定星期几。例如 `复盘 @+1600` 是明天 16:00，`周会 @W11600` 是下周一 16:00。没有提供最后的分钟数时，使用 Toto 设置中的默认提醒提前量。
- Toto 主窗口内默认快捷键：新增 `Alt+A`、历史 `Alt+Q`、刷新 `Alt+R`、详情 `Alt+D`、编辑 `Alt+E`、完成 `Alt+F`、取消 `Alt+C`。可在 **设置 → 工具设置 → Toto 日程提醒** 中修改。
- 更多数据和提醒说明见 [Toto README](src/Juju.Tools.Toto/README.md)。

### 开发中的工具

启动器也列出 **EchoHttp** 和 **Kairos**，目前是占位入口，选择后会提示开发中。

## 安装与启动

1. 打开 [GitHub Releases](https://github.com/qlxsply/juju/releases)，下载名称类似 `juju_v2.0.0_setup_win64.exe` 的 Windows x64 安装包。
2. 运行安装包。应用按当前用户安装，并创建开始菜单快捷方式；桌面快捷方式默认勾选。
3. 启动后使用 Juju 主界面的全局快捷键（默认 `Ctrl+Alt+Space`）显示或隐藏工具入口。也可以点击桌面或开始菜单中的 **juju** 快捷方式。
4. 在启动器中点击工具卡片打开工具，或按对应单键快速启动：JSON 默认 `J`、Toto 默认 `T`。这些单键可在应用设置中修改。

Juju 默认显示主界面启动；开机自启动、启动时最小化到托盘、点击空白区域自动隐藏等行为可在主界面的齿轮菜单中设置。即使主界面隐藏，应用仍可驻留系统托盘；通过托盘菜单退出可结束应用进程。

### 运行环境

- Windows x64。
- 安装包包含 .NET 运行时，不需要另行安装 .NET SDK。
- JSON 编辑器需要 Microsoft Edge WebView2 Runtime。多数 Windows 11 设备已提供；若 JSON 编辑器提示初始化失败，请安装 Evergreen WebView2 Runtime 后重启 Juju。

## 数据位置

默认数据根目录为安装目录下的 `data` 文件夹；可通过 **设置 → 应用设置 → 存储设置** 更改。

```text
<数据根目录>/config.json                  Juju 宿主设置
<数据根目录>/juju.json                    数据根目录标记
<数据根目录>/app/window-state.json         窗口位置与大小
<数据根目录>/tools/json/settings.json      JSON 工具设置
<数据根目录>/tools/json/metadata.json      JSON 文档列表与顺序
<数据根目录>/tools/json/documents/*.json   JSON 文档
<数据根目录>/tools/json/.trash/            已删除文档
<数据根目录>/tools/toto/config.json        Toto 设置与盘点状态
<数据根目录>/tools/toto/toto_ing.csv       进行中事项
<数据根目录>/tools/toto/toto_end.csv        当前历史事项
<数据根目录>/tools/toto/toto_end_yyyyMM.csv 按月归档的历史事项
<数据根目录>/tools/toto/yyyy.json           对应年份的节假日与调休数据
<数据根目录>/tools/toto/logs/               Toto 日志
```

## 从源码构建

构建需要 Windows、.NET SDK **10.0.400**、Node.js 22/npm；生成安装包还需要 Inno Setup 6。SDK 与前端依赖初始化后，可在仓库根目录使用以下命令。

```powershell
git clone https://github.com/qlxsply/juju.git
cd juju

# 首次初始化：恢复 .NET 项目、仓库本地格式化工具和前端依赖
dotnet restore Juju.sln
dotnet tool restore --tool-manifest .config/dotnet-tools.json
npm ci --prefix editor
```

### 编译、测试与清理

```powershell
# 编译整个解决方案
dotnet build Juju.sln --configuration Debug
dotnet build Juju.sln --configuration Release

# 运行解决方案中的测试
dotnet test Juju.sln --configuration Release

# 清理所选配置的 bin/obj 构建输出
dotnet clean Juju.sln --configuration Release
```

清理自包含发布目录和安装包目录（不会删除源码）：

```powershell
foreach ($path in @('artifacts\win-x64', 'artifacts\installer')) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
```

### 统一格式化

格式规则由根目录 `.editorconfig`（C#）和 `Settings.XamlStyler`（XAML）提供。日常开发可以在 Rider 中使用 C# 的 **Reformat Code**；要统一格式化整个解决方案及 `src/`、`tests/` 下的 XAML，请在修改前确认工作区状态后运行：

```powershell
# 主动格式化 C# 与 XAML，会修改未符合规则的文件
./scripts/format.ps1

# 只检查，不修改源码；不合规时会返回非零退出码
./scripts/check-format.ps1
```

格式检查是开发者本地的手动步骤，不属于构建或 GitHub Actions 工作流。建议在准备提交/推送前运行检查，并查看 `git diff` 确认格式化结果。脚本要求 .NET SDK 10.0.400 和 PowerShell 5.1 或更新版本。
当前仓库仍有既存 C# / XAML 格式差异，因此首次运行只检查脚本可能返回非零；如需修正，请先检查工作区，再明确运行主动格式化脚本并审阅差异。

### Rider 设置

打开 **Settings → Editor → Code Style**，确认 **Enable EditorConfig support** 已启用（Rider 默认启用）；项目 `.editorconfig` 中的规则会覆盖 Rider 对应的 IDE 默认值。不要把 Rider 设置导出回去覆盖仓库的 `.editorconfig`。可以用 **Help → Find Action → Show Code Style Configuration** 查看当前文件实际生效的规则。Windows 下使用 **Code → Reformat Code**（默认 `Ctrl+Alt+L`）格式化 C#。

XAML 的权威格式化器是仓库本地 XAML Styler，而不是 Rider 内置 XAML 格式器。要保证一致，使用 `./scripts/format.ps1` 统一格式化 XAML；工具版本和配置由 `.config/dotnet-tools.json`、`Settings.XamlStyler` 固定。详见 [代码格式说明](docs/code-formatting.md)。

### 生成 Windows x64 安装包

从仓库根目录运行以下命令，将示例版本替换为实际版本。`dotnet publish` 输出自包含的 Windows x64 应用；随后 Inno Setup 6 将它打包为安装程序。

```powershell
dotnet publish src/Juju.App/Juju.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Version=2.0.0 -o artifacts/win-x64

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
& $iscc '/DAppVersion=2.0.0' 'installer\juju.iss'
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
```

安装包将生成在 `artifacts/installer/juju_v2.0.0_setup_win64.exe`。

## 自动发布

向 GitHub 推送格式为 `juju_vx.y.z` 的 tag（例如 `juju_v2.0.0`）会触发 [Release workflow](.github/workflows/release.yml)。工作流会校验 tag、构建自包含的 Windows x64 应用、生成 Inno Setup 安装包，并将 `juju_vx.y.z_setup_win64.exe` 附加到 GitHub Release。

## 项目结构

```text
src/Juju.App/             WPF 启动器、设置窗口与工具窗口
src/Juju.Core/            宿主设置、数据根目录、工具抽象与共享基础设施
src/Juju.Platform.Windows/ Windows 托盘、快捷键、开机启动等平台服务
src/Juju.Tools.Json/      JSON 文档、元数据、设置与文件监控
src/Juju.Tools.Toto/      Toto 领域模型、CSV/JSON 存储和提醒调度
editor/                   Monaco 编辑器前端与 C#/WebView2 消息协议
installer/                Inno Setup 安装包定义
tests/                    JSON 与 Toto 自动化测试
```



## 待优项

1. toto 工具所有显示窗口都无法进行文本复制，需要让详情页面的文本可以进行复制操作
2. toto 工具新增事项的快捷语法增加一种方案 @h20m30
3. 主题颜色目前只有浅色，没有深色