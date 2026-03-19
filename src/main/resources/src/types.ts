
export interface Skill {
  name: string;
  damage: number;
  hits: number;
}

export interface RawPlayerValue {
  job?: string;
  dps?: number | string;
  damageContribution?: number | string;
  nickname?: string;
}

export interface RawCombatData {
  map?: Record<string, RawPlayerValue | number>;
  targetName?: string;
  battleTime?: number;
}

export interface Player {
  id: string;
  name: string;
  job: string;
  dps: number;
  totalDamage: number;
  damageContribution: number;
  isUser: boolean;
}
export interface CombatRaw {
  combatants: Record<string, any>;
}
