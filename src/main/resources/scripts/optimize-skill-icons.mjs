import { mkdir, readdir, unlink, writeFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = process.cwd();
const sourceDir = path.join(root, "src", "assets", "skill-icons");
const outputDir = path.join(root, "public", "skill-icons");
const manifestPath = path.join(root, "src", "generated", "skillIconManifest.ts");
const iconSize = 48;

await mkdir(outputDir, { recursive: true });
await mkdir(path.dirname(manifestPath), { recursive: true });

const existingOutputs = await readdir(outputDir).catch(() => []);
await Promise.all(
  existingOutputs
    .filter((file) => /\.(png|webp)$/i.test(file))
    .map((file) => unlink(path.join(outputDir, file))),
);

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
    .png({
      compressionLevel: 9,
      adaptiveFiltering: true,
      palette: true,
    })
    .toFile(path.join(outputDir, `${code}.png`));
}

const manifest = [
  "export const SKILL_ICON_CODES = new Set<number>([",
  ...codes.map((code) => `  ${code},`),
  "]);",
  "",
].join("\n");

await writeFile(manifestPath, manifest, "utf8");

console.log(`Optimized ${codes.length} skill icons to ${path.relative(root, outputDir)}`);
