import json
from pathlib import Path
from PIL import Image, ImageDraw

src = Path(r"E:\FileEditVersionHelper\phases\P4-导出增强与打包\图标\候选\方案A-叠页与眼睛.png")
out_dir = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build")
out_dir.mkdir(parents=True, exist_ok=True)

with Image.open(src).convert("RGB") as im:
    w, h = im.size
    px = im.load()

    # 找出中央浅色圆角方块的外接框：外圈是接近纯白，方块是 #F5F6F8 级别的浅灰
    def is_tile(x, y):
        r, g, b = px[x, y]
        # 外圈留白实测 252~254；图标方块内部实测 242~246
        return max(r, g, b) <= 249

    left, top, right, bottom = w, h, 0, 0
    for y in range(0, h, 4):
        for x in range(0, w, 4):
            if is_tile(x, y):
                left = min(left, x)
                top = min(top, y)
                right = max(right, x)
                bottom = max(bottom, y)

    bbox = (left, top, right + 1, bottom + 1)
    print(json.dumps({"src_size": [w, h], "tile_bbox": bbox,
                      "bbox_size": [bbox[2] - bbox[0], bbox[3] - bbox[1]],
                      "sample_tile_px": px[(left + right) // 2, top + 5],
                      "sample_outer_px": px[5, 5]}, ensure_ascii=False))

    cropped = im.crop(bbox)
    cropped.save(out_dir / "A-cropped.png")

    # 圆角蒙版：半径按实测方块比例给（约边长 22%）
    master = 1024
    resized = cropped.resize((master, master), Image.LANCZOS)
    radius = int(master * 0.22)
    mask = Image.new("L", (master, master), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, master - 1, master - 1], radius=radius, fill=255)
    rounded = Image.new("RGBA", (master, master), (0, 0, 0, 0))
    rounded.paste(resized.convert("RGBA"), (0, 0), mask)
    rounded.save(out_dir / "A-master-1024.png")

    sizes = [256, 128, 64, 48, 32, 16]
    small_dir = out_dir / "sizes"
    small_dir.mkdir(exist_ok=True)
    for s in sizes:
        rounded.resize((s, s), Image.LANCZOS).save(small_dir / f"icon-{s}.png")

    ico_path = out_dir / "FvHelper.ico"
    rounded.save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    print(json.dumps({"ico": str(ico_path), "ico_bytes": ico_path.stat().st_size,
                      "radius": radius}, ensure_ascii=False))

with Image.open(out_dir / "FvHelper.ico") as ico:
    print(json.dumps({"ico_sizes": sorted(ico.info.get("sizes", []))}, ensure_ascii=False))
