import os
import sys
import json
import zipfile
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
TRANSLATIONS_DIR = REPO_ROOT / "Translations"
PACKS_DIR = REPO_ROOT / "packs"
MANIFEST_PATH = REPO_ROOT / "manifest.json"
CONFIG_PATH = REPO_ROOT / "repo_config.json"

def get_base_url():
    # 1. Check repo_config.json
    if CONFIG_PATH.exists():
        try:
            cfg = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            if "base_url" in cfg and cfg["base_url"]:
                return cfg["base_url"].rstrip("/")
        except Exception:
            pass

    # 2. Check GitHub Actions Environment Variables
    github_repo = os.environ.get("GITHUB_REPOSITORY")
    github_ref = os.environ.get("GITHUB_REF_NAME", "main")
    if github_repo:
        return f"https://raw.githubusercontent.com/{github_repo}/{github_ref}"

    # 3. Default fallback placeholder
    return "https://raw.githubusercontent.com/MrSlavaW/RUML/main"

def clean_id(s: str) -> str:
    return re.sub(r"[^a-zA-Z0-9_]", "_", s).strip("_").lower()

def build():
    print("==================================================")
    print("RUML Translations Builder and Manifest Generator")
    print("==================================================")
    print(f"Root: {REPO_ROOT}")
    print(f"Translations dir: {TRANSLATIONS_DIR}")

    if not TRANSLATIONS_DIR.exists():
        TRANSLATIONS_DIR.mkdir(parents=True, exist_ok=True)
        print(f"Created empty {TRANSLATIONS_DIR}")

    # 0. Pre-build Translation Quality & Integrity Validation
    from validate_translations import validate
    if not validate():
        print("\n[ERROR] Build aborted due to critical translation validation errors!")
        sys.exit(1)

    PACKS_DIR.mkdir(parents=True, exist_ok=True)
    base_url = get_base_url()
    print(f"Base download URL: {base_url}")

    manifest = []
    
    # Scan: Translations/<ModName>/<Language>/<Author>/
    mod_dirs = [d for d in TRANSLATIONS_DIR.iterdir() if d.is_dir() and not d.name.startswith(".")]
    print(f"Found {len(mod_dirs)} mod directories.")

    for mod_dir in sorted(mod_dirs, key=lambda d: d.name.lower()):
        mod_name = mod_dir.name
        lang_dirs = [l for l in mod_dir.iterdir() if l.is_dir() and not l.name.startswith(".")]

        for lang_dir in sorted(lang_dirs, key=lambda d: d.name.lower()):
            lang_name = lang_dir.name
            author_dirs = [a for a in lang_dir.iterdir() if a.is_dir() and not a.name.startswith(".")]

            for author_dir in sorted(author_dirs, key=lambda d: d.name.lower()):
                author_name = author_dir.name
                
                # Check for optional info.json
                info_path = author_dir / "info.json"
                info = {}
                if info_path.exists():
                    try:
                        info = json.loads(info_path.read_text(encoding="utf-8"))
                    except Exception as ex:
                        print(f"[WARN] Failed to parse {info_path}: {ex}")

                package_id = info.get("packageId", f"{clean_id(mod_name)}.{clean_id(author_name)}")
                version = info.get("version", "1.0.0")
                description = info.get("description", f"Перевод {mod_name} ({lang_name}) от {author_name}")
                
                # Unique identifier and archive filename
                entry_id = f"{clean_id(mod_name)}_{clean_id(lang_name)}_{clean_id(author_name)}"
                
                # Hierarchical archive path: packs/<ModName>/<Author>/<ModName>.zip
                mod_author_pack_dir = PACKS_DIR / mod_name / author_name
                mod_author_pack_dir.mkdir(parents=True, exist_ok=True)
                archive_filename = f"{mod_name}.zip".replace(" ", "_")
                archive_path = mod_author_pack_dir / archive_filename

                # Target folder in RimWorld RUML_Translations: <ModName>/<Author>
                target_folder = f"{mod_name}/{author_name}"

                print(f"Packing [{mod_name}] -> {lang_name} by {author_name} into {mod_name}/{author_name}/{archive_filename}...")

                # Pack into zip ensuring valid RimWorld Languages structure + info.json
                has_languages_dir = (author_dir / "Languages").exists()
                
                # Deterministic ZIP creation (fixed timestamp and normalized LF for identical MD5 on Windows and Linux)
                FIXED_ZIP_TIME = (2026, 1, 1, 0, 0, 0)
                TEXT_EXTS = {".xml", ".json", ".txt", ".md"}
                with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as zf:
                    for root, dirs, files in os.walk(author_dir):
                        for f in sorted(files):
                            if f.startswith("."):
                                continue
                            file_path = Path(root) / f
                            rel_path = file_path.relative_to(author_dir)

                            if f == "info.json":
                                arc_name = "info.json"
                            elif has_languages_dir:
                                arc_name = str(rel_path).replace("\\", "/")
                            else:
                                arc_name = f"Languages/{lang_name}/{str(rel_path).replace(chr(92), '/')}"

                            zinfo = zipfile.ZipInfo(arc_name, date_time=FIXED_ZIP_TIME)
                            zinfo.compress_type = zipfile.ZIP_DEFLATED
                            zinfo.external_attr = 0o644 << 16

                            # Deterministic line endings for text files across Windows (CRLF) and Linux (LF)
                            if file_path.suffix.lower() in TEXT_EXTS:
                                try:
                                    raw_text = file_path.read_text(encoding="utf-8")
                                    norm_text = raw_text.replace("\r\n", "\n").replace("\r", "\n")
                                    raw_bytes = norm_text.encode("utf-8")
                                except Exception:
                                    raw_bytes = file_path.read_bytes()
                            else:
                                raw_bytes = file_path.read_bytes()

                            zf.writestr(zinfo, raw_bytes)

                # Calculate archive MD5 hash
                import hashlib
                hasher = hashlib.md5()
                with open(archive_path, "rb") as zf_in:
                    while True:
                        chunk = zf_in.read(65536)
                        if not chunk: break
                        hasher.update(chunk)
                archive_hash = hasher.hexdigest()

                # Add to manifest
                rel_url = f"packs/{mod_name}/{author_name}/{archive_filename}".replace(" ", "%20")
                download_url = f"{base_url}/{rel_url}"
                manifest.append({
                    "id": entry_id,
                    "modName": mod_name,
                    "packageId": package_id,
                    "author": author_name,
                    "language": lang_name,
                    "version": version,
                    "hash": archive_hash,
                    "downloadUrl": download_url,
                    "description": description,
                    "modFolder": mod_name,
                    "authorFolder": author_name,
                    "targetFolder": target_folder
                })

    # Save manifest.json
    manifest_json_str = json.dumps(manifest, indent=2, ensure_ascii=False)
    MANIFEST_PATH.write_text(manifest_json_str, encoding="utf-8")
    print(f"\nManifest successfully generated: {MANIFEST_PATH} ({len(manifest)} items)")

if __name__ == "__main__":
    build()
