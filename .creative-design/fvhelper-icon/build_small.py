import json
from pathlib import Path
from PIL import Image, ImageDraw

build = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build")
master = Image.open(build / "A-master-1024.png").convert("RGBA")

BLUE = (59, 114, 246, 255)
TILE = (245, 246, 248, 255)
PAPER = (255, 255, 255, 255)

def simplified(size: int) -> Image.Image:
    """小尺寸简化版：只保留「一张纸 + 一只眼」两个形，去掉横线与放射线。"""
    ss = 8  # 超采样倍数
    n = size * ss
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # 圆角底
    r = int(n * 0.22)
    d.rounded_rectangle([0, 0, n - 1, n - 1], radius=r, fill=TILE)

    # 纸张：略微倾斜以保留「两张纸错位」的动势
    pw, ph = int(n * 0.50), int(n * 0.60)
    px, py = int(n * 0.19), int(n * 0.24)
    stroke = max(ss, int(n * 0.062)) if size < 32 else max(ss, int(n * 0.045))
    d.rounded_rectangle([px, py, px + pw, py + ph], radius=int(n * 0.05),
                        fill=PAPER, outline=BLUE, width=stroke)
    # 纸内两条文本行（16px 时省略）
    if size >= 32:
        line_w = int(pw * 0.55)
        for i, dy in enumerate((0.22, 0.36)):
            y = py + int(ph * dy)
            d.rounded_rectangle([px + int(pw * 0.18), y, px + int(pw * 0.18) + line_w, y + max(ss * 1.5, int(n * 0.016))],
                                radius=max(ss, int(n * 0.008)), fill=BLUE)

    # 眼睛：实心蓝杏仁 + 白瞳中的蓝点（缩小到 16px 时就是一个可读的蓝点）
    cx, cy = int(n * 0.66), int(n * 0.22)
    ew, eh = int(n * 0.30), int(n * 0.17)
    d.ellipse([cx - ew // 2, cy - eh // 2, cx + ew // 2, cy + eh // 2], fill=BLUE)
    d.ellipse([cx - int(ew * 0.20), cy - int(ew * 0.20), cx + int(ew * 0.20), cy + int(ew * 0.20)], fill=PAPER)
    d.ellipse([cx - int(ew * 0.10), cy - int(ew * 0.10), cx + int(ew * 0.10), cy + int(ew * 0.10)], fill=BLUE)

    return im.resize((size, size), Image.LANCZOS)

sizes = [256, 128, 64, 48, 32, 16]
small_dir = build / "sizes"
small_dir.mkdir(exist_ok=True)
variants = {}
for s in sizes:
    if s <= 32:
        img = simplified(s)
        kind = "simplified"
    else:
        img = master.resize((s, s), Image.LANCZOS)
        kind = "artwork"
    img.save(small_dir / f"icon-{s}.png")
    variants[s] = (img, kind)

ico = build / "FvHelper.ico"
variants[256][0].save(ico, format="ICO", sizes=[(s, s) for s in sizes],
                      append_images=[variants[s][0] for s in sizes if s != 256])
print(json.dumps({"ico": str(ico), "ico_bytes": ico.stat().st_size,
                  "sizes": sizes,
                  "kinds": {str(k): v[1] for k, v in variants.items()},
                  "notes": "32/16 用简化重绘版，其余由 1024 母版等比缩放"}, ensure_ascii=False))

with Image.open(ico) as im:
    print(json.dumps({"ico_contained_sizes": sorted(im.info.get("sizes", []))}, ensure_ascii=False))
