"""Package an installable test ZIP without modifying a WoW installation."""
import json
import re
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ADDON = ROOT / "EvilTwinsCommandCenter"


def package():
    toc = ADDON / "EvilTwinsCommandCenter.toc"
    toc_text = toc.read_text(encoding="utf-8")
    version = re.search(r"^## Version: (\S+)\s*$", toc_text, re.MULTILINE).group(1)
    core_text = (ADDON / "Core.lua").read_text(encoding="utf-8")
    core_version = re.search(r'ET\.version = "([^"]+)"', core_text).group(1)
    if core_version != version:
        raise ValueError("Core.lua and the TOC have different addon versions.")
    files = [toc]
    for line in toc_text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        file = (ADDON / line).resolve()
        if not file.is_relative_to(ADDON.resolve()) or not file.is_file():
            raise ValueError(f"Invalid or missing TOC file: {line}")
        files.append(file)
    commit = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    dirty = bool(subprocess.check_output(
        ["git", "status", "--porcelain", "--untracked-files=normal"], cwd=ROOT, text=True
    ).strip())
    manifest = {"addon": ADDON.name, "version": version, "commit": commit, "dirty": dirty}
    output = ROOT / "dist" / f"{ADDON.name}-{version}.zip"
    output.parent.mkdir(exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file in files:
            archive.write(file, f"{ADDON.name}/{file.relative_to(ADDON).as_posix()}")
        archive.writestr(f"{ADDON.name}/build.json", json.dumps(manifest, indent=2) + "\n")
    with zipfile.ZipFile(output) as archive:
        if archive.testzip() is not None:
            raise ValueError("ZIP verification failed.")
    print(output)
    print(f"Version {version}, commit {commit[:8]}" + (" (uncommitted changes)" if dirty else ""))
    return output


if __name__ == "__main__":
    package()
