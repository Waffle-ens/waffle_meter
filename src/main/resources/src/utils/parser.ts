import type { Player, RawCombatData, RawPlayerValue } from "../types";

export function parseCombatData(raw: unknown): Player[] {
  const data = raw as RawCombatData;

  if (!data?.map) return [];

  const rows: Player[] = [];

  for (const [id, value] of Object.entries(data.map)) {
    const isObj = value && typeof value === "object";
    const obj = isObj ? (value as RawPlayerValue) : null;

    const user = obj?.user;
    const job = user?.job ?? "";
    const nickname = user?.nickname ?? "";
    const name = nickname || String(id);
    const isUser = user?.isExecutor ?? false;

    const dpsRaw = isObj ? obj?.dps : value;
    const amountRaw = isObj ? obj?.amount : 0;
    const dps = Math.trunc(Number(dpsRaw));
    const amount = Number(amountRaw);

    const contribRaw = isObj ? Number(obj?.damageContribution) : NaN;
    const damageContribution = Number.isFinite(contribRaw) ? Math.round(contribRaw * 10) / 10 : 0;

    if (!Number.isFinite(dps)) continue;

    rows.push({
      id: String(id),
      name,
      job,
      dps,
      amount,
      damageContribution,
      isUser,
    });
  }

  return rows.sort((a, b) => b.dps - a.dps);
}
