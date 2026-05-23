import { memo, useMemo } from "react";
import type { Player } from "@/types";
import { MeterRow } from "./MeterRow";
import { useSettingsStore } from "@/stores/useSettingsStore";

interface Props {
  players: Player[];
  selectedId?: number;
  onSelect: (id: number) => void;
  rowHeight: number;
}

const getMetric = (player: Player, mode: "dps" | "total") =>
  Number.isFinite(mode === "total" ? player.amount : player.dps)
    ? mode === "total"
      ? player.amount
      : player.dps
    : 0;

const getDisplayRows = (players: Player[], mode: "dps" | "total"): Player[] => {
  const sorted = [...players].sort((a, b) => getMetric(b, mode) - getMetric(a, mode));
  const top8 = sorted.slice(0, 8);
  const user = sorted.find((p) => p.isUser);
  if (!user) return top8;
  if (top8.some((p) => p.isUser)) return top8;
  return [...top8, user];
};

export const MeterList = memo(({ players, selectedId, onSelect, rowHeight }: Props) => {
  const damageValueMode = useSettingsStore((s) => s.damageValueMode);
  const rows = useMemo(() => getDisplayRows(players, damageValueMode), [players, damageValueMode]);
  const topMetric = useMemo(
    () => Math.max(...rows.map((p) => getMetric(p, damageValueMode)), 1),
    [rows, damageValueMode],
  );

  if (rows.length === 0) {
    return (
      <div className="w-full">
        <div
          style={{ height: rowHeight }}
          className="flex h-full items-center gap-3 rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-row-bg)] px-3 text-[var(--meter-muted)]">
          <div
            className="flex shrink-0 items-center justify-center rounded-full border border-[var(--meter-soft-border)] bg-[var(--meter-control-bg)]"
            style={{ width: Math.round(rowHeight * 0.7), height: Math.round(rowHeight * 0.7) }}>
            <div
              className="rounded-full bg-[var(--meter-muted)] opacity-30"
              style={{ width: Math.round(rowHeight * 0.5), height: Math.round(rowHeight * 0.5) }}
            />
          </div>
          <span
            className="truncate font-semibold"
            style={{ fontSize: `${Math.max(12, Math.round(rowHeight * 0.4))}px` }}>
            전투 대기 중
          </span>
        </div>
      </div>
    );
  }

  return (
    <div className="grid w-full gap-1">
      {rows.map((current, index) => {
        return (
          <div
            key={current.id}
            className="min-w-0">
            <MeterRow
              rank={index + 1}
              id={current.id}
              name={current.name}
              job={current.job}
              server={current.server}
              rowHeight={rowHeight}
              dps={current.dps}
              amount={current.amount}
              contribution={current.damageContribution}
              entireContribution={current.entireContribution}
              isUser={current.isUser}
              isSelected={selectedId === current.id}
              onSelect={onSelect}
              topMetric={topMetric}
              power={current.power}
            />
          </div>
        );
      })}
    </div>
  );
});
