"""Hash updater inputs, independent of addon-only changes and checkout line endings."""
from hashlib import sha256
from pathlib import Path

root = Path(__file__).resolve().parents[1]

def source_hash():
    paths = [p for p in (root / "updater").rglob("*")
             if p.suffix in {".cs", ".csproj"} and not {"bin", "obj"}.intersection(p.parts)
             and "EvilTwinsUpdater.Tests" not in p.parts]
    paths += [root / "scripts" / "app_source_hash.py", root / "scripts" / "publish_updater.ps1"]
    digest = sha256()
    for path in sorted(paths):
        digest.update(path.relative_to(root).as_posix().encode())
        digest.update(b"\0")
        digest.update(path.read_bytes().replace(b"\r\n", b"\n"))
        digest.update(b"\0")
    return digest.hexdigest()

if __name__ == "__main__":
    print(source_hash())
