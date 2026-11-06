# samples — 样例数据

放测试与演示用的数据，**不放实现代码**。

## 已建立的样例（2026-11-06）

| 目录 | 内容 | 状态 |
|---|---|---|
| `论文样例-毕业论文\` | 一个**可用的真实仓库**：4 个文件（`毕业论文-正文.docx` / `正文\第二章-文献综述.md` / `正文\第三章-研究方法.md` / `参考文献.txt`），`.fvhelper.dat` + `.fvhelper\objects` 齐全，**rev 1（初稿）与 rev 2（补第三章+改结论）两次提交**，`verify --deep` = OK 4/4 | 已建，可直接用 CLI 或后续界面打开 |

`论文样例-毕业论文` 是 P2 端到端验证后留下的真实产物，可以用它试：

```powershell
$dn run --project app\src\FvHelper.Cli -- status "samples\论文样例-毕业论文"
$dn run --project app\src\FvHelper.Cli -- log    "samples\论文样例-毕业论文"
$dn run --project app\src\FvHelper.Cli -- export "samples\论文样例-毕业论文" --rev=2 --to="$env:TEMP\fv-out"
```

## 子目录规划（其余仍待建）

| 目录 | 内容 | 用途 |
|---|---|---|
| `论文样例-特殊路径\` | 含空格、中文标点、超长文件名、8 层嵌套 | P1 V2、P5 A6 |
| `损坏样例\` | 截断的 `.dat`、改字节的 `.dat`、缺 blob 的仓库 | P5 A8（P2 已在临时目录验证过，未留档） |
| `导出产物\` | 各阶段导出的结果（可随时删除重建） | 目视检查 |

## 注意

- 样例文件控制在总体积 20MB 以内（长路径与损坏样例用脚本生成，见 `..\tools\`）。
- 真实论文内容脱敏后再放，不提交隐私内容。
- 生成脚本与说明放 `..\tools\README.md` 里登记。
