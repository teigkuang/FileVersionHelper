# 文件版本管理器（FileEditVersionHelper / FVHelper）

给写论文的学生用的**本地版本管理工具**：一个目录 = 一个论文工程，历史版本不外置到云端，全部存在目录里的一个数据信息文件里；换电脑、拷 U 盘、发给导师，都能靠这个文件还原出任意一次提交时的文件。

## 一句话定位

> 用"打开目录 → 看 changelist → 导出某个版本"替代"论文_v1.docx、论文_v2_最终版_真最终.docx"。

## 核心能力（v1.0 目标）

| 编号 | 能力 | 说明 |
|---|---|---|
| C1 | 打开目录 | 选择任意论文目录，识别其中的数据信息文件（`.fvhelper.dat`） |
| C2 | 读取 changelist | 解析数据信息文件，列出全部提交记录（版本号、说明、作者、时间、文件数） |
| C3 | 查看提交详情 | 点开某条记录，看到该次提交涉及的文件清单、动作（新增/修改/删除/重命名）、大小 |
| C4 | 记录新版本 | 扫描目录变更，填写说明后提交，写入作者/时间/说明，生成新的 changelist |
| C5 | 导出历史版本 | 把某次提交时的文件还原到指定目录；可选"仅文件"或"文件 + 数据信息文件" |

## 平台与技术栈

- 首选 Windows 10 1809+ / Windows 11 x64。
- **C# + .NET 10 (LTS) + WPF**，发布为 **self-contained 单文件 exe**（绿色版：双击即用、不装运行时、不写注册表、不要管理员权限）。
- 交付顺序：**先出绿色单文件 MVP**，后续再做 Inno Setup 安装包。
- 数据不落云端、不联网、不上传：全部在用户选择的目录内。

## 当前状态

**P1 技术验证阶段已完成（2026-11-06）**：.NET 10 SDK（10.0.403）装在 `D:\IDE\dotnet`；`app\` 四项目解决方案已建并编译通过（0 警告 0 错误）；**单文件发布实测 58.83 MB，发布目录只有 1 个 `FvHelper.exe`**，首启 1330 ms / 二启 692 ms；六项专项验证（长路径 / 文件占用 / 哈希性能 / 原子替换 / 目录监听 / 目录选择）全部出结论，无硬伤，不回退 WinForms。

可双击试跑的产物：`releases\v0.1-preview\FvHelper.exe`（59.17 MB 单文件，**已是可用版**：加项目 → 自动扫描 → 勾选改动 → 提交 → 提交记录详情 → 导出，全部走真实引擎；已带应用图标）。SHA256 见 `releases\README.md`。

界面骨架（2026-11-06 第二版）：左栏**项目列表**（`＋` 选文件夹加项目）／中栏**两态**（项目文件视图 ↔ 某次提交详情）／右栏该项目的**提交记录**。规范与两态解读见 `phases\P3-桌面界面\界面稿\主界面布局规范-v0.2.md`，两张验收图见 `phases\P3-桌面界面\验收截图\`。

**P3 桌面界面（2026-11-06，交互项已收口）**：P2 引擎已接进界面——左栏项目列表（`＋` 选文件夹或直接拖文件夹进窗口，记在 `%LOCALAPPDATA%\FVHelper\projects.json`）／中栏两态（项目文件表格 ↔ 某次提交详情）／右栏该项目的提交记录。用户在线下改完文件回到软件即自动扫描并提示改动（选中项目自动扫 + 切回窗口 2 s 去抖 + 手动重扫），改动行带勾选框（默认全选，可全选/全不选），只提交勾选的文件；填说明提交成一条提交记录（记录版本信息署名/时间/文件数/大小），点条目可见该次提交详情（四态校验条、双击定位、复制清单），可导出"仅文件"或"含版本信息"（后者可勾"作为新起点"），导出完成页可复制路径/打开文件夹。

> **版本数据是隐藏文件**（第 4 组需求）：`.fvhelper.dat` / `.bak` / `.tmp` 与 `.fvhelper\` 一律带 Windows `Hidden` 属性，用户打开论文文件夹只看得到自己的文件；导出目录名直接标出 `_仅文件` / `_含版本信息` / `_含版本信息-新起点`。界面措辞统一为"版本信息""版本信息署名""提交记录"。

- 已实测：`samples\论文样例-毕业论文` 打开即见 rev1/rev2；`%TEMP%` 测试库完成 rev3→rev8 提交（rev8 验证了"只提交勾选的文件"）；仅文件导出不含任何 `.fvhelper*`；含版本信息导出 `verify --deep` OK；新起点导出是单条 rev1 且全为 A；窗口位置记忆与快捷键（Ctrl+O / F5 / Ctrl+E / Esc / F2）可用；三栏可拖拽、项目可重命名/重新指定、文件夹可直接拖进窗口。
- 验收截图与踩坑记录见 `phases\P3-桌面界面\README.md`；文案与隐藏文件改动的截图在 `phases\P4-导出增强与打包\验收截图\`。
- 测试：`dotnet test` **39/39**（新增"版本数据写完自动隐藏""导出目录名区分是否含版本信息"）。
- 余项：提交"可取消"（UI 未建 CTS）、忽略规则编辑（现只读）、ReadOnly 态、窄窗抽屉式折叠。

下一步 = **收口 P3 余项 → P4 安装包与关于页**。图标已完成（2026-11-06，叠页 + 眼睛，方案 A，六档 `.ico` 已挂到 exe 与窗口，说明见 `phases\P4-导出增强与打包\图标\图标说明.md`）。

**P2 核心引擎（2026-11-06 完成）**：14 个 Core 类 + CLI 七个子命令（`init/status/commit/log/show/export/verify`）。
- 已跑通真实闭环：扫描 → A/M/D/R 识别 → 提交（记录作者/说明/时间）→ 读历史 → 查看提交详情 → 两种导出 → 四层校验 → **导出包交给别人后可继续提交到 rev3**。
- 36 个自动化测试全绿，行覆盖率 80.2%；性能基线（1001 文件含 100MB）：冷扫描 752 ms / 缓存扫描 324 ms / 深度校验 886 ms。
- 细节见 `phases\P2-核心引擎\README.md`。

## 用 git 维护 / 换台电脑继续开发

仓库（`git@github.com:teigkuang/FileVersionHelper.git`）只放"人和 IDE 真正需要的东西"，构建产物与 59 MB 的 exe 不进仓库。规则写在根目录 `.gitignore` + `.gitattributes`：

**进仓库的（151 个文件、约 1.8 MB）**

| 内容 | 为什么 |
|---|---|
| `app\src\**`、`app\tests\**`、`app\FvHelper.slnx`、`app\Directory.Build.props`、`app\README.md` | 源码 + 解决方案，Visual Studio / VS Code 打开即能编译 |
| `docs\**` | 需求、数据格式、架构、路线图、风险 |
| `phases\**` | 各阶段记录、界面稿、验收截图（含图标候选图） |
| `tools\**` | 截图 / 造样例 / UI 冒烟脚本 |
| `samples\**`（含样例里的 `.fvhelper` 库数据） | 手上就有一份可复现的样例库，新机器不用重新造 |
| `releases\README.md` | 只留版本记录与 SHA256；exe 本身发 GitHub Releases |
| `.creative-design\**`（脚本 + 图标母版） | 图标可重制（`build_icon.py` / `build_small.py`） |
| `README.md`、`.gitignore`、`.gitattributes` | 工程说明与仓库规则 |

**不进仓库的**：`app\**\bin\`、`app\**\obj\`（约 208 MB）、`phases\**\spike\**\bin|obj`、`releases\**\*.exe`（59 MB）、`*.pdb`、`*.log`、`*.tmp`、`.vs\`、`.vscode\`（除少量放行的配置）、`%LOCALAPPDATA%\FVHelper\` 下的设置与项目列表。

**在新电脑上开工**

```powershell
git clone git@github.com:teigkuang/FileVersionHelper.git
# 装 .NET 10 SDK（10.0.x），装完 dotnet --version 应为 10.0.4xx
cd FileVersionHelper\app
dotnet build "FvHelper.slnx" -c Release      # 0 warning / 0 error
dotnet test "tests\FvHelper.Core.Tests" -c Release    # 40 个用例
# 出单文件 exe（产物在 src\FvHelper.Desktop\bin\Release\net10.0-windows\win-x64\publish\）
dotnet publish "src\FvHelper.Desktop" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

- **Visual Studio**：2022 17.12 及以上才认 .NET 10 与 `.slnx`（低版本请升级，或临时把 `.slnx` 换成 `.sln` 新格式仍可生成）。
- **VS Code**：装 C# Dev Kit 扩展（含 C# 扩展），打开 `app` 目录即可，`.vscode` 里放 `launch.json` 断点调试 WPF。
- 注意：git 不记录 Windows 的 `Hidden` 属性，`clone` 下来的 `samples` 库文件是普通文件；打开项目时程序会自动补打隐藏属性，不影响使用。

## 目录导航

```
E:\FileEditVersionHelper
├─ README.md                      本文件：项目概览与导航
├─ .gitignore / .gitattributes    仓库规则：什么进版本库、换行与二进制处理
├─ docs\                          全局文档（跨阶段长期有效）
│  ├─ 01-需求规格说明.md
│  ├─ 02-技术选型与架构.md        .NET 10 + WPF 的对比论证与工程结构
│  ├─ 03-数据格式规范-v0.1.md     .fvhelper.dat 容器 / 索引 / blob 库 / 导出
│  ├─ 04-开发路线图与阶段验收.md   P0–P5、里程碑、验收用例 A1–A10
│  ├─ 05-风险清单与待决问题.md     14 项风险 + 决策表
│  └─ 06-分发、监管与签名.md       exe 要不要审批、SmartScreen/杀软/签名
├─ app\                           主线代码：.NET 解决方案（P1 已建骨架 + 浅色三栏空壳）
│  ├─ FvHelper.slnx               解决方案文件（.NET 10 新格式）
│  ├─ Directory.Build.props       版本号 / 语言 / 中文资源统一配置
│  ├─ src\FvHelper.Core\          引擎（纯 .NET，无 UI 依赖）
│  ├─ src\FvHelper.Desktop\       WPF 界面（AssemblyName=FvHelper）
│  ├─ src\FvHelper.Cli\           命令行（自测与自动验收）
│  └─ tests\FvHelper.Core.Tests\  单元/集成测试
├─ phases\                        各阶段工作区（方案、试验、验收材料）
│  ├─ P0-立项与调研\  P1-技术验证\  P2-核心引擎\
│  ├─ P3-桌面界面\    P4-导出增强与打包\  P5-测试与发布\
├─ samples\                       样例数据：论文样例、特殊路径样例、损坏样例、导出产物
├─ tools\                         辅助脚本：造样例、造长路径、破坏索引、打包
└─ releases\                      发布产物：单文件 exe、安装包、手册、校验值
```

## 代码与阶段文件夹的分工

- **`app\` 是唯一的主线代码库**，随阶段增量开发，不按阶段复制多份代码。
- **`phases\Px-*\`** 放该阶段的过程材料：方案笔记、验证结论、界面稿、验收记录、缺陷清单。每个阶段目录的 `README.md` 写明目标、任务清单、交付物、退出标准、以及本阶段动了 `app\` 里哪些文件。
- 可复用脚本放 `tools\`，样例数据放 `samples\`，最终产物放 `releases\`。

## 术语表（全项目统一口径）

| 术语 | 含义 |
|---|---|
| 论文工程 / 仓库（repo） | 用户选中的一个目录，含文件 + `.fvhelper.dat` + `.fvhelper\` 内容库 |
| 数据信息文件 | `.fvhelper.dat`，版本索引（提交记录 + 文件清单指向）；**默认隐藏，用户看不到** |
| 内容库 | `.fvhelper\objects\`，按内容哈希存放的历史文件本体（目录同样隐藏） |
| 版本信息 | 界面用语：数据信息文件 + 内容库的统称（原"数据信息文件"） |
| changelist / 变更集 | 一次提交，对应一个版本号 `rev`；界面上叫"**提交记录**" |
| 版本信息署名 | 界面用语：记录在每次提交里的名字（原"作者"） |
| rev | 提交序号，从 1 递增 |
| 工作区 | 目录中用户可直接编辑的文件（除 `.fvhelper\` 与忽略项） |
| 版本树 / 快照 | 某次提交时目录内全部文件的清单（路径 + 哈希 + 大小） |
| blob | 内容库中一个按 SHA-256 命名的文件本体 |
| 导出 | 把某次提交的版本树还原成真实文件到目标目录 |
| 绿色单文件 | 单个 exe，双击即用，不安装、不写注册表、不需要运行时 |

## 文档约定

- 文档用中文，文件名带编号放 `docs\`；阶段材料放对应 `phases\Px-*\`。
- 数据格式以 `docs\03` 为唯一准绳：**先改文档，再改代码**。
- 每个阶段结束在阶段 `README.md` 记一行（日期 / 完成项 / 结论 / 遗留）。
- 决策变更记入 `docs\05` 决策表，不口头约定。
