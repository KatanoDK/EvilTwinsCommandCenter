"""Check that addon-only changes do not force another Windows app download."""
import importlib.util
from pathlib import Path
import tempfile

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("app_source_hash", root / "scripts/app_source_hash.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
with tempfile.TemporaryDirectory() as directory:
    module.root = Path(directory)
    app = module.root / "updater/EvilTwinsUpdater"
    app.mkdir(parents=True)
    scripts = module.root / "scripts"
    scripts.mkdir()
    for name in ("app_source_hash.py", "publish_updater.ps1"):
        (scripts / name).write_text("fixture\n", encoding="utf-8")
    source = app / "App.cs"
    source.write_bytes(b"line one\nline two\n")
    before = module.source_hash()
    source.write_bytes(b"line one\r\nline two\r\n")
    assert module.source_hash() == before, "Checkout line endings changed the app hash"
    (module.root / "Addon.lua").write_text("changed addon", encoding="utf-8")
    generated = app / "obj"
    generated.mkdir()
    (generated / "Generated.cs").write_text("generated", encoding="utf-8")
    assert module.source_hash() == before, "Addon/generated files forced an app update"
    source.write_bytes(b"new app behavior\n")
    assert module.source_hash() != before, "A changed app would not publish an update"
print("PASS: app source changes publish; addon/generated files and checkout line endings retain the app build.")
