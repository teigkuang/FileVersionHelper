import json
import subprocess
from pathlib import Path
from PIL import Image

exes = [
    Path(r"E:\FileEditVersionHelper\app\src\FvHelper.Desktop\bin\Release\net10.0-windows\FvHelper.exe"),
]
out = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build\exe-icon.png")
ps = (
    "Add-Type -AssemblyName System.Drawing;"
    "$i=[System.Drawing.Icon]::ExtractAssociatedIcon('" + str(exes[0]) + "');"
    "$b=$i.ToBitmap();"
    "$b.Save('" + str(out) + "',[System.Drawing.Imaging.ImageFormat]::Png);"
    "Write-Output ($b.Width.ToString() + 'x' + $b.Height.ToString())"
)
cmd = ["powershell", "-NoProfile", "-Command", ps]
res = subprocess.run(cmd, capture_output=True)
(Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build\extract.log")).write_text(
    res.stdout.decode("utf-8", "replace") + "\n" + res.stderr.decode("utf-8", "replace"), encoding="utf-8")
if out.exists():
    with Image.open(out) as im:
        print(json.dumps({"extracted": str(out), "size": im.size}))
else:
    print("no icon extracted")
