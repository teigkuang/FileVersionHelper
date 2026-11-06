import json
from pathlib import Path
from PIL import Image

src = Path(r"C:\Users\Lenovo\Documents\xwechat_files\wxid_kyqu6shlxm4h21_c183\temp\RWTemp\2026-11\9e20f478899dc29eb19741386f9343c8\61a497a53f21020e2e09498a5fbc58aa.jpg")
with Image.open(src) as im:
    print(json.dumps({"width": im.width, "height": im.height, "mode": im.mode}))
