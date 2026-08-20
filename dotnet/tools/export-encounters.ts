/**
 * Regenerates `dotnet/Assets/json/encounters.json` from the stats web's encounter seed.
 *
 * The web's seed is the single source of truth for which encounters it will accept: `normalizeEncounter`
 * only returns a verified mapping for a mobCode that appears there, and an unverified one is answered
 * `400 unsupported_encounter`. The meter ships a copy so it can gate uploads BEFORE spending a request,
 * and so it can label a boss with its difficulty/stage offline.
 *
 * Run it from the stats web repo (it needs that repo's node_modules for tsx):
 *
 *   cd <stats-web-repo>
 *   npx tsx <this-file> src/shared/encounters.ts <waffle_meter>/dotnet/Assets/json/encounters.json
 *
 * It fails loudly on a duplicate mobCode — the meter's lookup assumes one encounter per code.
 */
import { writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { resolve } from "node:path";

type BossSeed = { index: number; name: string };
type VariantSeed = {
  label: string;
  dungeonId: number;
  difficulty: string | null;
  stage: string | null;
  mobCodes: number[];
  mobCodeAliases?: Array<{ mobCode: number; bossIndex: number }>;
};
type DungeonSeed = {
  key: string;
  category: string;
  name: string;
  variantType: string;
  bosses: BossSeed[];
  variants: VariantSeed[];
};

const CATEGORY_ORDINAL: Record<string, number> = { "원정": 1, "초월": 2, "성역": 3 };

const [, , seedPath, outPath] = process.argv;

if (!seedPath || !outPath) {
  console.error("usage: tsx export-encounters.ts <encounters.ts> <out.json>");
  process.exit(2);
}

// Wrapped in main() rather than using top-level await: this file lives outside the stats web package, so
// esbuild transforms it as CJS where top-level await is a hard error.
async function main() {
const seed = (await import(pathToFileURL(resolve(seedPath!)).href)) as {
  encounterDungeons: DungeonSeed[];
};

const dungeons = seed.encounterDungeons.map((dungeon) => {
  const categoryOrd = CATEGORY_ORDINAL[dungeon.category];

  if (!categoryOrd) {
    throw new Error(`unknown category "${dungeon.category}" on ${dungeon.key}`);
  }

  return {
    key: dungeon.key,
    category: dungeon.category,
    categoryOrd,
    name: dungeon.name,
    variantType: dungeon.variantType,
    bosses: dungeon.bosses.map((boss) => ({ index: boss.index, name: boss.name })),
    variants: dungeon.variants
      .map((variant) => ({
      label: variant.label,
      dungeonId: variant.dungeonId,
      difficulty: variant.difficulty,
      stage: variant.stage,
      // [mobCode, bossIndex] pairs — primary seeds in boss order, then the alias codes.
      // Tolerate both an empty array and an absent field — the meter's build runs this script, and a
      // crash here blocks it. Variants that end up with no codes are dropped below.
      mobs: [
        ...(variant.mobCodes ?? []).flatMap((mobCode, i) => {
          const boss = dungeon.bosses[i];

          // mobCodes pair with bosses BY POSITION. A variant listing more codes than the dungeon has bosses
          // would silently lose the extras — and a code missing from the catalog is a battle the meter's
          // upload gate refuses while the server would have taken it. Loud, like the duplicate check.
          if (mobCode !== undefined && !boss) {
            throw new Error(
              `${dungeon.key} / ${variant.label}: mobCode ${mobCode} at index ${i} has no boss ` +
                `(dungeon declares ${dungeon.bosses.length}). Use mobCodeAliases for extra codes.`,
            );
          }

          return mobCode === undefined || !boss ? [] : [[mobCode, boss.index]];
        }),
        ...(variant.mobCodeAliases ?? []).map((alias) => [alias.mobCode, alias.bossIndex]),
      ],
      }))
      // A code-less variant cannot be reached through this file. The shipped catalogue is a
      // mobCode -> variant lookup, and the seed's one code-less variant on purpose — 시련's top
      // difficulty bucket, whose levels all share the pooled 시련 variant's three codes — is chosen
      // from the upload payload's affix block instead. `TierArtifact.cs` says the same from the other
      // side: that variant "cannot appear in mobs — that map is 1:1", and `OverlayViewModel` builds
      // "시련 16단계" from the parsed affixes rather than from a catalogue label.
      //
      // Emitting it anyway breaks two shipped guards at once: `Every_variant_carries_at_least_one_mob`
      // and — because it reuses the pooled variant's dungeonId — `No_dungeon_id_belongs_to_two_variants`.
      // The catalogue committed before this label existed simply lacked it, so dropping it here is what
      // the meter has always shipped, not a change to it.
      .filter((variant) => variant.mobs.length > 0),
  };
});

const codes = new Set<number>();
const duplicates: string[] = [];

for (const dungeon of dungeons) {
  for (const variant of dungeon.variants) {
    for (const [mobCode] of variant.mobs) {
      if (codes.has(mobCode)) {
        duplicates.push(`${mobCode} (${dungeon.name} / ${variant.label})`);
      }

      codes.add(mobCode);
    }
  }
}

if (duplicates.length > 0) {
  throw new Error(`duplicate mobCode(s): ${duplicates.join(", ")}`);
}

writeFileSync(
  outPath,
  JSON.stringify(
    {
      _comment:
        "Supported-encounter catalog: mobCode -> (dungeon, variant, boss). Mirrors the stats web's encounter " +
        "seed — regenerate with dotnet/tools/export-encounters.ts whenever that catalog changes. A battle whose " +
        "boss mobCode is absent here is never uploaded; the web answers 400 unsupported_encounter for it.",
      schemaVersion: 1,
      dungeons,
    },
    null,
    1,
  ) + "\n",
  "utf8",
);

console.log(`${dungeons.length} dungeons, ${codes.size} mobCodes -> ${outPath}`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
