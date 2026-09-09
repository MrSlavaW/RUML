# -*- coding: utf-8 -*-
"""
RUML Translations Validator and Quality Linter.
Verifies all translation files across Translations/<ModName>/<Language>/<Author>/
Checks for:
1. XML syntax and well-formedness
2. Duplicate tag conflicts (especially English fallback overwriting Russian)
3. English residual strings in Russian translations
4. Hediff stage key syntax
"""

import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
TRANSLATIONS_DIR = REPO_ROOT / "Translations"

def contains_cyrillic(text: str) -> bool:
    return any('\u0400' <= char <= '\u04FF' for char in text)

def is_likely_untranslated_english(text: str) -> bool:
    s = text.strip()
    if not s or len(s) < 12:
        return False
    if contains_cyrillic(s):
        return False
    # Ignore format strings and placeholders like {0}
    if re.match(r"^\{[0-9]+\}$", s):
        return False
    # Check if text contains at least two English words
    words = [w for w in re.split(r"[^a-zA-Z]", s) if len(w) > 2]
    return len(words) >= 2

def validate():
    print("==================================================")
    print("RUML Translations Quality Validator & Linter")
    print("==================================================")
    print(f"Scanning: {TRANSLATIONS_DIR}\n")

    total_mods = 0
    total_files = 0
    total_tags = 0

    critical_errors = []
    warnings = []
    notices = []

    stage_numeric_pattern = re.compile(r"^[^.]+\.stages\.[0-9]+\.(label|description|overrideLabel)$")

    for mod_dir in sorted(TRANSLATIONS_DIR.iterdir(), key=lambda d: d.name.lower()):
        if not mod_dir.is_dir() or mod_dir.name.startswith("."):
            continue
        total_mods += 1
        mod_name = mod_dir.name

        for lang_dir in sorted(mod_dir.iterdir(), key=lambda d: d.name.lower()):
            if not lang_dir.is_dir() or lang_dir.name.startswith("."):
                continue
            lang_name = lang_dir.name
            is_russian = (lang_name.lower() == "russian")

            for author_dir in sorted(lang_dir.iterdir(), key=lambda d: d.name.lower()):
                if not author_dir.is_dir() or author_dir.name.startswith("."):
                    continue
                author_name = author_dir.name

                seen_tags = {} # (category, tag) -> (file_path, value)

                for root, dirs, files in os.walk(author_dir):
                    for f in files:
                        if not f.endswith(".xml"):
                            continue
                        total_files += 1
                        file_path = Path(root) / f
                        rel_path = file_path.relative_to(author_dir)

                        # Determine category and DefType scope
                        parts = rel_path.parts
                        if len(parts) >= 2 and parts[0] == "DefInjected":
                            scope = ("DefInjected", parts[1])
                        elif len(parts) >= 1:
                            scope = (parts[0], "")
                        else:
                            scope = ("Root", "")

                        # 1. XML Parse Check
                        try:
                            tree = ET.parse(file_path)
                            root_elem = tree.getroot()
                        except Exception as ex:
                            critical_errors.append(f"[{mod_name}] XML Parse Error in {rel_path}: {ex}")
                            continue

                        for elem in root_elem:
                            tag = elem.tag
                            val = (elem.text or "").strip()
                            total_tags += 1
                            scoped_key = (scope, tag)

                            # 2. Duplicate Tag Check (scoped to same DefType or Keyed category)
                            if scoped_key in seen_tags:
                                prev_file, prev_val = seen_tags[scoped_key]
                                if is_russian:
                                    prev_ru = contains_cyrillic(prev_val)
                                    curr_ru = contains_cyrillic(val)
                                    if prev_ru and not curr_ru and is_likely_untranslated_english(val):
                                        critical_errors.append(
                                            f"[{mod_name}] CRITICAL CONFLICT on <{tag}> ({scope[1] or scope[0]}):\n"
                                            f"   Russian in: {prev_file}\n"
                                            f"   English fallback in: {rel_path} (would overwrite Russian in memory!)"
                                        )
                                    elif not prev_ru and curr_ru and is_likely_untranslated_english(prev_val):
                                        critical_errors.append(
                                            f"[{mod_name}] CRITICAL CONFLICT on <{tag}> ({scope[1] or scope[0]}):\n"
                                            f"   English fallback in: {prev_file}\n"
                                            f"   Russian in: {rel_path} (risk of silent overwrite!)"
                                        )
                                    elif val != prev_val:
                                        warnings.append(
                                            f"[{mod_name}] Duplicate tag <{tag}> in {scope[1] or scope[0]} with different values:\n"
                                            f"   File 1: {prev_file} -> \"{prev_val[:40]}\"\n"
                                            f"   File 2: {rel_path} -> \"{val[:40]}\""
                                        )
                            else:
                                seen_tags[scoped_key] = (str(rel_path), val)

                            # 3. Check for English Residuals in Russian translation
                            if is_russian and is_likely_untranslated_english(val):
                                warnings.append(
                                    f"[{mod_name}] Untranslated English string in {rel_path} -> <{tag}>: \"{val[:60]}\""
                                )

                            # 4. Check for Numeric Hediff Stages
                            if stage_numeric_pattern.match(tag):
                                notices.append(
                                    f"[{mod_name}] Numeric Hediff stage key in {rel_path} -> <{tag}> (RUML runtime sanitizer will alias this automatically)"
                                )

    print(f"Audit Results:")
    print(f"  Mods scanned: {total_mods}")
    print(f"  Files scanned: {total_files}")
    print(f"  Tags verified: {total_tags}")
    print(f"  Critical conflicts: {len(critical_errors)}")
    print(f"  Quality warnings: {len(warnings)}")
    print(f"  Notices: {len(notices)}")

    if notices:
        print("\n--- NOTICES (First 5) ---")
        for n in notices[:5]:
            print("  [INFO] " + n)
        if len(notices) > 5:
            print(f"  ... and {len(notices) - 5} more notices.")

    if warnings:
        print("\n--- WARNINGS (First 10) ---")
        for w in warnings[:10]:
            print("  [WARN] " + w)
        if len(warnings) > 10:
            print(f"  ... and {len(warnings) - 10} more warnings.")

    if critical_errors:
        print("\n--- CRITICAL ERRORS ---")
        for e in critical_errors:
            print("  [ERROR] " + e)
        print("\nFAILED: Critical translation integrity issues found.")
        return False

    print("\nSUCCESS: All translation files passed integrity checks!")
    return True

if __name__ == "__main__":
    success = validate()
    sys.exit(0 if success else 1)
