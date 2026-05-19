export interface SkillMeta {
  code: number;
  job: string;
  name?: string;
  isStigma: boolean;
}

export interface GroupedJobSkills {
  job: string;
  normalSkills: number[];
  stigmaSkills: number[];
}

export const JOB_PREFIX_MAP: Record<string, number> = {
  "검성": 11,
  "수호성": 12,
  "살성": 13,
  "궁성": 14,
  "마도성": 15,
  "정령성": 16,
  "치유성": 17,
  "호법성": 18,
};

export const GROUPED_BY_JOB: GroupedJobSkills[] = [
  {
    job: "검성",
    normalSkills: [11250000, 11400000],
    stigmaSkills: [11800000],
  },
  {
    job: "수호성",
    normalSkills: [12120000],
    stigmaSkills: [12780000],
  },
  {
    job: "살성",
    normalSkills: [],
    stigmaSkills: [],
  },
  {
    job: "궁성",
    normalSkills: [14220000, 14310000],
    stigmaSkills: [],
  },
  {
    job: "마도성",
    normalSkills: [15110000, 15320000],
    stigmaSkills: [],
  },
  {
    job: "정령성",
    normalSkills: [16150000, 16220000],
    stigmaSkills: [16370000],
  },
  {
    job: "치유성",
    normalSkills: [16140340, 17070000, 17080000, 17090000, 17160000, 17400000, 17420000],
    stigmaSkills: [1739000, 1741000, 17780000],
  },
  {
    job: "호법성",
    normalSkills: [18080000],
    stigmaSkills: [18780000],
  },
];

const flattenSkills = () =>
  GROUPED_BY_JOB.flatMap(({ job, normalSkills, stigmaSkills }) => [
    ...normalSkills.map((code) => ({ code, job, isStigma: false })),
    ...stigmaSkills.map((code) => ({ code, job, isStigma: true })),
  ]);

const SKILLS: SkillMeta[] = flattenSkills();

export const SKILL_MAP = new Map<number, SkillMeta>(SKILLS.map((skill) => [skill.code, skill]));

export const SKILL_ORDER_MAP = new Map<number, number>(
  SKILLS.map((skill, index) => [skill.code, index]),
);

export const DEFAULT_VISIBLE_SKILL_CODES = SKILLS.map((skill) => skill.code);

export const getSkillName = (code: number) => SKILL_MAP.get(code)?.name;
