"""Regenerate Assets/json/skill-shapes.json (the boss-mechanic AoE catalog) from a client datamine.

The client ships every mechanic's exact zone in Data/Table/SkillEffectFilter.dat. The join to a cast's
skill code is arithmetic: filter id = skillCode * 10 + index (a multi-stage mechanic has several rows —
e.g. 1806420 = Circle r700 then Rings 1400/2100/2800 = an expanding shockwave).

Prereq: dump the table with the CUE4Parse datamine spike (see memory/datamine-client-boss-codes):
    dotnet run -c Release -- dat SkillEffectFilter      # -> out/dat/SkillEffectFilter.json
Then:
    python skill-shapes-export.py <out/dat/SkillEffectFilter.json> [dotnet/Assets/json/skill-shapes.json]

Only MOB/BOSS skills (7-digit codes) and renderable zone types are kept — player skills (8-digit) and
Self/Single/Triangle/Cross rows would just bloat the shipped catalog.
"""

import json
import os
import sys
from collections import Counter

RENDERABLE = {"Circle", "Sphere", "Ring", "RingArc", "Arc", "Rectangle"}

# 7-digit codes are the mob/boss band (player skills are 8-digit, e.g. 17400058).
MOB_SKILL_MIN, MOB_SKILL_MAX = 1_000_000, 2_999_999


def export(src: str, dst: str) -> None:
    rows = json.load(open(src, encoding="utf-8"))["Properties"]["Data"]
    out: dict[str, list[dict]] = {}
    skipped = 0

    for r in rows:
        fid = r["ID"]["Value"]
        if fid < 10_000_000:
            continue  # generic low-id filters, not skill-derived
        code, idx = divmod(fid, 10)
        if not (MOB_SKILL_MIN <= code <= MOB_SKILL_MAX):
            continue
        # Only zones that can select a PLAYER are floor-damage 장판. The relationship flags are from the caster's
        # (boss's) frame — players are its enemies — so a real player-damage zone always has bRelationshipEnemy.
        # The rows with it false are mob heal/buff auras (Friendly), party buffs cast on PCs, and neutral markers
        # (huge radii up to 250m, no telegraph) that deal no player damage yet were rendering as bogus zones.
        if not r["bRelationshipEnemy"]:
            continue
        kind = r["EffectRangeType"].split("::")[1]
        if kind not in RENDERABLE:
            skipped += 1
            continue
        values = r["EffectRangeValues"]
        if len(values) < 5:
            continue
        # Where the zone sits: on the caster (boss's body), dropped at the target's location (a ground
        # puddle), or stuck to the target (a marker that follows them).
        anchor = (
            "targetLoc" if r["bRangeNoticeTargetLocation"]
            else "target" if r["bEffectRangeNeedTarget"]
            else "caster"
        )
        out.setdefault(str(code), []).append({
            "i": idx,
            "t": kind,
            "v": [int(x) for x in values],
            "n": int(r["RangeNoticePreviousTime"]),  # telegraph pre-warning (ms)
            "a": anchor,
        })

    for zones in out.values():
        zones.sort(key=lambda z: z["i"])

    with open(dst, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))

    zones_n = sum(len(v) for v in out.values())
    print(f"skills={len(out)} zones={zones_n} skipped(non-zone)={skipped} bytes={os.path.getsize(dst)}")
    print("types:", Counter(z["t"] for v in out.values() for z in v).most_common())
    print("anchors:", Counter(z["a"] for v in out.values() for z in v).most_common())


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    default_dst = os.path.join(os.path.dirname(__file__), "..", "Assets", "json", "skill-shapes.json")
    export(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else os.path.normpath(default_dst))
