import json
import subprocess
from pathlib import Path

workdir = Path(__file__).resolve().parent
cli_entry = [r"C:\Users\Lenovo\AppData\Local\Doubao\User Data\Default\sandbox_envs_dir\envs\c835bda2-e7bd-4b17-9c2b-36989783098a\override_dlcs\design-cli.exe"]
workspace_id = "cws_60988486083023874"
xml_text = (workdir / "item-anchor.xml").read_text(encoding="utf-8-sig")
payload = xml_text.encode("utf-8")
if not xml_text.strip() or len(payload) > 1024 * 1024:
    raise SystemExit("XML empty or over 1MB")

argv = cli_entry + ["design-workspace", "+create", "--canvas-workspace-id", workspace_id, "--input", "-"]
result = subprocess.run(argv, input=payload, shell=False, cwd=workdir, capture_output=True, timeout=120)
(workdir / "anchor.stdout.bin").write_bytes(result.stdout)
(workdir / "anchor.stderr.bin").write_bytes(result.stderr)
(workdir / "anchor.exitcode.txt").write_text(str(result.returncode), encoding="utf-8")
if result.returncode != 0:
    raise SystemExit("CLI exit " + str(result.returncode))
response = json.loads(result.stdout.decode("utf-8", errors="strict"))
if not isinstance(response, dict) or response.get("code") != 0:
    raise SystemExit("response not ok")
print(response["xml_data"])
