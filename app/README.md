# app — 主线代码（.NET 解决方案）

| **当前状态**：**P1 骨架 + P2 引擎 + P3 界面（含全部交互余项）+ P4 图标已挂 + 版本数据隐藏 + 提交确认窗与版本详细信息区**（编译 0 Warning / 0 Error，测试 **40/40** 绿）。单文件发布 59.17 MB（SHA256 `B2458F0E…`）已验证；界面已全部由 `FvHelper.Core` 驱动（打开/扫描/打开文件目录/勾选提交/提交确认窗/提交记录/四态校验/两种导出）。全新文件夹按"还没建库"处理（灰条提示），首次提交自动建库。

## Desktop 界面模块（P3 产出）

| 文件 | 作用 |
|---|---|
| `MainWindow.xaml(.cs)` | 三栏（可拖拽）两态：项目列表 / 项目文件表格 ↔ 提交详情 / 提交记录；扫描、「打开文件目录」、勾选提交（提交前弹 `CommitWindow`）、两种导出、四态校验条、版本详细信息区、拖文件夹进窗口、重命名/重新指定、快捷键都在这里 |
| `PromptWindow.xaml(.cs)` | 通用单行输入（重命名项目） |
| `ExportDoneWindow.xaml(.cs)` | 导出完成页（模式+摘要+只读路径+复制路径/打开文件夹/完成） |
| `ProjectStore.cs` | 项目列表持久化 `%LOCALAPPDATA%\FVHelper\projects.json`（只存路径+显示名） |
| `UiStateStore.cs` | 窗口位置与大小 `%LOCALAPPDATA%\FVHelper\ui-state.json` |
| `AppSettings.cs` / `AuthorSettingsWindow` | 设置窗（作者名 / 大文件提示阈值 / 导出默认"作为新起点"，`settings.json`） |
| `ExportWindow.xaml(.cs)` | 含数据导出前的"作为新起点"勾选（默认值取自设置） |
| `CommitWindow.xaml(.cs)` | 「提交新版本」确认窗（640×600）：待提交文件清单（动作/文件/大小）+ 标题（必填）+ 详情（多行可空）；对外给 `CommitTitle` / `CommitDetail` |
| `AppLog.cs` / `AppPaths` | 界面日志 `%LOCALAPPDATA%\FVHelper\logs\app.log` 与目录约定 |
| `App.xaml` | 五个浅色样式放在 `Application.Resources`（弹窗也要用，不能放 `Window.Resources`） |

> 已删除 `FolderScanner.cs`：只读目录扫描由 Core 的 `WorkspaceScanner` 统一负责。

## Core 模块一览（P2 产出）

| 文件 | 作用 |
|---|---|
| `Hashing.cs` | SHA-256、前缀归一、流式哈希（共享读，被占用也可读） |
| `IndexModels.cs` | 索引模型（`IndexDocument`/`CommitRecord`/`ChangeRecord`/`TreeEntry`） |
| `DatFile.cs` | `.fvhelper.dat` 容器读写 + 完整性校验 |
| `AtomicFile.cs` | 原子替换 + `.bak`（只读重试、`File.Move` 分流） |
| `BlobStore.cs` | 内容库 `objects\aa\bb...`（边拷贝边哈希） |
| `PathSafety.cs` / `IgnoreRules.cs` | 路径安全 / glob 忽略规则 |
| `WorkspaceScanner.cs` / `RepoStateCache.cs` / `ChangeDetector.cs` | 快照 / 加速缓存 / A·M·D·R 识别 |
| `Repository.cs` | 门面：Open/Init/Status/Commit/History |
| `Verifier.cs` / `Exporter.cs` | 四层校验（`Verify` 全库 / `CheckCommit` 单次提交）/ 两种导出 |
| `VersionData.cs` | 版本数据可见性：`Hide(root)`（`.fvhelper.dat`/`.bak`/`.tmp`/`.fvhelper\` 打 Windows `Hidden`）、`Unhide(path)`（整文件重写前解除，绕开 `CREATE_ALWAYS` 拒绝覆盖隐藏文件的限制）、`IsHidden(root)` |

## CLI 用法（P2 起）

```powershell
$dn run --project src\FvHelper.Cli -- status   "E:\论文\毕业论文"
$dn run --project src\FvHelper.Cli -- commit   "E:\论文\毕业论文" --message="改结论" --author=黎广
$dn run --project src\FvHelper.Cli -- log      "E:\论文\毕业论文"
$dn run --project src\FvHelper.Cli -- export   "E:\论文\毕业论文" --rev=3 --to="D:\导出"
$dn run --project src\FvHelper.Cli -- verify   "E:\论文\毕业论文" --deep
```

## 目录结构（实际）

```
app\
├─ FvHelper.slnx                  .NET 10 新格式（注意：不是 .sln）
├─ Directory.Build.props          统一 LangVersion / Nullable / 版本号 / 中文资源
├─ src\
│  ├─ FvHelper.Core\              net10.0      类库：模型、编解码、内容库、扫描、提交、导出（无 UI 依赖）
│  ├─ FvHelper.Desktop\           net10.0-windows  WPF：Views(XAML) / ViewModels / Services
│  │  ├─ app.manifest             PerMonitorV2 + longPathAware + asInvoker
│  │  └─ FvHelper.Desktop.csproj  AssemblyName=FvHelper（产物叫 FvHelper.exe）
│  └─ FvHelper.Cli\               net10.0      控制台：init/status/commit/log/export/verify
└─ tests\
   └─ FvHelper.Core.Tests\        net10.0      xUnit
```

## 本机构建（SDK 不在系统 PATH，必须写全路径）

```powershell
$dn = "D:\IDE\dotnet\dotnet.exe"
cd E:\FileEditVersionHelper\app

& $dn build src\FvHelper.Desktop -c Release            # 编译
& $dn run   --project src\FvHelper.Desktop             # 开发期运行（框架依赖模式，靠 D:\IDE\dotnet 的运行时）
& $dn test  tests\FvHelper.Core.Tests                  # 跑测试
```

> ⚠️ 直接双击 `bin\Release\net10.0-windows\FvHelper.exe` 会弹"需安装 .NET 10"——那是**框架依赖**模式，系统目录只有 .NET 8。
> 想双击就用下面发布出来的 self-contained 单文件。

## 单文件发布（交付形态）

```powershell
& $dn publish src\FvHelper.Desktop -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

产物：`src\FvHelper.Desktop\bin\Release\net10.0-windows\win-x64\publish\FvHelper.exe`（**单文件 59.17 MB / 62 041 012 B**，无 dll；自带图标）。
实测首启 1330 ms / 二启 692 ms，详见 `..\phases\P1-技术验证\环境记录.md`。

图标：源文件 `src\FvHelper.Desktop\Assets\FvHelper.ico`（六档 256/128/64/48/32/16）。`FvHelper.Desktop.csproj` 里 `<ApplicationIcon>` 决定 exe 资源图标，`<Resource>` + `MainWindow.xaml` 的 `Icon="Assets/FvHelper.ico"` 决定标题栏图标；设计与取舍见 `..\phases\P4-导出增强与打包\图标\图标说明.md`。

自检通道（不需要图形界面即可验证引擎在单文件里也正常）：

```powershell
FvHelper.exe --scan "<某个项目文件夹>"     # 结果写 %TEMP%\fvhelper-scan.txt（repoId/head/commits/health/files/changes）
```

## 生成命令（P1 实际执行过的，供重建参考）

```powershell
& $dn new sln -n FvHelper                                   # 生成 FvHelper.slnx
& $dn new classlib -n FvHelper.Core   -o src\FvHelper.Core -f net10.0
& $dn new wpf      -n FvHelper.Desktop -o src\FvHelper.Desktop
& $dn new console  -n FvHelper.Cli    -o src\FvHelper.Cli -f net10.0
& $dn new xunit    -n FvHelper.Core.Tests -o tests\FvHelper.Core.Tests -f net10.0
& $dn sln FvHelper.slnx add src\FvHelper.Core\FvHelper.Core.csproj src\FvHelper.Desktop\FvHelper.Desktop.csproj src\FvHelper.Cli\FvHelper.Cli.csproj tests\FvHelper.Core.Tests\FvHelper.Core.Tests.csproj
```

坑：`dotnet sln FvHelper.sln add` 会报 `Could not find solution or directory`——.NET 10 生成的是 `.slnx`，要写 `FvHelper.slnx`。

## 硬规则

- `FvHelper.Core` **不得**引用 `FvHelper.Desktop` 或任何 UI 程序集；必须能被 `FvHelper.Cli` 独立驱动。
- 版本号只在 `Directory.Build.props` 里改，界面"关于"和产物文件名同源。
- 提交前跑：`dotnet build -warnaserror`（零警告）、`dotnet test`（全绿）。
- 构建产物（`bin\`、`obj\`、`publish\`）不进 git，也不进 `releases\`。
