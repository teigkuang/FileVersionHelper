# P2 核心引擎（进行中，2026-11-06 开工）

> 目标：把"一个论文文件夹 = 一个项目"的所有离线能力做实——读 `.fvhelper.dat`、识别改动、提交、导出、校验。
> 界面这层不动（仍是占位数据），P2 结束时引擎可被 CLI 与 WPF 共同调用。

## 1. 本阶段交付的代码（`app\src\FvHelper.Core\`）

| 文件 | 职责 |
|---|---|
| `Hashing.cs` | SHA-256；`sha256:` 前缀归一；流式文件哈希（`FileShare.ReadWrite\|Delete`，被 Word 占着也能读，I-04）；8 位短 id |
| `IndexModels.cs` | `IndexDocument` / `CommitRecord` / `CommitStats` / `ChangeRecord` / `TreeEntry`，schema `fvhelper.index/1` |
| `DatFile.cs` | 容器编解码：魔数 `FVH1` + version + flags(gzip/checksum) + payloadLength + gzip(JSON) + payload SHA-256 + `END!`；**逐项校验**，异常一律返回 `Corrupt` 不抛 |
| `AtomicFile.cs` | 原子替换：目标存在 `File.Replace(tmp,dat,dat.bak)`；不存在 `File.Move`；只读清属性重试一次（I-05 / P1 V5） |
| `BlobStore.cs` | 内容库 `objects\<hash前2>\<hash后62>`；边拷贝边算哈希（一次 IO 完成"内容+哈希"）；写临时分片 → rename；存在性/大小/全量哈希校验；tmp 清理 |
| `PathSafety.cs` | 相对路径规范化 + 越界拦截：拒绝绝对路径、盘符、`..`、保留字符、控制字符、段尾空格/点、符号链接（I-02，无 `\\?\`） |
| `IgnoreRules.cs` | glob 忽略规则（默认 `.fvhelper*` / `~$*` / `*.tmp` / `Thumbs.db` / `desktop.ini`，I-09）；含 `/` 的规则也匹配路径后缀段 |
| `WorkspaceScanner.cs` | 工作区快照（只统计 size/mtime，不算哈希） |
| `RepoStateCache.cs` | `.fvhelper\state.json` 加速筛子（**不是历史数据**，丢了自动退回全量哈希） |
| `ChangeDetector.cs` | A/M/D/R 识别；size+mtime 命中缓存则沿用旧哈希，否则真算哈希——"只打开没保存"不算修改；R 用"同哈希同大小、不同路径"推断 |
| `Repository.cs` | 门面：`Open`（坏主文件回退 `.bak`）/`Init`/`Status`/`Commit`（可部分提交）/`History`/`GetCommit` |
| `Verifier.cs` | 四层校验 L1 容器 / L2 索引自洽 / L3 内容库（**全部历史提交引用到的内容**）/ L4 工作区；四态 OK/WARN/MISMATCH/CORRUPT |
| `Exporter.cs` | 两种导出（仅文件 / 含数据信息）+ "作为新起点"；导出前预校验，缺一份就整体中止且不留半套 |

CLI：`app\src\FvHelper.Cli\Program.cs` — `init / status / commit / log / show / export / verify`。

## 2. 关键实现取舍（与文档/既有决定有出入的地方，必须记录）

1. **I-03 从"遇到占用就跳过"改成"能共享读就照常提交，只给提示"**。
   依据是 P1-V3 实测：Word 打开文档时 `FileShare.ReadWrite` 仍可读，算哈希没问题；只有在**连共享读都失败**（独占锁/无权限）时才中止并列出文件。
   理由：跳过会造成"用户以为提交了、其实少了一个文件"的隐性数据丢失，比中止更危险。
2. **`.fvhelper\state.json` 是新增的本地缓存**（`docs\03` 原未列）。
   纯性能用途，不进 `.dat`、不参与校验；删掉只会让下次扫描变慢，不会影响正确性。**部分提交后只缓存"工作区内容确实等于新树"的文件**（这条是测试逼出来的 bug：否则未提交的改动会被误判成"无变化"）。
3. **"作为新起点"导出时，那条提交的 changes 全部重写为 `A`**。
   否则新仓库的第一次提交会出现"修改一个不存在的文件"这种自相矛盾记录。
4. **提交前置校验**：提交前先跑一遍 L1–L3（不做工作区比对），MISMATCH/CORRUPT 直接拒绝，避免把坏状态往前推（D-13）。

## 3. 验证记录

### 3.1 自动化测试（`app\tests\FvHelper.Core.Tests`）

```
36 个用例全部通过（xunit）
行覆盖率 80.2%，分支覆盖率 74.1%（coverlet，报告 D:\IDE\coverage\...\coverage.cobertura.xml）
```

覆盖：容器往返/魔数/截断/改字节/尾标记/缺失文件；首提、A/M/D/R、只改 mtime 不算修改、忽略规则、部分提交、内容缺失禁提交、`.bak` 恢复、双损坏报错、两种导出、新起点、导出中止不留半套、四层校验四态、路径安全、被占用文件哈希、blob 路径分层。

### 3.2 端到端（真实目录，CLI 驱动）

| 场景 | 结果 |
|---|---|
| 三文件初提 | rev 1，A 3，内容库 3 个 blob，`.bak` 同步生成 |
| 改 1 / 增 1 / 删 1 / 改名 1 | 一次扫描准确产出 M/A/D/R 各 1，提交后状态立即回到"无改动" |
| 校验 `--deep` | `OK（4/4 层）` |
| 仅文件导出 rev1 | 3 个文件、无 `.fvhelper.dat`、无 `.fvhelper\`，内容是 rev1 当时内容 |
| 含数据导出 rev2 | 文件 + `.fvhelper.dat` + 历史 blob；打开后 `verify --deep` OK |
| **分享包继续编辑** | 在导出目录里改文件 → 提交 → **rev 3**，repoId 与原仓库相同（D-12 成立） |
| 作为新起点导出 rev1 | 单条 rev1、repoId 不同、verify OK |
| 主 `.dat` 截断（`.bak` 完好） | `verify` = CORRUPT 且提示可用备份；`status` 走备份恢复到 rev1 状态并给出提示 |
| 主文件与 `.bak` 双损坏 | `status` 明确报错"无法从备份恢复"，不静默 |
| 删掉一个 blob | `verify` = MISMATCH（并且发现的是**历史版本**的内容缺失）；提交被拒；导出在写任何文件前就中止，导出目录为空 |

### 3.3 性能基线（本机，1001 个文件：1000 × 1.4KB + 1 × 100MB）

| 动作 | 耗时 |
|---|---|
| 冷扫描（无缓存，全部算哈希） | **752 ms** |
| 首次提交（1001 文件 / 101MB，写内容库） | 11.7 s（优化前）→ 单文件路径改为流式后，单独改动 100MB 提交 **1.2 s** |
| 缓存命中扫描 | **324 ms** |
| 深度校验（含 100MB 全量哈希） | **886 ms** |
| `.dat` 体积 | 82.3 KB（1001 文件的全量 tree，gzip 后） |
| 内容库体积 | 102.2 MB（≈ 原文件总大小，未压缩） |

## 4. 尚未做（进 P3/P4）

- WPF 接引擎：项目加载、真实扫描、changelist、提交、导出、四态校验条（当前界面全是占位）
- 导出向导交互（选目录、模式、"作为新起点"位置）
- 内容库 GC（`docs\03` F-03）、`RenameConfidence` 字段（当前只做二元判定）
- 网络盘/同步盘提示（I-06 只保证不做跨盘写，提示文案未做）
- CLI 尚未接入 `FvHelper.slnx` 构建脚本；`tools\` 脚本仍空

## 5. 本阶段结论

- 引擎闭环成立：**扫描 → 识别 → 提交 → 历史 → 查看 → 导出 → 校验 → 分享后继续提交**，全部跑通并有测试与端到端证据。
- 没有发现需要改格式或改技术路线的问题；`.fvhelper.dat` v1 格式稳定，后续可加字段（JSON 向前兼容）。
- 数据安全底线已落地：写索引先自检可读回、原子替换 + `.bak`、损坏可回退、MISMATCH 时禁止提交与导出、导出失败不留半套。
