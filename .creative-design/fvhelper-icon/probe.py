import json
from pathlib import Path
from PIL import Image

src = Path(r"E:\FileEditVersionHelper\phases\P4-导出增强与打包\图标\候选\方案A-叠页与眼睛.png")
with Image.open(src).convert("RGB") as im:
    w, h = im.size
    px = im.load()
    mid = h // 2
    scan = []
    for x in range(0, 400, 20):
        scan.append([x, px[x, mid]])
    print(json.dumps({
        "size": [w, h],
        "corner_px": [px[0, 0], px[w - 1, 0], px[0, h - 1]],
        "top_mid_scan": [[y, px[w // 2, y]] for y in range(0, 400, 20)],
        "left_mid_scan": scan,
        "colors": len(im.getcolors(maxcolors=1 << 24) or []),
    }, ensure_ascii=False))
