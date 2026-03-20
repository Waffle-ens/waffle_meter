import { useState, useCallback, useEffect, useRef } from "react";
import { useMeter } from "./hooks/useMeter";

import type { Player } from "./types";
import type { PanelType } from "./types";

import { MeterList } from "./components/MeterList";
import { useDragWindow } from "./hooks/useDragWindow";

import { Header } from "@/components/Header.tsx";
import { TargetInfo } from "@/components/TargetInfo";
import { SidePanel } from "@/components/panels/SidePanel.tsx";
import { CombatTimer } from "@/components/CombatTimer.tsx";
import { useVersionCheck } from "@/hooks/useVersionCheck";
import { DebugConsole } from "./components/DebugConsole";
// import { useDebugStore } from "./stores/debugStore.ts";

export default function App() {
  const {
    players,
    targetName,
    isCollapse,
    reset,
    toggleCollapse,
    battleTime,
    formatBattleTime,
    isInCombat,
  } = useMeter();
  const activePanelRef = useRef<PanelType>(null);
  const selectedRef = useRef<Player | null>(null);
  const { updateInfo, openReleasePage } = useVersionCheck();

  const [activePanel, setActivePanel] = useState<PanelType>(null);

  const handlePanelToggle = useCallback((panel: PanelType) => {
    setActivePanel((prev) => (prev === panel ? null : panel));
  }, []);

  const [selected, setSelected] = useState<Player | null>(null);

  useDragWindow(".drag-area");

  const handleToggleCollapse = useCallback(() => {
    toggleCollapse();
    setActivePanel(null);
    setSelected(null);
  }, [toggleCollapse]);

  const handleReset = useCallback(() => {
    reset();
    setActivePanel(null);
    setSelected(null);
  }, [reset]);

  const handleSelect = useCallback(
    (id: string) => {
      const player = players.find((p) => p.id === id);
      if (!player) return;

      if (activePanelRef.current === "details" && selectedRef.current?.id === player.id) {
        setActivePanel(null);
        return;
      }

      setSelected(player);
      setActivePanel("details");
    },
    [players],
  );

  const handleClose = useCallback(() => {
    setActivePanel(null);
  }, []);

  useEffect(() => {
    activePanelRef.current = activePanel;
  }, [activePanel]);
  useEffect(() => {
    selectedRef.current = selected;
  }, [selected]);

  useEffect(() => {
    (window as any).resetDpsUI = () => {
      reset();
      setActivePanel(null);
      setSelected(null);
    };
  }, [reset]);

  // const addLog = useDebugStore((s) => s.addLog);
  useEffect(() => {
    if (updateInfo) {
      // addLog(`updateinfo 바뀜: ${JSON.stringify(updateInfo)}`);
      setActivePanel("update");
    }
  }, [updateInfo]);

  useEffect(() => {
    const handleKeydown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        handleClose();
      }
    };

    window.addEventListener("keydown", handleKeydown);
    return () => window.removeEventListener("keydown", handleKeydown);
  }, [handleClose]);

  return (
    <div
      style={{ width: "fit-content" }}
      className="relative">
      <div className="w-100 rounded-lg meter p-4">
        <DebugConsole />
        <Header
          reset={handleReset}
          setSettings={handlePanelToggle}
          isCollapse={isCollapse}
          toggleCollapse={handleToggleCollapse}
        />
        {players.length > 0 && <TargetInfo targetName={targetName} />}
        <MeterList
          players={players}
          selectedId={selected?.id}
          onSelect={handleSelect}
        />
        {battleTime && (
          <CombatTimer
            isInCombat={isInCombat}
            combatTime={formatBattleTime(battleTime)}
          />
        )}
      </div>
      <SidePanel
        type={activePanel}
        player={selected}
        onClose={handleClose}
        combatTime={formatBattleTime(battleTime)}
        updateInfo={updateInfo}
        onUpdate={openReleasePage}
      />
    </div>
  );
}
