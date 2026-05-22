export interface SkillChainGroup {
  mainCode: string;
  childCodes: string[];
}

export const SKILL_CHAIN_GROUPS: SkillChainGroup[] = [
  { mainCode: "11020000", childCodes: ["11030000", "11040000"] },
  { mainCode: "12010000", childCodes: ["12040000", "12020000", "12030000"] },
  { mainCode: "13010000", childCodes: ["13030000", "13040000"] },
  { mainCode: "14020000", childCodes: ["14340000"] },
  { mainCode: "15210000", childCodes: ["15040000", "15050000"] },
  { mainCode: "15010000", childCodes: ["15030000", "15250000"] },
  { mainCode: "16010000", childCodes: ["16040000", "16020000", "16030000"] },
  { mainCode: "17010000", childCodes: ["17020000", "17030000"] },
  { mainCode: "18010000", childCodes: ["18020000", "18030000"] },
];

export const normalizeChainCode = (code: string | number) => {
  const num = typeof code === "string" ? parseInt(code, 10) : code;
  if (!Number.isFinite(num) || num <= 0) return String(code);
  if (num >= 11_000_000 && num <= 19_999_999) {
    return String(Math.floor(num / 10_000) * 10_000);
  }
  return String(num);
};

const chainMainByCode = new Map<string, string>();

for (const group of SKILL_CHAIN_GROUPS) {
  chainMainByCode.set(group.mainCode, group.mainCode);
  for (const childCode of group.childCodes) {
    chainMainByCode.set(childCode, group.mainCode);
  }
}

export const getChainMainCode = (code: string | number) =>
  chainMainByCode.get(normalizeChainCode(code));
