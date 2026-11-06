import json
import subprocess

CLI = r"C:\Users\Lenovo\AppData\Local\Doubao\User Data\Default\sandbox_envs_dir\envs\c835bda2-e7bd-4b17-9c2b-36989783098a\override_dlcs\design-cli.exe"
WS = "cws_60988486083023874"

for path in ["/", "/generated-assets"]:
    res = subprocess.run([CLI, "design-workspace", "+ls", "--canvas-workspace-id", WS, "--path", path],
                         capture_output=True, timeout=120)
    print("== +ls", path, "exit", res.returncode)
    print(res.stdout.decode("utf-8", "replace"))
    if res.stderr:
        print("ERR", res.stderr.decode("utf-8", "replace")[:400])
