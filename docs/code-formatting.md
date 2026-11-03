# C# 与 XAML 代码格式

本仓库使用两个独立的格式化器，并以根目录 `.editorconfig` 和 `Settings.XamlStyler` 为唯一规则来源。格式化范围限定在解决方案中的 C# 项目，以及 `src/`、`tests/` 下非生成的 XAML；`editor/`、`dist/`、`node_modules/`、`bin/`、`obj/` 均不属于格式化范围。

## 工具与版本

- 当前项目目标框架为 .NET 10；两个格式脚本要求 .NET SDK **10.0.400**。
- 没有添加 `global.json`，以免把现有 Release 工作流的 `10.0.x` SDK 选择限制到单一 feature band。
- C# 使用该 SDK 随附的 `dotnet format`，并读取现有 `.editorconfig`。不要求额外安装 IDE 或全局工具。
- XAML 使用本地 `xamlstyler.console` dotnet tool，版本固定为 **3.2501.8**，配置在根目录 `Settings.XamlStyler`。
- XAML 的检查使用 XAML Styler 的 passive 模式；检查失败时会列出不符合格式的文件，且不会写入这些文件。

## 新成员初始化

1. 安装 .NET SDK 10.0.400（Windows Desktop Runtime/SDK 支持 WPF 项目）。
2. 安装 Node.js 22.12+ 或 24+（应用构建会调用 `editor/` 的 npm 构建）。
3. 在仓库根目录运行：

   ```powershell
   dotnet tool restore --tool-manifest .config/dotnet-tools.json
   dotnet restore Juju.sln
   ```

PowerShell 5.1 或 PowerShell 7 均可运行仓库脚本。脚本自行定位仓库根目录，不依赖当前目录。

## 日常命令

在仓库根目录运行下列命令；从其他目录调用时，给脚本路径加上仓库的绝对路径即可（脚本会自行定位仓库根目录）：

```powershell
# 主动格式化解决方案 C# 与 src/tests 中的非生成 XAML
./scripts/format.ps1

# 只检查，不修改源码
./scripts/check-format.ps1

# 构建验证
dotnet build Juju.sln --configuration Release
```

`format.ps1` 会恢复本地工具和解决方案项目，然后执行 `dotnet format Juju.sln` 并逐个格式化范围内的 XAML。`check-format.ps1` 使用 `dotnet format Juju.sln --verify-no-changes --no-restore` 检查 C#，并使用 `xstyler --passive` 检查 XAML。原生命令的退出码会被脚本检查；工具错误或格式差异都会以非零状态结束。

如需单独恢复本地 XAML 工具：

```powershell
dotnet tool restore --tool-manifest .config/dotnet-tools.json
```

## IDE 兼容性

Rider、Visual Studio 和其他遵循 EditorConfig 的 IDE 都可读取本仓库 `.editorconfig`。C# 的统一检查器是 Roslyn `dotnet format`；Rider 自己还支持 `resharper_*` 专属规则，Roslyn 不会把这些扩展规则当作通用配置。如果 Rider 与 `dotnet format` 的结果不一致，应以 `scripts/check-format.ps1` 的结果定位冲突，并在审阅后协调规则；不要静默覆盖现有 `.editorconfig`。

Rider 默认读取 EditorConfig，并让其中的规则覆盖 IDE 对应的格式设置。检查/调整方式：

1. 打开 **Settings → Editor → Code Style**，确认 **Enable EditorConfig support** 已启用（Rider 默认启用）。
2. 使用 **Help → Find Action → Show Code Style Configuration** 查看当前文件应用了哪些 `.editorconfig` 规则及其来源。
3. 使用 **Code → Reformat Code**（Windows 默认 `Ctrl+Alt+L`）格式化 C#；避免把 Rider 的个人设置导出并覆盖仓库 `.editorconfig`。
4. Rider 内置 XAML 格式化不使用 `Settings.XamlStyler`。需要统一 XAML 时运行 `./scripts/format.ps1`，由仓库固定版本的 XAML Styler 处理。

IDE 的个人配置无需提交；项目格式约定由 `.editorconfig`、`Settings.XamlStyler` 和本地脚本共同保证。

格式化不参与构建。格式检查由开发者在本地运行，不会通过 GitHub Actions 在推送后执行；正常编译只检查代码是否可以构建。

## 常见问题

- **工具无法恢复：** 确认可以访问 NuGet，并运行 `dotnet tool restore --tool-manifest .config/dotnet-tools.json`。脚本不会依赖全局 XAML Styler。
- **SDK 版本不匹配：** 安装 .NET SDK 10.0.400；格式脚本会拒绝使用其他版本。
- **C# 检查报告 ENDOFLINE：** 检查工作树的行尾是否符合 `.editorconfig` / `.gitattributes`。检查脚本不会自动改写文件。
- **C# 检查报告 IMPORTS 或格式诊断：** 根据 `dotnet format` 输出中的文件与行号处理；此检查不会自动修复。
- **XAML 检查失败：** 脚本会打印失败的相对路径。运行 `./scripts/format.ps1` 主动格式化后再复查差异。
- **Rider 与本地检查结果不同：** 查看 Roslyn 支持的标准 EditorConfig 规则与 Rider 专属 `resharper_*` 规则；不要假定后者受 `dotnet format` 支持。

## 格式化范围与语义

格式化脚本只调用 C# 格式化器和 XAML Styler，不会格式化前端 `editor/` 工程。XAML Styler 的重排选项在 `Settings.XamlStyler` 中关闭；修改格式后仍应审阅 Git diff，确认绑定、资源引用和控件属性保持原意。被动检查不会更改源码。
