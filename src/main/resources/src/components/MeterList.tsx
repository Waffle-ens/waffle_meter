import { memo, useMemo, useRef } from "react";
import type { Player } from "../types";
import { MeterRow } from "./MeterRow";

const MAX_CACHE = 32;

interface Props {
  players: Player[];
  selectedId?: string;
  onSelect: (id: string) => void;
}

const getDisplayRows = (players: Player[]): Player[] => {
  const sorted = [...players].sort((a, b) => b.dps - a.dps);
  const top6 = sorted.slice(0, 6);
  const user = sorted.find((p) => p.isUser);

  if (!user) return top6;
  if (top6.some((p) => p.isUser)) return top6;
  return [...top6, user];
};

export const MeterList = memo(({ players, selectedId, onSelect }: Props) => {
  const cachedPlayersRef = useRef<Map<string, Player>>(new Map());
  const prevRowsRef = useRef<Player[]>([]);

  const rows = useMemo(() => {
    const next = getDisplayRows(players);

    const isSame =
      next.length === prevRowsRef.current.length &&
      next.every((p, i) => {
        const prev = prevRowsRef.current[i];
        return (
          prev.id === p.id && prev.dps === p.dps && prev.damageContribution === p.damageContribution
        );
      });

    if (isSame) return prevRowsRef.current;
    prevRowsRef.current = next;
    return next;
  }, [players]);

  const topDps = useMemo(() => Math.max(...rows.map((p) => p.dps), 1), [rows]);

  rows.forEach((p) => cachedPlayersRef.current.set(p.id, p));

  if (cachedPlayersRef.current.size > MAX_CACHE) {
    const visibleIds = new Set(rows.map((p) => p.id));
    const candidates = Array.from(cachedPlayersRef.current.keys()).filter(
      (id) => !visibleIds.has(id),
    );
    candidates.slice(0, cachedPlayersRef.current.size - MAX_CACHE).forEach((id) => {
      cachedPlayersRef.current.delete(id);
    });
  }

  const visibleIds = new Set(rows.map((p) => p.id));

  return (
    <div className="grid gap-1">
      {Array.from(cachedPlayersRef.current.values()).map((cached) => {
        const isVisible = visibleIds.has(cached.id);
        const current = isVisible ? rows.find((r) => r.id === cached.id)! : cached;

        return (
          <div
            className="overflow-hidden"
            key={cached.id}
            style={{ display: isVisible ? "block" : "none" }}>
            <MeterRow
              id={current.id}
              name={current.name}
              job={current.job}
              dps={current.dps}
              amount={current.amount}
              contribution={current.damageContribution}
              isUser={current.isUser}
              isSelected={selectedId === current.id}
              onSelect={onSelect}
              topDps={topDps}
            />
          </div>
        );
      })}
    </div>
  );
});
