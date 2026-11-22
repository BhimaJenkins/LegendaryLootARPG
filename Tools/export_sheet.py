"""
Export the design Sheet to the mod's data files.

    python export_sheet.py "Affix Master List.xlsx" [output_folder]

Reads the "Affix list" and "Uniques" tabs (columns are found by their header names, so moving columns
is fine) and writes affixes.yml and uniques.yml. Copy them to:
    - LegendaryLootARPG/Data/Defaults/   (built into the mod as defaults), and/or
    - BepInEx/config/LegendaryLootARPG/  (live files; the server reloads them automatically)

Only rows with Keep? = Y become affixes. "Unique only" rows belong on the Uniques tab.
Needs: pip install openpyxl
"""
import re
import sys
from pathlib import Path

import openpyxl

TIERS = ["Uncommon", "Magic", "Rare", "Epic", "Legendary"]
TIER_HEADERS = ["Uncommon (Green)", "Magic (Blue)", "Rare (Yellow)", "Epic (Purple)", "Legendary (Orange)"]
WEAPONS = ["OneHanded", "TwoHanded", "TwoHandedLeft", "Bow", "Crossbow", "Staff", "Torch"]

# Words people write in the Sheet -> item categories (what the item is)
CATEGORY_WORDS = {
    "onehandedweapon": ["OneHanded"], "onehandedweapons": ["OneHanded"], "one-handedweapons": ["OneHanded"],
    "twohandedweapon": ["TwoHanded"], "twohandedweapons": ["TwoHanded", "TwoHandedLeft"],
    "two-handedweapons": ["TwoHanded", "TwoHandedLeft"],
    "one-andtwo-handedweapons": ["OneHanded", "TwoHanded", "TwoHandedLeft"],
    "twohandedweaponleft": ["TwoHandedLeft"],
    "bow": ["Bow", "Crossbow"], "bows": ["Bow"], "crossbow": ["Crossbow"], "crossbows": ["Crossbow"],
    "torch": ["Torch"], "shield": ["Shield"], "shields": ["Shield"],
    "helmet": ["Helmet"], "helmets": ["Helmet"], "helm": ["Helmet"], "head": ["Helmet"],
    "chest": ["Chest"], "legs": ["Legs"],
    "shoulder": ["Shoulder"], "cape": ["Shoulder"], "capes": ["Shoulder"], "cloak": ["Shoulder"],
    "utility": ["Utility"], "trinket": ["Trinket"], "trinkets": ["Trinket"],
    "staff": ["Staff"], "staffs": ["Staff"],
    "weapon": WEAPONS, "weapons": WEAPONS,
}
# Words -> skills (what the item trains)
SKILL_WORDS = {
    "swords": "Swords", "sword": "Swords", "knives": "Knives", "knife": "Knives", "daggers": "Knives",
    "dagger": "Knives", "dualknives": "Knives", "clubs": "Clubs", "club": "Clubs", "mace": "Clubs",
    "maces": "Clubs", "polearms": "Polearms", "atgeir": "Polearms", "atgeirs": "Polearms",
    "spears": "Spears", "spear": "Spears", "axes": "Axes", "axe": "Axes", "fists": "Unarmed",
    "fist": "Unarmed", "unarmed": "Unarmed", "bloodmagic": "BloodMagic", "elementalmagic": "ElementalMagic",
    "blocking": "Blocking", "pickaxes": "Pickaxes",
}
# In the skill column, "Bows" means the Bows skill (in the item-type column it means the item type)
RANGED_SKILLS = {"bows": "Bows", "bow": "Bows", "crossbows": "Crossbows", "crossbow": "Crossbows"}
# Phrases that need both a category AND a skill
COMPOUND_WORDS = {
    "one-handedmace": "OneHanded+Clubs", "one-handedclubs": "OneHanded+Clubs",
    "one-handedswords": "OneHanded+Swords", "one-handedaxes": "OneHanded+Axes",
    "two-handedaxes": "TwoHanded+Axes", "two-handedclubs": "TwoHanded+Clubs",
    "elementalstaffs": "Staff+ElementalMagic", "bloodstaffs": "Staff+BloodMagic",
}
IGNORED_WORDS = {"tool", "fishingpole", "all", "anyweaponorarmor", ""}

# Extra checks the Sheet can't express (affix id -> requirements, see AffixRoller.MeetsRequirements)
REQUIRES = {"FrostDamageAOE": ["frostDamage"], "RemoveSpeedPenalty": ["movementPenalty"]}

BIOME_NAMES = {"any": "Meadows", "meadows": "Meadows", "blackforest": "BlackForest", "swamp": "Swamp",
               "mountains": "Mountain", "mountain": "Mountain", "plains": "Plains", "mistlands": "Mistlands",
               "ashlands": "AshLands", "deepnorth": "DeepNorth"}


def norm(word):
    return re.sub(r"[\s_]", "", str(word).strip().lower())


def parse_targets(text, warnings, row_label):
    """Returns (allowedOn, allowedSkills) from an 'allowed item types' cell."""
    allowed_on, allowed_skills = [], []
    if text is None:
        return allowed_on, allowed_skills
    for raw in re.split(r"[,/]", str(text)):
        w = norm(raw)
        if w in IGNORED_WORDS:
            continue
        if w in COMPOUND_WORDS:
            allowed_on.append(COMPOUND_WORDS[w])
        elif w in CATEGORY_WORDS:
            allowed_on.extend(CATEGORY_WORDS[w])
        elif w in SKILL_WORDS:
            allowed_skills.append(SKILL_WORDS[w])
        else:
            warnings.append(f"{row_label}: don't know the item type '{raw.strip()}'")
    return sorted(set(allowed_on), key=allowed_on.index), sorted(set(allowed_skills))


def parse_skills(text, warnings, row_label):
    out = []
    if text is None:
        return out
    for raw in re.split(r"[,/]", str(text)):
        w = norm(raw)
        if not w:
            continue
        if w in SKILL_WORDS:
            out.append(SKILL_WORDS[w])
        elif w in RANGED_SKILLS:
            out.append(RANGED_SKILLS[w])
        else:
            warnings.append(f"{row_label}: don't know the skill '{raw.strip()}'")
    return sorted(set(out))


def parse_value(cell):
    """'1 to 3' -> [1, 3]; 5 -> [5, 5]; 'On' -> 'on'; NA/blank -> None; anything else -> 'text'."""
    if cell is None:
        return None
    if isinstance(cell, (int, float)):
        return [float(cell), float(cell)]
    s = str(cell).strip()
    if s.lower() in ("", "na", "n/a"):
        return None
    if s.lower() == "on":
        return "on"
    m = re.fullmatch(r"(-?[\d.]+)\s*(?:to|-)\s*(-?[\d.]+)", s)
    if m:
        return [float(m.group(1)), float(m.group(2))]
    try:
        v = float(s)
        return [v, v]
    except ValueError:
        return "text"


def q(s):
    """Quote a string for YAML."""
    s = "" if s is None else str(s).replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ")
    return f'"{s}"'


def num(v):
    return str(int(v)) if float(v).is_integer() else f"{v:g}"


def ylist(items):
    return "[" + ", ".join(q(i) for i in items) + "]"


def header_map(ws):
    return {str(c.value).strip(): i for i, c in enumerate(ws[1]) if c.value is not None}


def export_affixes(ws, warnings):
    h = header_map(ws)
    col = lambda name: h.get(name)
    need = ["Affix", "Tooltip line", "Keep?", "Internal ID"]
    for n in need:
        if col(n) is None:
            sys.exit(f"Affix list is missing the column '{n}'")
    types_col = next((h[k] for k in h if k.startswith("Allowed item types")), None)
    skills_col = next((h[k] for k in h if k.startswith("Allowed skill types")), None)

    out = ["# Generated from the Sheet by Tools/export_sheet.py. Edit the Sheet, then export again.",
           "# Each affix: tooltip (X = rolled value), where it can roll, and its value range per tier.", ""]
    count = 0
    for r, row in enumerate(ws.iter_rows(min_row=2, values_only=True), start=2):
        name = row[col("Affix")]
        keep = row[col("Keep?")]
        aid = row[col("Internal ID")]
        if not name or str(keep).strip().upper() != "Y":
            continue
        label = f"Row {r} ({name})"
        if not aid:
            warnings.append(f"{label}: no Internal ID, skipped")
            continue
        allowed_on, allowed_skills = parse_targets(row[types_col] if types_col is not None else None, warnings, label)
        required = parse_skills(row[skills_col] if skills_col is not None else None, warnings, label)
        values, toggle = {}, False
        for tier, header in zip(TIERS, TIER_HEADERS):
            if header not in h:
                continue
            v = parse_value(row[h[header]])
            if v is None:
                continue
            if v == "on":
                toggle = True
                values[tier] = [1.0, 1.0]
            elif v == "text":
                warnings.append(f"{label}: {tier} value '{row[h[header]]}' isn't a number or range; that tier is skipped")
            else:
                values[tier] = v
        if not values:
            warnings.append(f"{label}: no tier has a value, skipped")
            continue
        tooltip = row[col("Tooltip line")] or name
        if not row[col("Tooltip line")]:
            warnings.append(f"{label}: no tooltip line, using the affix name")

        out.append(f"- id: {q(aid)}")
        out.append(f"  name: {q(name)}")
        out.append(f"  tooltip: {q(tooltip)}")
        if "Category" in h and row[h["Category"]]:
            out.append(f"  group: {q(row[h['Category']])}")
        out.append(f"  allowedOn: {ylist(allowed_on)}")
        if allowed_skills:
            out.append(f"  allowedSkills: {ylist(allowed_skills)}")
        if required:
            out.append(f"  requiredSkills: {ylist(required)}")
        if aid in REQUIRES:
            out.append(f"  requires: {ylist(REQUIRES[aid])}")
        if toggle:
            out.append("  toggle: true")
        out.append("  values:")
        for tier in TIERS:
            if tier in values:
                lo, hi = values[tier]
                out.append(f"    {tier}: [{num(lo)}, {num(hi)}]")
        out.append("")
        count += 1
    return "\n".join(out), count


def export_uniques(ws, warnings):
    h = header_map(ws)
    for n in ["Unique name", "Allowed on (item types)", "What the Unique affix does"]:
        if n not in h:
            sys.exit(f"Uniques tab is missing the column '{n}'")
    out = ["# Generated from the Sheet by Tools/export_sheet.py. Edit the Sheet, then export again.",
           "# A Unique only drops once its power is coded (or with 'Unfinished Uniques (testing)' on).", ""]
    count = 0
    for r, row in enumerate(ws.iter_rows(min_row=2, values_only=True), start=2):
        name = row[h["Unique name"]]
        if not name:
            continue
        label = f"Uniques row {r} ({name})"
        uid = re.sub(r"[^A-Za-z0-9]", "", str(name))
        allowed_on, allowed_skills = parse_targets(row[h["Allowed on (item types)"]], warnings, label)
        status = str(row[h["Status"]]).strip() if "Status" in h and row[h["Status"]] else ""
        biome_raw = row[h["Minimum biome"]] if "Minimum biome" in h else None
        biome = BIOME_NAMES.get(norm(biome_raw or "any"))
        if biome is None:
            warnings.append(f"{label}: don't know the biome '{biome_raw}', using Meadows")
            biome = "Meadows"
        weight = row[h["Drop weight"]] if "Drop weight" in h and row[h["Drop weight"]] is not None else 1.0

        out.append(f"- id: {q(uid)}")
        out.append(f"  name: {q(name)}")
        out.append(f"  description: {q(row[h['What the Unique affix does']])}")
        out.append(f"  allowedOn: {ylist(allowed_on)}")
        if allowed_skills:
            out.append(f"  allowedSkills: {ylist(allowed_skills)}")
        out.append(f"  minBiome: {q(biome)}")
        out.append(f"  weight: {num(float(weight))}")
        if status.lower() == "cut":
            out.append("  enabled: false")
        out.append("")
        count += 1
    return "\n".join(out), count


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = Path(sys.argv[1])
    dest = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(".")
    dest.mkdir(parents=True, exist_ok=True)
    wb = openpyxl.load_workbook(src, data_only=True)
    warnings = []
    affixes, n_aff = export_affixes(wb["Affix list"], warnings)
    uniques, n_uni = export_uniques(wb["Uniques"], warnings)
    (dest / "affixes.yml").write_text(affixes, encoding="utf-8")
    (dest / "uniques.yml").write_text(uniques, encoding="utf-8")
    print(f"Wrote {n_aff} affixes and {n_uni} Uniques to {dest.resolve()}")
    if warnings:
        print(f"\n{len(warnings)} things to check in the Sheet:")
        for w in warnings:
            print("  - " + w)


if __name__ == "__main__":
    main()
