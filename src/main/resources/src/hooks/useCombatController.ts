import { useEffect, useRef, useState } from "react";
import type { Player } from "../types";
import { parseCombatData } from "../utils/parser";
// import { useDebugStore } from "../stores/debugStore";

const POLL_MS = 300;

export const useCombatController = () => {
  const [players, setPlayers] = useState<Player[]>([]);
  const [targetName, setTargetName] = useState<string>("");
  const [isCollapse, setIsCollapse] = useState(false);
  // const addLog = useDebugStore((s) => s.addLog);

  const [battleTime, setBattleTime] = useState<number | null>(null);
  const lastJsonRef = useRef<string | null>(null);
  const snapshotRef = useRef<Player[] | null>(null);
  const resetPendingRef = useRef(false);
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const formatBattleTime = (ms: number | null | undefined) => {
    if (!ms || !Number.isFinite(ms)) return "00:00";

    const totalSec = Math.floor(ms / 1000);
    const min = Math.floor(totalSec / 60);
    const sec = totalSec % 60;

    return `${String(min).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
  };

  const fetchDps = () => {
    if (isCollapse) return;

    const raw = window.dpsData?.getDpsData?.();
    // addLog(raw);
    if (typeof raw !== "string") return;

    if (raw === lastJsonRef.current) return;

    lastJsonRef.current = raw;

    const parsed = JSON.parse(raw);

    const rows = parseCombatData(parsed);
    const targetName = parsed.targetName ?? "";
    const battleTime = parsed.battleTime ?? null;

    let rowsToRender = rows;

    if (rows.length === 0) {
      if (snapshotRef.current) {
        rowsToRender = snapshotRef.current;
      } else {
        return;
      }
    } else {
      snapshotRef.current = rows;
    }

    setPlayers(rowsToRender);
    setTargetName(targetName);
    setBattleTime(battleTime);
  };
  const startPolling = () => {
    if (pollTimerRef.current) return;

    pollTimerRef.current = setInterval(fetchDps, POLL_MS);
  };

  const stopPolling = () => {
    if (!pollTimerRef.current) return;

    clearInterval(pollTimerRef.current);
    pollTimerRef.current = null;
  };

  const reset = () => {
    resetPendingRef.current = true;

    snapshotRef.current = null;
    lastJsonRef.current = null;

    setPlayers([]);
    setTargetName("");
  };

  const toggleCollapse = () => {
    setIsCollapse((prev) => {
      const next = !prev;

      if (next) {
        stopPolling();
        reset();
      } else {
        startPolling();
        fetchDps();
      }

      return next;
    });
  };

  useEffect(() => {
    startPolling();
    fetchDps();

    return () => stopPolling();
  }, []);

  return {
    players,
    targetName,
    isCollapse,
    battleTime,
    formatBattleTime,
    reset,
    toggleCollapse,
  };
};
