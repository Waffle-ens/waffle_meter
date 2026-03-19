import type { Player } from "../types";


export interface Skill {
  code: string;
  name: string;
  time: number;
  crit: number;
  parry: number;
  back: number;
  perfect: number;
  double: number;
  dmg: number;
}

export interface Details {
  totalDmg: number;
  contributionPct: number;
  totalCritPct: number;
  totalParryPct: number;
  totalBackPct: number;
  totalPerfectPct: number;
  totalDoublePct: number;
  combatTime: string;
  skills: Skill[];
}

export const useDetails = () => {
  const getDetails = async (row: Player): Promise<Details> => {
    const raw = await window.dpsData?.getBattleDetail?.(row.id);

    let detailObj =
      typeof raw === "string" ? JSON.parse(raw) : raw;

    if (!detailObj || typeof detailObj !== "object") detailObj = {};

    const skills: Skill[] = [];
    let totalDmg = 0;

    let totalTimes = 0;
    let totalCrit = 0;
    let totalParry = 0;
    let totalBack = 0;
    let totalPerfect = 0;
    let totalDouble = 0;

    const pushSkill = (skill: Partial<Skill> & { code: string }) => {
      const dmg = Math.trunc(Number(skill.dmg || 0));
      if (dmg <= 0) return;

      totalDmg += dmg;

      totalTimes += skill.time || 0;
      totalCrit += skill.crit || 0;
      totalParry += skill.parry || 0;
      totalBack += skill.back || 0;
      totalPerfect += skill.perfect || 0;
      totalDouble += skill.double || 0;

      skills.push({
        code: skill.code,
        name: skill.name || "",
        time: skill.time || 0,
        crit: skill.crit || 0,
        parry: skill.parry || 0,
        back: skill.back || 0,
        perfect: skill.perfect || 0,
        double: skill.double || 0,
        dmg,
      });
    };

    for (const [code, value] of Object.entries(detailObj)) {
      if (!value || typeof value !== "object") continue;

      const v: any = value;
      const baseName = v.skillName || `스킬 ${code}`;

      pushSkill({
        code,
        name: baseName,
        time: v.times,
        dmg: v.damageAmount,
        crit: v.critTimes,
        parry: v.parryTimes,
        back: v.backTimes,
        perfect: v.perfectTimes,
        double: v.doubleTimes,
      });

      if (Number(v.dotDamageAmount) > 0) {
        pushSkill({
          code: `${code}-dot`,
          name: `${baseName} - 지속`,
          time: v.dotTimes,
          dmg: v.dotDamageAmount,
        });
      }
    }

    const pct = (num: number, den: number) =>
      den > 0 ? Math.round((num / den) * 1000) / 10 : 0;

    return {
      totalDmg,
      contributionPct: row.damageContribution,
      totalCritPct: pct(totalCrit, totalTimes),
      totalParryPct: pct(totalParry, totalTimes),
      totalBackPct: pct(totalBack, totalTimes),
      totalPerfectPct: pct(totalPerfect, totalTimes),
      totalDoublePct: pct(totalDouble, totalTimes),
      combatTime: "00:00", // timer 제거
      skills: skills.sort((a, b) => b.dmg - a.dmg),
    };
  };

  return { getDetails };
};