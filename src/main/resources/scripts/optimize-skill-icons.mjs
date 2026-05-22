import { mkdir, readdir, writeFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = process.cwd();
const sourceDir = path.join(root, "src", "assets", "skill-icons");
const outputDir = path.join(root, "public", "skill-icons");
const manifestPath = path.join(root, "src", "generated", "skillIconManifest.ts");
const iconSize = 48;

await mkdir(outputDir, { recursive: true });
await mkdir(path.dirname(manifestPath), { recursive: true });

const files = (await readdir(sourceDir))
  .filter((file) => file.toLowerCase().endsWith(".png"))
  .sort((a, b) => a.localeCompare(b, "en", { numeric: true }));

const codes = [];

for (const file of files) {
  const code = path.basename(file, ".png");
  if (!/^\d+$/.test(code)) continue;

  codes.push(Number(code));
  await sharp(path.join(sourceDir, file))
    .resize(iconSize, iconSize, {
      fit: "contain",
      withoutEnlargement: true,
    })
    .webp({
      quality: 82,
      effort: 5,
    })
    .toFile(path.join(outputDir, `${code}.webp`));
}

const manifest = [
  "export const SKILL_ICON_CODES = new Set<number>([",
  ...codes.map((code) => `  ${code},`),
  "]);",
  "",
].join("\n");

await writeFile(manifestPath, manifest, "utf8");

console.log(`Optimized ${codes.length} skill icons to ${path.relative(root, outputDir)}`);
