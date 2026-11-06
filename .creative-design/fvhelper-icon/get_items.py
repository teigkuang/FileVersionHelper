import json
import subprocess

CLI = r"C:\Users\Lenovo\AppData\Local\Doubao\User Data\Default\sandbox_envs_dir\envs\c835bda2-e7bd-4b17-9c2b-36989783098a\override_dlcs\design-cli.exe"
WS = "cws_60988486083023874"
ids = [
    "gen-v2-d5p7LMgBF2-stQu3nnx66alMO3AvttLDdQD8JDEO4mw",
    "gen-v2-HJurFiYeSXgdJPeRqeY6Hgyznmib2ivby7FlbZk9Uqo",
    "gen-v2-Mbd1pvK-9Fh_uHwk57iN99fugOLtU3B4ChwKuaNGDJA",
    "fMvdb9Yt-4VJu62bspO-0201",
]
argv = [CLI, "design-workspace", "+get", "--canvas-workspace-id", WS]
for i in ids:
    argv += ["--item-ids", i]
res = subprocess.run(argv, capture_output=True, timeout=120)
print("exit", res.returncode)
print(res.stdout.decode("utf-8", "replace"))
if res.stderr:
    print("ERR", res.stderr.decode("utf-8", "replace")[:400])
