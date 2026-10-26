"""Download the public plugin API and HID references from a pinned official release.

For compilation only. These files are ignored by Git and excluded from the plugin
output; Fan Control supplies both assemblies at runtime.
"""
import hashlib
import io
from pathlib import Path
import urllib.request
import zipfile

URL = "https://github.com/Rem0o/FanControl.Releases/releases/download/V284/FanControl_284_net_10_0.zip"
SHA256 = "18d59a2d834ea956f99ab9292136a84e01bb436a68585bf2cb24a504a500fafa"
FILES = {
    "FanControl.Plugins.dll": "da248a490f10da3072e362d91badfdadcdcf1335bf4d551a7102eb0bf9634bc4",
    "HidSharp.dll": "79f2bc8ddff102e6dbede84b2c211a3aea2ed675bf762fa0f02ecb65f61ee812",
}


def main():
    target = Path(__file__).resolve().parents[1] / "lib"
    if all((target / name).exists() and hashlib.sha256((target / name).read_bytes()).hexdigest() == digest
           for name, digest in FILES.items()):
        print("Verified existing V284 reference assemblies.")
        return
    with urllib.request.urlopen(URL, timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != SHA256:
        raise RuntimeError("Fan Control archive SHA-256 mismatch")
    target.mkdir(exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for name, expected in FILES.items():
            matches = [entry for entry in archive.namelist() if Path(entry).name == name]
            if len(matches) != 1:
                raise RuntimeError(f"Expected one {name}, found {len(matches)}")
            content = archive.read(matches[0])
            if hashlib.sha256(content).hexdigest() != expected:
                raise RuntimeError(f"Reference SHA-256 mismatch: {name}")
            (target / name).write_bytes(content)
    print("Verified V284 reference assemblies. Do not redistribute lib/.")


if __name__ == "__main__":
    main()
