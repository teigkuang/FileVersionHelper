import json
import subprocess
from pathlib import Path
from PIL import Image

exe = Path(r"E:\FileEditVersionHelper\app\src\FvHelper.Desktop\bin\Release\net10.0-windows\win-x64\publish\FvHelper.exe")
out = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build\publish-exe-icon.png")
ps = (
    "Add-Type -AssemblyName System.Drawing;"
    "$i=[System.Drawing.Icon]::ExtractAssociatedIcon('" + str(exe) + "');"
    "$b=$i.ToBitmap();"
    "$b.Save('" + str(out) + "',[System.Drawing.Imaging.ImageFormat]::Png);"
    "Write-Output ($b.Width.ToString() + 'x' + $b.Height.ToString())"
)
res = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True)
print(res.stdout.decode("utf-8", "replace").strip())
if out.exists():
    with Image.open(out) as im:
        print(json.dumps({"publish_exe_icon": str(out), "size": im.size}))
