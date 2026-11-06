# releases — 发布产物

只放最终要发出去的东西，不放中间构建结果（中间产物在 `app\src\*\bin\`）。

## 现状

```
releases\
└─ v0.1-preview\
   └─ FvHelper.exe        可用预览版（59.17 MB，self-contained 单文件，已带图标）
                           SHA256 = 23A85837F8975AA71338BEFF59D84086C2914B1900462E14AC576A3EFB26BAE6
                           构建时间 2026-11-06 23:32:07
```

> **2026-11-06 23:32 重发（去右键菜单）**：左栏项目右键菜单取消 —— 「重命名」移到中栏项目标题旁的小按钮（也可 F2）；「在资源管理器中打开」由顶栏「打开文件目录」承担；「从列表移除」由「－」承担；「重新指定文件夹…」整条去掉（路径失效时改为"放回原位，或移除后重新添加"）。体积 62 048 915 B；SHA256 23A85837…BAE6。

> **2026-11-06 23:21 重发（已被 23:32 覆盖）**：新建/添加一个全新文件夹时不再误报"版本信息损坏"红条 —— 校验器现在区分"全新文件夹（没有 .fvhelper.dat 也没有 .bak）"与真正损坏，前者给灰条"这是全新文件夹：还没有版本信息，第一次提交时自动建立"、VerifyResult.IsNewRepo，提交照常可用（首次提交即建库 rev1）。测试 40/40。SHA256 B2458F0E…186C。

> **2026-11-06 23:05 重发（已被 23:21 覆盖）**：新增「打开文件目录」按钮（在"重新扫描"左边）、提交改为「提交新版本」确认弹窗（列出待提交文件 + 标题 + 详情两栏），提交详情页在导出条上方新增「版本详细信息」区（标题 / 详情 / 署名 / 时间 / 上一版号 / 改动统计 / 版本标识）。引擎 `CommitRecord` 新增 `detail` 字段（旧库读出来是空串，向后兼容）。编译 0 warning / 0 error，测试 39/39；体积 62 048 796 B（59.17 MB）。上一版 SHA256 `E481B503…BC4` 已作废。

> **2026-11-06 22:46 重发（已被 23:05 覆盖）**：这一版把**版本数据改成隐藏文件**（`.fvhelper.dat`/`.bak`/`.tmp` 与 `.fvhelper\` 都带 Windows `Hidden`，打开项目时也会给老库补打）、**导出目录名区分**（`_仅文件` / `_含版本信息` / `_含版本信息-新起点`）、**文案口径**（`导出（含数据信息文件）`→`导出（含版本信息）`、`作者`→`版本信息署名`、`changelist`→`提交记录`）。体积 62 041 012 B（59.17 MB），编译 0 warning / 0 error，测试 39/39。上一版 SHA256 `F0C5F630…3E24` 已作废（22:36 那次重发与这一版内容一致、二进制逐字节相同，只差最后几条源码注释，SHA256 未变）。
>
> 实测：`releases\v0.1-preview\FvHelper.exe` 双击实跑，顶栏"版本信息署名 张三"、右栏"提交记录"、详情态"导出（含版本信息）"均正常（截图 `phases\P4-导出增强与打包\验收截图\`）；CLI 在 `E:\test\note` 上跑三种导出，目录名与隐藏属性均符合预期。

> `v0.1-preview` 现在是**引擎已接线**的版本：项目列表（`＋` 选文件夹添加、记在 `%LOCALAPPDATA%\FVHelper\projects.json`）、选中项目自动扫描 + 切回窗口 2s 去抖自动扫 + 手动"重新扫描"、中栏文件表格（状态 修改/新增/删除/改名/无变化）、扫描到改动给淡黄提示条、填说明提交成一条提交记录（记录版本信息署名/时间/文件数/字节）、点提交记录某条 → 中栏显示该次提交详情并做单条校验、导出"仅文件"或"含版本信息"（可勾"作为新起点"）、版本信息署名等设置（存 `%LOCALAPPDATA%\FVHelper\settings.json`）、日志 `%LOCALAPPDATA%\FVHelper\logs\app.log`。
>
> 实测：`samples\论文样例-毕业论文` 打开即见 rev1/rev2；`%TEMP%` 测试库上完成 rev3～rev6 的提交；两个导出模式都跑通（仅文件导出不含任何 `.fvhelper*`，含数据导出 `verify --deep` 为 OK，勾"作为新起点"时是单条 rev1、所有文件记为 A）。窗口位置记忆、快捷键（Ctrl+O / F5 / Ctrl+E / Esc）已可用。
>
> 已知未做（P3 余项）：三栏可拖拽调宽/窄窗折叠、提交时变更勾选面板（现在是全部改动一起提交）、导出向导的进度页与"复制路径"、四态（OK/WARN/MISMATCH/CORRUPT）完整呈现（现在只显示单条提交的 OK/MISMATCH）、忽略规则等设置项、五态视图的 Loading/ReadOnly。

## 目标形态（v1.0）

```
releases\
├─ v1.0.0\
│  ├─ FvHelper-1.0.0-win-x64.exe    绿色版：单个 exe（.NET PublishSingleFile）
│  ├─ FvHelper-Setup-1.0.0.exe      安装包（Inno Setup，P4 再做）
│  ├─ 使用手册.pdf
│  └─ SHA256SUMS.txt                各文件校验值
└─ CHANGELOG.md
```

**绿色版不使用 7z SFX 自解压壳**：.NET 官方 `PublishSingleFile` 产出的就是真实单文件 exe，不是"解压器"。SFX 壳会被杀软判定为可疑安装器、且首启更慢（见 `docs\06` 第 3 节）。

## 发布检查表

- [ ] 版本号与 `app\Directory.Build.props` 的 `<Version>` 一致（P0 曾误写 `pubspec.yaml`，那是已废弃的 Flutter 方案）
- [ ] 绿色版单 exe ≤ 80MB（P1 实测 58.83 MB；当前带图标 + P3 交互版 59.17 MB）
- [ ] 在干净的 Windows 机器上跑 A1/A4/A5（`docs\04`）
- [ ] `SHA256SUMS.txt` 用 `Get-FileHash -Algorithm SHA256` 生成（见下）
- [ ] `CHANGELOG.md` 记录本次新增/修复/已知问题
- [ ] 手册写明"首次启动稍慢（约 1.3 s）、SmartScreen 提示如何解除锁定"

## 生成校验值

```powershell
Get-ChildItem -LiteralPath .\releases\v1.0.0 -File |
  ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name } |
  Set-Content -LiteralPath .\releases\v1.0.0\SHA256SUMS.txt -Encoding utf8
```
