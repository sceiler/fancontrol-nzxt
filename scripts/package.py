"""Package only this plugin and its documentation, never the host's DLLs."""
import hashlib
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
plugin = root / "src/FanControl.NzxtHub/bin/Release/netstandard2.0/FanControl.NzxtHub.dll"
if not plugin.is_file():
    raise SystemExit("Run dotnet build -c Release first.")
dest = root / "dist"
dest.mkdir(exist_ok=True)
archive = dest / "FanControl.NzxtHub-0.1.1.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
    bundle.write(plugin, plugin.name)
    bundle.write(root / "README.md", "README.md")
    bundle.write(root / "docs/protocol.md", "docs/protocol.md")
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(dest / (archive.name + ".sha256")).write_text(f"{digest}  {archive.name}\n")
print(f"{archive}\nSHA-256: {digest}")
