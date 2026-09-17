"""Create deterministic .meta files for new FlyBrain assets so that GUIDs are stable across copies of the project."""
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TARGETS = [ROOT / "Assets" / "FlyBrain", ROOT / "Assets" / "StreamingAssets", ROOT / "Assets" / "Scenes" / "FlyBrainDemo.unity"]

def guid(rel):
    return hashlib.md5(("flybrain:" + rel).encode()).hexdigest()

def meta_for(p, rel):
    g = guid(rel)
    if p.is_dir():
        return f"fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    ext = p.suffix.lower()
    if ext == ".cs":
        return f"fileFormatVersion: 2\nguid: {g}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if ext == ".asmdef":
        return f"fileFormatVersion: 2\nguid: {g}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if ext == ".shader":
        return f"fileFormatVersion: 2\nguid: {g}\nShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"fileFormatVersion: 2\nguid: {g}\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

created = 0
for t in TARGETS:
    if not t.exists():
        continue
    items = [t] + (sorted(t.rglob("*")) if t.is_dir() else [])
    for p in items:
        if p.suffix == ".meta":
            continue
        m = p.with_name(p.name + ".meta")
        if m.exists():
            continue
        rel = p.relative_to(ROOT).as_posix()
        m.write_bytes(meta_for(p, rel).encode("utf-8"))
        created += 1
print(f"created {created} meta files")
