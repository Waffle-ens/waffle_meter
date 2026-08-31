/**
 * Regenerates `dotnet/Assets/json/buff_values.json` from the stats web's buff-value table.
 *
 * The web's `src/shared/buff-values.ts` is what its own nDPS/rDPS model (`src/shared/dps-metrics.ts`)
 * reads to turn a buff's uptime into a damage gain. The meter ships a copy so it can compute the same
 * two numbers locally — live, for every participant, without waiting for an upload round-trip.
 *
 * Run it from the stats web repo (it needs that repo's node_modules for tsx):
 *
 *   cd <stats-web-repo>
 *   npx tsx <waffle_meter>/dotnet/tools/export-buff-values.ts src/shared/buff-values.ts \
 *          <waffle_meter>/dotnet/Assets/json/buff_values.json
 *
 * Only the fields the gain math actually reads are exported — `category` and `value`, keyed by buff code.
 * The Korean label and the `stat` enum are dropped: the meter never shows this table, it only multiplies
 * with it, and carrying them would double the shipped size for nothing.
 *
 * ⚠️ This table is a SNAPSHOT and it is NOT the authority for the party-synergy buffs. Those scale with
 * the caster's skill level, which the table has no room for — it holds one fixed number per buff code —
 * so 불패의 진언 at level 25 reads here as its level-1 value, 질풍의 권능's rank-5 code is missing outright,
 * and 흡혈의 검 has no entry at all. `PartySynergyCatalog` overrides those from the level the wire gives us
 * and wins wherever both have an opinion. Everything else (consumables, other classes' incidental buffs)
 * comes from here.
 */
import { writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { resolve } from "node:path";

type BuffValueEntry = {
  stat: string;
  ko: string;
  category: string;
  value: number;
};

const [, , seedPath, outPath] = process.argv;

if (!seedPath || !outPath) {
  console.error("usage: tsx export-buff-values.ts <buff-values.ts> <out.json>");
  process.exit(2);
}

// Wrapped in main() rather than using top-level await: this file lives outside the stats web package, so
// esbuild transforms it as CJS where top-level await is a hard error.
async function main() {
  const seed = (await import(pathToFileURL(resolve(seedPath!)).href)) as {
    buffValuesByCode: Readonly<Record<string, readonly BuffValueEntry[]>>;
  };

  const table = seed.buffValuesByCode;
  const out: Record<string, Array<{ c: string; v: number }>> = {};
  let entries = 0;

  for (const key of Object.keys(table).sort((a, b) => Number(a) - Number(b))) {
    const code = Number(key);

    if (!Number.isInteger(code) || code <= 0) {
      throw new Error(`non-numeric buff code key "${key}"`);
    }

    const values = table[key] ?? [];
    // Drop zero-valued rows: they multiply by 1 and only cost space. The web keeps them because its table
    // doubles as documentation of which stats a buff touches; the meter has no such use.
    const kept = values
      .filter((entry) => Number.isFinite(entry.value) && entry.value !== 0)
      .map((entry) => ({ c: entry.category, v: entry.value }));

    if (kept.length === 0) {
      continue;
    }

    out[key] = kept;
    entries += kept.length;
  }

  writeFileSync(outPath!, JSON.stringify(out, null, 1) + "\n", "utf8");
  console.log(
    `wrote ${outPath} — ${Object.keys(out).length} buff codes / ${entries} effects ` +
      `(from ${Object.keys(table).length} in the seed)`,
  );
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
