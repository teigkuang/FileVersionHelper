import json
import subprocess
from pathlib import Path
from PIL import Image

CLI = r"C:\Users\Lenovo\AppData\Local\Doubao\User Data\Default\sandbox_envs_dir\envs\c835bda2-e7bd-4b17-9c2b-36989783098a\override_dlcs\design-cli.exe"
WS = "cws_60988486083023874"
rel = Path(r"E:\FileEditVersionHelper\releases\v0.1-preview\FvHelper.exe")
out = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon\build\release-exe-icon.png")

ps = (
    "Add-Type -AssemblyName System.Drawing;"
    "$i=[System.Drawing.Icon]::ExtractAssociatedIcon('" + str(rel) + "');"
    "$b=$i.ToBitmap();"
    "$b.Save('" + str(out) + "',[System.Drawing.Imaging.ImageFormat]::Png);"
    "Write-Output ($b.Width.ToString() + 'x' + $b.Height.ToString())"
)
res = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True)
icon_info = {"extract_stdout": res.stdout.decode("utf-8", "replace").strip()}
if out.exists():
    with Image.open(out) as im:
        icon_info["release_icon_png"] = str(out)
        icon_info["size"] = list(im.size)
print(json.dumps(icon_info, ensure_ascii=False))

tree = subprocess.run([CLI, "design-workspace", "+tree", "--canvas-workspace-id", WS, "--path", "/"],
                      capture_output=True, timeout=120)
print(tree.stdout.decode("utf-8", "replace"))
print("exit", tree.returncode)
