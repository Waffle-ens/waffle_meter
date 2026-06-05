import { useCallback, useEffect, useRef, useState } from "react";
import type { Player } from "@/types";
import { parseCombatData } from "../utils/parser";
// import { useDebugStore } from "../stores/debugStore";

const POLL_MS = 500;

interface MeterSnapshot {
  players: Player[];
  targetName: string;
  remainHp: number;
  maxHp: number;
  isInCombat: boolean;
  battleTime: number | null;
}

const initialSnapshot: MeterSnapshot = {
  players: [],
  targetName: "",
  remainHp: 0,
  maxHp: 0,
  isInCombat: false,
  battleTime: null,
};

const isPlayerSame = (a: Player, b: Player) =>
  a.id === b.id &&
  a.name === b.name &&
  a.job === b.job &&
  a.server === b.server &&
  a.dps === b.dps &&
  a.amount === b.amount &&
  a.damageContribution === b.damageContribution &&
  a.entireContribution === b.entireContribution &&
  a.isUser === b.isUser;

const arePlayersSame = (prev: Player[], next: Player[]) =>
  prev.length === next.length && prev.every((p, i) => isPlayerSame(p, next[i]));

const stabilizePlayers = (prev: Player[], next: Player[]) => {
  if (arePlayersSame(prev, next)) return prev;
  if (prev.length === 0 || next.length === 0) return next;

  const prevById = new Map(prev.map((p) => [p.id, p]));
  let reusedAny = false;
  const stable = next.map((row) => {
    const prevRow = prevById.get(row.id);
    if (!prevRow || !isPlayerSame(prevRow, row)) return row;
    reusedAny = true;
    return prevRow;
  });

  return reusedAny ? stable : next;
};

export const useMeter = () => {
  const [snapshot, setSnapshot] = useState<MeterSnapshot>(initialSnapshot);
  // const [isCollapse, setIsCollapse] = useState(false);
  const resetTimestampRef = useRef<number>(0);

  // const { addLog } = useDebugStore();
  const isCollapseRef = useRef(false);
  const lastBattleTimeRef = useRef<number | null>(null);
  const combatTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isInCombatRef = useRef(false);

  const lastJsonRef = useRef<string | null>(null);
  const snapshotRef = useRef<Player[] | null>(null);
  const resetPendingRef = useRef(false);
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const setSnapshotIfChanged = useCallback((patch: Partial<MeterSnapshot>) => {
    setSnapshot((prev) => {
      const nextPlayers = patch.players ? stabilizePlayers(prev.players, patch.players) : prev.players;
      const next: MeterSnapshot = { ...prev, ...patch, players: nextPlayers };
      isInCombatRef.current = next.isInCombat;
      if (
        prev.targetName === next.targetName &&
        prev.remainHp === next.remainHp &&
        prev.maxHp === next.maxHp &&
        prev.isInCombat === next.isInCombat &&
        prev.battleTime === next.battleTime &&
        arePlayersSame(prev.players, next.players)
      ) {
        return prev;
      }
      if (patch.players) snapshotRef.current = nextPlayers;
      return next;
    });
  }, []);

  const formatBattleTime = useCallback((ms: number | null | undefined) => {
    if (!ms || !Number.isFinite(ms) || ms < 0) return "00:00";
    const totalSec = Math.floor(ms / 1000);
    const min = Math.floor(totalSec / 60);
    const sec = totalSec % 60;
    return `${String(min).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
  }, []);

  const fetchDps = useCallback(() => {
    if (isCollapseRef.current) return;
    const raw = window.javaBridge?.getDpsData?.();
    // addLog(` ${raw}`);
    if (typeof raw !== "string") return;
    if (raw === lastJsonRef.current) {
      return;
    }

    lastJsonRef.current = raw;

    let parsed: unknown;
    try {
      parsed = JSON.parse(raw);
    } catch {
      return;
    }
    const { players: rows, targetName, remainHp, maxHp } = parseCombatData(parsed);
    const combatData = parsed as {
      contributors?: unknown[];
      battleStart?: number;
      battleEnd?: number;
    };

    if (resetPendingRef.current) {
      const contributors = combatData.contributors ?? [];
      if (contributors.length > 0) {
        if ((combatData.battleStart ?? 0) > resetTimestampRef.current) {
          resetPendingRef.current = false;
        } else {
          return;
        }
      } else {
        resetPendingRef.current = false;
        return;
      }
    }

    const battleStart = combatData.battleStart ?? null;
    const battleEnd = combatData.battleEnd ?? null;
    const battleTime = battleStart && battleEnd ? battleEnd - battleStart : null;
    let isInCombat = isInCombatRef.current;

    if (battleEnd !== lastBattleTimeRef.current) {
      lastBattleTimeRef.current = battleEnd;
      isInCombat = true;

      if (combatTimerRef.current) clearTimeout(combatTimerRef.current);
      combatTimerRef.current = setTimeout(() => {
        setSnapshotIfChanged({ isInCombat: false });
      }, 1000);
    }

    let rowsToRender = rows;

    if (rows.length === 0) {
      if (snapshotRef.current) {
        rowsToRender = snapshotRef.current;
      } else {
        return;
      }
    }

    setSnapshotIfChanged({
      players: rowsToRender,
      targetName,
      remainHp,
      maxHp,
      battleTime,
      isInCombat,
    });
  }, [setSnapshotIfChanged]);

  const reset = useCallback(() => {
    resetPendingRef.current = false;
    resetTimestampRef.current = Date.now();
    snapshotRef.current = null;
    lastJsonRef.current = null;
    lastBattleTimeRef.current = null;
    isInCombatRef.current = false;

    if (combatTimerRef.current) {
      clearTimeout(combatTimerRef.current);
      combatTimerRef.current = null;
    }

    setSnapshot(initialSnapshot);
  }, []);

  // const toggleCollapse = () => {
  //   const next = !isCollapseRef.current;
  //   isCollapseRef.current = next;
  //   setIsCollapse(next);

  //   if (next) {
  //     stopPolling();
  //     reset();
  //     window?.javaBridge?.resetDps?.();
  //   } else {
  //     startPolling();
  //     fetchDps();
  //   }
  // };
  const setHistoryData = useCallback((report: any) => {
    const { players: rows, targetName, remainHp, maxHp } = parseCombatData(report);
    const sorted = [...rows].sort((a, b) => b.dps - a.dps);

    const battleTime = (report.battleEnd ?? 0) - (report.battleStart ?? 0);
    setSnapshotIfChanged({
      players: sorted,
      targetName,
      remainHp,
      maxHp,
      battleTime,
      isInCombat: false,
    });
  }, [setSnapshotIfChanged]);

  useEffect(() => {
    if (!pollTimerRef.current) {
      pollTimerRef.current = setInterval(fetchDps, POLL_MS);
    }
    fetchDps();

    return () => {
      if (pollTimerRef.current) {
        clearInterval(pollTimerRef.current);
        pollTimerRef.current = null;
      }
      if (combatTimerRef.current) {
        clearTimeout(combatTimerRef.current);
        combatTimerRef.current = null;
      }
    };
  }, [fetchDps]);

  return {
    players: snapshot.players,
    targetName: snapshot.targetName,
    // isCollapse,
    battleTime: snapshot.battleTime,
    isInCombat: snapshot.isInCombat,
    remainHp: snapshot.remainHp,
    maxHp: snapshot.maxHp,
    formatBattleTime,
    reset,
    // toggleCollapse,
    setHistoryData,
  };
};
