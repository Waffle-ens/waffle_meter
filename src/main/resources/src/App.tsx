import { useState, useCallback, useEffect, useRef } from "react";
import { useMeter } from "./hooks/useMeter";
import type { Player, PanelType } from "@/types";
import { MeterList } from "./components/MeterList";
import { useDragUi } from "@/hooks/drag/useDragUi";
import { Header } from "@/components/Header.tsx";
import { TargetInfo } from "@/components/TargetInfo";
import { SidePanel } from "@/components/panels/SidePanel.tsx";
import { CombatTimer } from "@/components/CombatTimer.tsx";
import { useVersionCheck } from "@/hooks/useVersionCheck";
import { useResizable } from "@/hooks/resize/useResizable";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
// import { TooltipProvider } from "@/components/ui/tooltip";
import { useJoinRequestStore } from "@/stores/useJoinRequestStore";
import { JoinRequestPanel } from "@/components/joinPanel/JoinRequestPanel";
import { cn } from "@/lib/utils";
import { DebugConsole } from "./components/DebugConsole";
import lock from "@/assets/lock.png";
export default function App() {
  const {
    players,
    targetName,
    // isCollapse,
    isInCombat,
    remainHp,
    maxHp,
    // reset,
    // toggleCollapse,
    battleTime,
    formatBattleTime,
    setHistoryData,
  } = useMeter();

  const activePanelRef = useRef<PanelType>(null);
  const selectedRef = useRef<Player | null>(null);
  const {
    updateInfo,
    currentVersion,
    openReleasePage,
    downloadState,
    retryDownload,
    startUpdate,
    checkUpdate,
    checkStatus,
  } = useVersionCheck();
  const addRequest = useJoinRequestStore((s) => s.addRequest);
  const removeRequest = useJoinRequestStore((s) => s.removeRequest);
  const clearAll = useJoinRequestStore((s) => s.clearAll);
  const refuseRequest = useJoinRequestStore((s) => s.refuseRequest);

  const [activePanel, setActivePanel] = useState<PanelType>(null);
  const { meterWidth, onMouseDown, isDragging } = useResizable();
  const {
    isLoaded,
    rowHeight,
    isMinimal,
    showCombatTimerInMinimal,
    showTargetInfoInMinimal,
    meterOpacity,
    isClickThrough,
    overlayTheme,
    overlayLayout,
    uiX,
    uiY,
  } = useSettingsStore(
    useShallow((s) => ({
      isLoaded: s.isLoaded,
      rowHeight: s.rowHeight,
      isMinimal: s.isMinimal,
      showCombatTimerInMinimal: s.showCombatTimerInMinimal,
      showTargetInfoInMinimal: s.showTargetInfoInMinimal,
      meterOpacity: s.meterOpacity,
      isClickThrough: s.isClickThrough,
      overlayTheme: s.overlayTheme,
      overlayLayout: s.overlayLayout,
      uiX: s.uiX,
      uiY: s.uiY,
    })),
  );

  const [selectedHistoryIdx, setSelectedHistoryIdx] = useState<number | undefined>(undefined);

  const handlePanelToggle = useCallback((panel: PanelType) => {
    setActivePanel((prev) => (prev === panel ? null : panel));
  }, []);
  const [selected, setSelected] = useState<Player | null>(null);
  const { wasDraggingRef } = useDragUi();
  // const handleToggleCollapse = useCallback(() => {
  //   toggleCollapse();
  //   setActivePanel(null);
  //   setSelected(null);
  // }, [toggleCollapse]);

  // const handleReset = useCallback(() => {
  //   reset();
  //   setSelectedHistoryIdx(undefined);
  //   setActivePanel(null);
  //   setSelected(null);
  // }, [reset]);
  const playersRef = useRef<Player[]>([]);
  useEffect(() => {
    playersRef.current = players;
  }, [players]);

  const handleSelect = useCallback((id: number) => {
    if (wasDraggingRef.current) return;
    const player = playersRef.current.find((p) => p.id === id);
    if (!player) return;
    if (activePanelRef.current === "details" && selectedRef.current?.id === player.id) {
      setActivePanel(null);
      return;
    }
    setSelected(player);
    setActivePanel("details");
  }, []);
  const handleClose = useCallback(() => {
    setActivePanel(null);
  }, []);
  const handleCheckUpdate = useCallback(() => {
    checkUpdate();
    handlePanelToggle("update");
  }, [checkUpdate, handlePanelToggle]);

  useEffect(() => {
    activePanelRef.current = activePanel;
  }, [activePanel]);
  useEffect(() => {
    selectedRef.current = selected;
  }, [selected]);

  useEffect(() => {
    if (!isLoaded) return;
    window.javaBridge?.syncOverlayBounds?.();
  }, [isLoaded]);

  useEffect(() => {
    if (updateInfo) setActivePanel("update");
  }, [updateInfo]);

  useEffect(() => {
    const handleKeydown = (e: KeyboardEvent) => {
      if (e.key === "Escape") handleClose();
    };
    window.addEventListener("keydown", handleKeydown);
    return () => window.removeEventListener("keydown", handleKeydown);
  }, [handleClose]);
  useEffect(() => {
    (window as any).onClickThroughChanged = (v: boolean) => {
      useSettingsStore.setState({ isClickThrough: v });
    };
  }, []);
  // useEffect(() => {
  //   (window as any).strongReset = () => {
  //     reset();
  //     setActivePanel(null);
  //     setSelected(null);
  //   };
  // }, [reset]);

  useEffect(() => {
    if (isInCombat) {
      setSelectedHistoryIdx(undefined);
    }
  }, [isInCombat]);

  useEffect(() => {
    (window as any).onJoinRequest = (data: any) => {
      addRequest(data);
    };
    (window as any).onJoinRequestRemove = (id: number) => {
      removeRequest(id);
    };
    (window as any).onExitPartyUI = () => {
      clearAll();
    };
    (window as any).onRefuseJoinRequest = () => {
      refuseRequest();
    };
  }, []);
  const isLightOverlay = overlayTheme === "light";
  const isBottomLayout = overlayLayout === "bottom";

  const meterClass = cn(
    "relative overflow-hidden rounded-md border px-2.5 py-2",
    "shadow-[0_20px_44px_rgba(0,0,0,0.32)] backdrop-blur-md transition-colors duration-300",
    "border-[var(--meter-border)] text-[var(--meter-fg)]",
    isMinimal
      ? "border-transparent bg-transparent group-hover/app:border-[var(--meter-border)] group-hover/app:bg-[var(--meter-bg)]"
      : "bg-[var(--meter-bg)]",
  );

  const headerClass = cn(
    "transition-opacity duration-300",
    isMinimal && "opacity-0 group-hover/app:opacity-100",
  );

  const rootClass = cn(
    "overlay-shell drag-area cursor-move select-none relative group/app antialiased",
    isBottomLayout ? "pb-2" : "pt-2",
    isLightOverlay && "overlay-light",
    isBottomLayout && "overlay-bottom-layout",
    isDragging && "pointer-events-none",
  );
  const handleSelectHistory = useCallback(
    (idx: number, report: any) => {
      setHistoryData(report);
      setSelectedHistoryIdx(idx);
    },
    [setHistoryData],
  );
  return (
    // <TooltipProvider>
    <div
      style={
        {
          position: "fixed",
          left: uiX,
          top: uiY,

          width: "fit-content",
          "--meter-bg": isLightOverlay
            ? `rgba(250,252,255,${meterOpacity})`
            : `rgba(5,10,16,${meterOpacity})`,
          "--panel-bg": isLightOverlay
            ? "rgba(250,252,255,0.97)"
            : "rgba(7,12,20,0.97)",
          "--join-panel-bg": isLightOverlay
            ? "rgba(250,252,255,0.97)"
            : "rgba(7,12,20,0.97)",
          "--meter-fg": isLightOverlay ? "#172033" : "#f8fafc",
          "--meter-muted": isLightOverlay ? "rgba(51,65,85,0.7)" : "rgba(203,213,225,0.68)",
          "--meter-border": isLightOverlay ? "rgba(15,23,42,0.18)" : "rgba(148,163,184,0.18)",
          "--meter-soft-border": isLightOverlay
            ? "rgba(15,23,42,0.11)"
            : "rgba(148,163,184,0.11)",
          "--meter-row-bg": isLightOverlay ? "rgba(255,255,255,0.72)" : "rgba(15,23,42,0.64)",
          "--meter-row-hover": isLightOverlay
            ? "rgba(226,232,240,0.92)"
            : "rgba(30,41,59,0.82)",
          "--meter-row-selected": isLightOverlay
            ? "rgba(16,185,129,0.16)"
            : "rgba(20,184,166,0.13)",
          "--meter-row-selected-border": isLightOverlay
            ? "rgba(5,150,105,0.38)"
            : "rgba(45,212,191,0.36)",
          "--meter-section-bg": isLightOverlay
            ? "rgba(226,232,240,0.88)"
            : "rgba(15,23,42,0.84)",
          "--meter-table-head-bg": isLightOverlay
            ? "rgba(51,65,85,0.84)"
            : "rgba(30,41,59,0.94)",
          "--meter-table-head-fg": "#f8fafc",
          "--meter-table-row-bg": isLightOverlay
            ? "rgba(255,255,255,0.74)"
            : "rgba(15,23,42,0.58)",
          "--meter-table-row-alt": isLightOverlay
            ? "rgba(241,245,249,0.82)"
            : "rgba(30,41,59,0.46)",
          "--meter-control-bg": isLightOverlay ? "rgba(255,255,255,0.72)" : "rgba(15,23,42,0.7)",
          "--meter-control-hover": isLightOverlay
            ? "rgba(226,232,240,0.96)"
            : "rgba(30,41,59,0.92)",
          "--meter-control-border": isLightOverlay
            ? "rgba(15,23,42,0.13)"
            : "rgba(148,163,184,0.13)",
          "--meter-icon-ring": isLightOverlay ? "rgba(15,23,42,0.16)" : "rgba(148,163,184,0.16)",
          "--meter-tint": isLightOverlay ? "rgba(255,255,255,0.46)" : "rgba(255,255,255,0.035)",
          "--meter-stat-bg": isLightOverlay ? "rgba(15,23,42,0.055)" : "rgba(2,6,23,0.34)",
          "--meter-accent": isLightOverlay ? "#0f766e" : "#2dd4bf",
          "--meter-accent-soft": isLightOverlay ? "rgba(15,118,110,0.12)" : "rgba(45,212,191,0.12)",
          "--meter-shine": isLightOverlay
            ? "linear-gradient(90deg,rgba(255,255,255,0.52),rgba(255,255,255,0)_44%)"
            : "linear-gradient(90deg,rgba(255,255,255,0.06),rgba(255,255,255,0)_44%)",
          visibility: isLoaded ? "visible" : "hidden",
        } as React.CSSProperties
      }
      className={rootClass}>
      <div
        data-meter-root-anchor
        className={meterClass}
        style={{ width: meterWidth }}>
        {!isBottomLayout && (
          <div className="mb-2">
            <Header
              className={headerClass}
              // reset={handleReset}
              setSettings={handlePanelToggle}

              // isCollapse={isCollapse}
              // toggleCollapse={handleToggleCollapse}
            />
          </div>
        )}
        <div>
          {players.length > 0 && (!isMinimal || showTargetInfoInMinimal) && (
            <TargetInfo
              targetName={targetName}
              rowHeight={rowHeight}
              remainHp={remainHp}
              maxHp={maxHp}
            />
          )}
          <MeterList
            players={players}
            selectedId={selected?.id}
            onSelect={handleSelect}
            rowHeight={rowHeight}
          />

          {battleTime && (!isMinimal || showCombatTimerInMinimal) && (
            <CombatTimer
              isInCombat={isInCombat}
              combatTime={formatBattleTime(battleTime)}
            />
          )}
        </div>
        {isBottomLayout && (
          <div className="mt-2">
            <Header
              className={headerClass}
              // reset={handleReset}
              setSettings={handlePanelToggle}
              // isCollapse={isCollapse}
              // toggleCollapse={handleToggleCollapse}
            />
          </div>
        )}
        {!isMinimal && (
          <div
            onMouseDown={onMouseDown}
            className="resizeHandle absolute top-1/2 -right-3 flex h-16 w-3 -translate-y-1/2 cursor-e-resize items-center justify-center opacity-45 transition-opacity hover:opacity-100">
            <div className="h-10 w-1 rounded-full bg-[var(--meter-muted)] shadow-[0_0_12px_rgba(255,255,255,0.2)] transition-colors" />
          </div>
        )}
        {isClickThrough && (
          <div
            className="absolute -top-2  z-50 pointer-events-none"
            style={{ right: "-7.5px" }}>
            <img
              src={lock}
              className="w-4 h-4"></img>
            {/* <LockKeyhole className="size-4 opacity-60 text-white " /> */}
          </div>
        )}
      </div>

      <DebugConsole></DebugConsole>
      <div>
        <JoinRequestPanel />
      </div>
      <div>
        <SidePanel
          type={activePanel}
          player={selected}
          players={players}
          onClose={handleClose}
          combatTime={formatBattleTime(battleTime)}
          updateInfo={updateInfo}
          onUpdate={startUpdate}
          checkStatus={checkStatus}
          downloadState={downloadState}
          onRetryDownload={retryDownload}
          formatBattleTime={formatBattleTime}
          historyIdx={selectedHistoryIdx}
          onOpenReleasePage={openReleasePage}
          onSelectHistory={handleSelectHistory}
          currentVersion={currentVersion ?? undefined}
          onCheckUpdate={handleCheckUpdate}
        />
      </div>
    </div>
    // </TooltipProvider>
  );
}
