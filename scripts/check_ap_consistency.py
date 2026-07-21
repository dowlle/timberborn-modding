#!/usr/bin/env python3
"""Cross-repo consistency checks between the Unity mod and the Python APWorld.

Two failure modes have shipped to players so far, both silent:

  1. BLOCKED-WITHOUT-ITEM. A building listed in the mod's ApBuildingLocations
     gets its science cost set to int.MaxValue by VanillaUnlockBlocker. If the
     APWorld has no matching item, nothing ever unlocks it and the building is
     permanently unbuildable. This cost a live async player their Wonder goal
     (v0.0.5.2 hotfix) and later turned out to affect Sluice too.

  2. UNRESOLVED TEMPLATE ID. VanillaUnlockBlocker looks buildings up by internal
     template id. If a game update renames one, the lookup misses and the
     building silently stays unlocked -- free to build, never routed through AP.
     Timberborn 1.1 renames four and removes one, breaking several entries.

Note these are DIFFERENT defects needing different checks. A startup warning for
unresolved ids does not catch case 1, because there the template resolves fine
and the miss is on the APWorld side.

Exit code 0 = all checks pass, 1 = at least one failure.

Usage:
    python scripts/check_ap_consistency.py
    python scripts/check_ap_consistency.py --blueprints <dir-or-zip>
    python scripts/check_ap_consistency.py --items <path-to>/worlds/timberborn/Items.py

This script needs BOTH halves of the project: the C# side from this repo, and
Items.py from the APWorld repo (https://github.com/dowlle/TimberbornArchipelago).
It finds the APWorld automatically if that repo is checked out next to this one,
under either its own name or as an `ArchipelagoWorld` submodule. Otherwise pass
--items.

--blueprints enables check 2. Accepts either the Unity importer output dir
(Assets/Tools/ImportedAssets/Editor/Resources/Buildings) or the game's
StreamingAssets/Modding/Blueprints.zip, which is readable directly and needs no
Unity import.
"""

import argparse
import os
import re
import sys
import zipfile

MOD_REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_BLOCKER = os.path.join(
    MOD_REPO, "Assets", "Mods", "Archipelago", "ApBuildingLocations.cs")

# Where the APWorld repo might be relative to this one. The first entry is the
# combined-repo layout (both halves as submodules of a parent); the rest cover a
# plain side-by-side checkout.
APWORLD_CANDIDATES = (
    os.path.join(MOD_REPO, os.pardir, "ArchipelagoWorld"),
    os.path.join(MOD_REPO, os.pardir, "TimberbornArchipelago"),
    os.path.join(MOD_REPO, os.pardir, "timberborn-archipelago"),
)
ITEMS_RELATIVE = os.path.join("worlds", "timberborn", "Items.py")

# Buildings deliberately blocked with no AP item. VanillaUnlockBlocker skips
# these explicitly so they keep their vanilla science cost. Adding to this list
# is a design decision -- it must be matched by a skip in VanillaUnlockBlocker,
# or the building becomes unbuildable.
INTENTIONALLY_BLOCKED_WITHOUT_ITEM = {
    "Earth Recultivator",   # FT wonder, unlocked via vanilla science
    "Earth Repopulator",    # IT wonder, unlocked via vanilla science
}

ENTRY_RE = re.compile(
    r'\{\s*"([A-Za-z0-9_]+)\.(Folktails|IronTeeth)"\s*,\s*"([^"]+)"\s*\}')
ITEM_RE = re.compile(r'\(\s*"([^"]+)"\s*,\s*ItemClassification')


def find_items_py():
    """Locate the APWorld's Items.py, or return None if it is not beside us."""
    for candidate in APWORLD_CANDIDATES:
        path = os.path.join(candidate, ITEMS_RELATIVE)
        if os.path.isfile(path):
            return os.path.normpath(path)
    return None


def read_blocker_entries(path):
    with open(path, encoding="utf-8") as fh:
        return ENTRY_RE.findall(fh.read())


def read_apworld_items(path):
    with open(path, encoding="utf-8") as fh:
        return set(ITEM_RE.findall(fh.read()))


def read_blueprint_ids(path):
    """Return the set of 'Template.Faction' ids found in a blueprint source."""
    ids = set()
    if zipfile.is_zipfile(path):
        names = zipfile.ZipFile(path).namelist()
    else:
        names = []
        for root, _, files in os.walk(path):
            names.extend(files)
    for name in names:
        base = os.path.basename(name)
        if base.endswith(".blueprint.json"):
            stem = base[: -len(".blueprint.json")]
            if "." in stem:
                ids.add(stem)
    return ids


def check_blocked_without_item(entries, items):
    offenders = sorted({
        display for _, _, display in entries
        if display not in items and display not in INTENTIONALLY_BLOCKED_WITHOUT_ITEM
    })
    if offenders:
        print("FAIL: blocked by the mod but no AP item exists to grant them.")
        print("      These are permanently unbuildable in every seed.")
        for name in offenders:
            print("        - %s" % name)
        print("      Fix: remove the ApBuildingLocations entry, add an APWorld")
        print("      item, or add an explicit VanillaUnlockBlocker skip and list")
        print("      it in INTENTIONALLY_BLOCKED_WITHOUT_ITEM.")
        return False
    print("OK  : every blocked building has a matching AP item "
          "(%d entries, %d intentional exclusions)."
          % (len(entries), len(INTENTIONALLY_BLOCKED_WITHOUT_ITEM)))
    return True


def check_templates_resolve(entries, blueprint_ids):
    missing = sorted({
        "%s.%s" % (template, faction)
        for template, faction, _ in entries
        if "%s.%s" % (template, faction) not in blueprint_ids
    })
    if missing:
        print("FAIL: blocker entries that do not resolve against the blueprints.")
        print("      Each stays unlocked and free to build, bypassing AP entirely.")
        for name in missing:
            print("        - %s" % name)
        print("      Fix: update the template id, or drop the entry if the")
        print("      building was removed from the game.")
        return False
    print("OK  : all %d blocker template ids resolve against the blueprints."
          % len(entries))
    return True


def main():
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--blueprints", metavar="PATH",
                        help="blueprint directory or Blueprints.zip; enables the "
                             "template-resolution check")
    parser.add_argument("--items", metavar="PATH", default=None,
                        help="path to the APWorld's worlds/timberborn/Items.py; "
                             "auto-detected if the APWorld repo sits beside this one")
    parser.add_argument("--blocker", metavar="PATH", default=DEFAULT_BLOCKER,
                        help="path to ApBuildingLocations.cs (defaults to this repo)")
    args = parser.parse_args()

    if not os.path.isfile(args.blocker):
        print("FAIL: could not find ApBuildingLocations.cs at %s" % args.blocker)
        print("      Pass --blocker if you are running from outside the mod repo.")
        return 1

    items_path = args.items or find_items_py()
    if items_path is None:
        print("FAIL: could not find the APWorld's Items.py.")
        print("      This check needs both halves of the project. Clone the")
        print("      APWorld next to this repo:")
        print("        git clone https://github.com/dowlle/TimberbornArchipelago")
        print("      or point at an existing copy with:")
        print("        --items <path-to>/worlds/timberborn/Items.py")
        return 1
    if not os.path.isfile(items_path):
        print("FAIL: --items path does not exist: %s" % items_path)
        return 1

    entries = read_blocker_entries(args.blocker)
    items = read_apworld_items(items_path)
    if not entries or not items:
        print("FAIL: could not parse the mod or APWorld sources; check paths.")
        print("      blocker: %s" % args.blocker)
        print("      items  : %s" % items_path)
        return 1

    ok = check_blocked_without_item(entries, items)

    if args.blueprints:
        if not os.path.exists(args.blueprints):
            print("FAIL: --blueprints path does not exist: %s" % args.blueprints)
            ok = False
        else:
            ok = check_templates_resolve(
                entries, read_blueprint_ids(args.blueprints)) and ok
    else:
        print("SKIP: template-resolution check (pass --blueprints to enable).")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
