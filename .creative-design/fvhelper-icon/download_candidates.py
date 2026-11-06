import io
import json
import shutil
import urllib.request
from pathlib import Path

base = Path(r"E:\FileEditVersionHelper\phases\P4-导出增强与打包\图标")
cand = base / "候选"
sketch = base / "草图"
cand.mkdir(parents=True, exist_ok=True)
sketch.mkdir(parents=True, exist_ok=True)

items = [
    ("https://aka.doubaocdn.com/s/cMnQc7ITl0", "方案A-叠页与眼睛.png"),
    ("https://aka.doubaocdn.com/s/abd8ZYsP9g", "方案B-眼睛在纸里.png"),
    ("https://aka.doubaocdn.com/s/6T4yfAUqpS", "方案C-折角页与眼啮合.png"),
]

report = []
for url, name in items:
    target = cand / name
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        data = resp.read()
    target.write_bytes(data)
    report.append({"file": str(target), "bytes": len(data)})

src_sketch = Path(r"C:\Users\Lenovo\Documents\xwechat_files\wxid_kyqu6shlxm4h21_c183\temp\RWTemp\2026-11\9e20f478899dc29eb19741386f9343c8\61a497a53f21020e2e09498a5fbc58aa.jpg")
dst_sketch = sketch / "图标草图-用户手绘.jpg"
if src_sketch.exists():
    shutil.copy2(src_sketch, dst_sketch)
    report.append({"file": str(dst_sketch), "bytes": dst_sketch.stat().st_size, "copied_from": str(src_sketch)})
else:
    report.append({"file": str(src_sketch), "error": "sketch not found"})

print(json.dumps(report, ensure_ascii=False, indent=1))
