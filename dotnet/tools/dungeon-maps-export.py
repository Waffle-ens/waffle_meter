"""Regenerate Assets/Maps/* (the replay's dungeon backgrounds) from a client datamine.

Joins three datamined sources so every instanced dungeon we can fight in gets its real map:

  1. Data/Map/Dungeon/**/MapData.dat   -> which BOSSES spawn in a dungeon variant, and its SharingMap key
  2. Data/WorldMap/<Key>.dat           -> that map's world-coordinate anchor (StartWorldLocation + extent)
  3. UI/Map/WorldMap/**/<Key>/Res/*    -> the map art itself (tiles, stitched here)

Boss codes are EXPANDED across difficulty tiers: the client's spawn tables only list some tiers, but the
higher ones reuse the same room with a code that keeps the boss's ordinal (last 3 digits) and its name —
without this, a hard-mode pull matches no map. Any code that would then point at two different maps is
dropped rather than guessed.

Prereqs (CUE4Parse datamine spike — see memory/datamine-client-boss-codes):
    dotnet run -c Release -- mapdat Dungeon          # -> out/maps/dat/*.json  (spawns + anchors)
    dotnet run -c Release -- tex <Key>/Res/ ...      # -> out/tex/<Key>_<i>_<j>.png  (art)
Then:
    python dungeon-maps-export.py <datamine-out-dir> [repo-Assets/Maps]
"""

import json
import os
import re
import sys
from collections import defaultdict

from PIL import Image

# Dungeon maps are 2048px tiles; the replay zooms into the action, so keep native resolution but drop the
# alpha channel and quantize — the art is a dim background, not something to pixel-peep.
MAX_SIDE = 2048
QUANTIZE_COLORS = 128


def load_bosses(repo_json: str) -> dict[int, str]:
    mobs = json.load(open(os.path.join(repo_json, "mobs.json"), encoding="utf-8"))
    return {m["code"]: m["name"] for m in mobs if m.get("boss")}


def spawns_per_map(dat_dir: str, bosses: dict[int, str]) -> dict[str, set[int]]:
    """SharingMap key -> the boss codes the client spawns in it."""
    out: dict[str, set[int]] = defaultdict(set)
    for path in os.listdir(dat_dir):
        if not (path.startswith("Map_Dungeon_") and path.endswith("_MapData.json")):
            continue
        text = open(os.path.join(dat_dir, path), encoding="utf-8").read()
        link = re.search(r"/Game/BG/SharingMap/[^\"]*", text)
        if not link:
            continue
        key = link.group(0).rstrip("/").split("/")[-1]
        out[key] |= {int(c) for c in re.findall(r"\b(2\d{6})\b", text)} & bosses.keys()
    return out


def expand_tiers(spawned: dict[str, set[int]], bosses: dict[int, str]) -> dict[str, list[int]]:
    """Add the difficulty tiers the spawn tables omit: same boss name, same ordinal (last 3 digits).
    A code that lands in two maps is ambiguous — drop it instead of guessing."""
    by_name_ordinal: dict[tuple[str, str], set[int]] = defaultdict(set)
    for code, name in bosses.items():
        by_name_ordinal[(name, str(code)[-3:])].add(code)

    claims: dict[int, set[str]] = defaultdict(set)
    for key, codes in spawned.items():
        for code in codes:
            for sibling in by_name_ordinal[(bosses[code], str(code)[-3:])]:
                claims[sibling].add(key)

    result: dict[str, list[int]] = defaultdict(list)
    dropped = 0
    for code, keys in claims.items():
        if len(keys) != 1:
            dropped += 1
            continue
        result[next(iter(keys))].append(code)

    for codes in result.values():
        codes.sort()

    print(f"boss codes: {sum(len(v) for v in result.values())} mapped, {dropped} dropped as ambiguous")
    return result


def anchors(dat_dir: str) -> dict[str, dict]:
    """Map key -> its world rectangle, from the WorldMap .dat exports."""
    out = {}
    for path in os.listdir(dat_dir):
        if not (path.startswith("WorldMap_") and path.endswith(".json")):
            continue
        data = json.load(open(os.path.join(dat_dir, path), encoding="utf-8"))["Properties"]["Data"]
        start, sectors, size = data["StartWorldLocation"], data["SectorSize"], data["SectorWorldSize"]
        out[data["WorldMapDataTableKey"]] = {
            "worldMinX": start["X"],
            "worldMinY": start["Y"],
            "worldMaxX": start["X"] + sectors["X"] * size,
            "worldMaxY": start["Y"] + sectors["Y"] * size,
        }
    return out


def stitch(tex_dir: str, key: str, dest: str) -> tuple[int, int] | None:
    """Stitch <Key>_<i>_<j>.png tiles into one image (i = column, j = row). Masked / path-overlay variants
    are decoration for the in-game map UI — only the base tiles are the terrain."""
    rx = re.compile(rf"^{re.escape(key)}_(\d+)_(\d+)\.png$", re.IGNORECASE)
    tiles = []
    for name in os.listdir(tex_dir):
        m = rx.match(name)
        if m:
            tiles.append((int(m.group(1)), int(m.group(2)), os.path.join(tex_dir, name)))
    if not tiles:
        return None

    cols = max(t[0] for t in tiles) + 1
    rows = max(t[1] for t in tiles) + 1
    with Image.open(tiles[0][2]) as probe:
        tw, th = probe.size

    canvas = Image.new("RGB", (cols * tw, rows * th), (10, 12, 16))
    for i, j, path in tiles:
        with Image.open(path) as tile:
            canvas.paste(tile.convert("RGB"), (i * tw, j * th))

    if max(canvas.size) > MAX_SIDE:
        scale = MAX_SIDE / max(canvas.size)
        canvas = canvas.resize((int(canvas.width * scale), int(canvas.height * scale)), Image.LANCZOS)

    canvas.quantize(colors=QUANTIZE_COLORS, method=Image.MEDIANCUT).save(dest, optimize=True)
    return canvas.size


def main(out_dir: str, maps_dir: str) -> None:
    repo_json = os.path.normpath(os.path.join(maps_dir, "..", "json"))
    dat_dir = os.path.join(out_dir, "maps", "dat")
    tex_dir = os.path.join(out_dir, "tex")
    os.makedirs(maps_dir, exist_ok=True)

    bosses = load_bosses(repo_json)
    per_map = expand_tiers(spawns_per_map(dat_dir, bosses), bosses)
    anchor = anchors(dat_dir)
    names = json.load(open(os.path.join(out_dir, "maps", "_l10n-map-names.json"), encoding="utf-8"))

    manifest, skipped = [], []
    for key, codes in sorted(per_map.items()):
        if key not in anchor:
            skipped.append((key, "no world anchor"))
            continue
        size = stitch(tex_dir, key, os.path.join(maps_dir, f"{key}.png"))
        if size is None:
            skipped.append((key, "no map art extracted"))
            continue

        manifest.append({
            "key": key,
            "nameKo": names.get(key, key),
            "image": f"{key}.png",
            "imageWidth": size[0],
            "imageHeight": size[1],
            **anchor[key],
            "bossCodes": codes,
        })

    path = os.path.join(maps_dir, "map-manifest.json")
    with open(path, "w", encoding="utf-8-sig") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=4)

    total = sum(os.path.getsize(os.path.join(maps_dir, m["image"])) for m in manifest)
    print(f"maps: {len(manifest)} ({total / 1e6:.1f} MB), codes: {sum(len(m['bossCodes']) for m in manifest)}")
    for key, why in skipped:
        print(f"  [skip] {key}: {why}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    default_maps = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "Assets", "Maps"))
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else default_maps)
