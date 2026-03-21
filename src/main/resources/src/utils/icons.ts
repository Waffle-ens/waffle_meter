const jobIconModules = import.meta.glob("../assets/*.png", {
  eager: true,
  import: "default",
}) as Record<string, string>;

const skillIconModules = import.meta.glob("../assets/skill-icons/*.png", {
  eager: true,
  import: "default",
}) as Record<string, string>;

export const getJobIconSrc = (job: string | undefined) => jobIconModules[`../assets/${job}.png`];

const normalizeSkillName = (name: string) => name.split("-")[0].trim();

const nameToFilename = (name: string) => name.replace(/[\\/:*?"<>|]/g, "_").replace(/\s+/g, "_");

export const getSkillIconSrc = (name: string | undefined) => {
  if (!name) return undefined;
  const normalized = normalizeSkillName(name);
  const filename = nameToFilename(normalized);
  return skillIconModules[`../assets/skill-icons/${filename}.png`];
};
