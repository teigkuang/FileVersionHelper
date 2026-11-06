import json
import subprocess
from pathlib import Path

CLI = r"C:\Users\Lenovo\AppData\Local\Doubao\User Data\Default\sandbox_envs_dir\envs\c835bda2-e7bd-4b17-9c2b-36989783098a\override_dlcs\design-cli.exe"
WS = "cws_60988486083023874"
wd = Path(r"E:\FileEditVersionHelper\.creative-design\fvhelper-icon")
payload = (wd / "batch.xml").read_text(encoding="utf-8-sig").encode("utf-8")
res = subprocess.run([CLI, "design-workspace", "+update-batch", "--canvas-workspace-id", WS, "--input", "-"],
                     input=payload, capture_output=True, timeout=120)
(wd / "update.stdout.bin").write_bytes(res.stdout)
print("exit", res.returncode)
print(res.stdout.decode("utf-8", "replace"))
if res.stderr:
    print("ERR", res.stderr.decode("utf-8", "replace")[:400])
