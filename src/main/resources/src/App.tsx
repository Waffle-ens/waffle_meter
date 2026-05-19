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
    panelOpacity,
    joinPanelOpacity,
    meterListOpacity,
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
      panelOpacity: s.panelOpacity,
      joinPanelOpacity: s.joinPanelOpacity,
      meterListOpacity: s.meterListOpacity,
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
    "shadow-[0_18px_50px_rgba(0,0,0,0.36)] backdrop-blur-md transition-colors duration-300",
    "border-[var(--meter-border)] text-[var(--meter-fg)]",
    isMinimal
      ? "border-transparent bg-transparent group-hover/app:border-[var(--meter-border)] group-hover/app:bg-(--meter-bg)"
      : "bg-(--meter-bg)",
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
            ? `rgba(248,250,252,${meterOpacity})`
            : `rgba(13,17,23,${meterOpacity})`,
          "--panel-bg": isLightOverlay
            ? `rgba(248,250,252,${panelOpacity})`
            : `rgba(15,18,24,${panelOpacity})`,
          "--join-panel-bg": isLightOverlay
            ? `rgba(248,250,252,${joinPanelOpacity})`
            : `rgba(15,18,24,${joinPanelOpacity})`,
          "--meter-fg": isLightOverlay ? "#172033" : "#f8fafc",
          "--meter-muted": isLightOverlay ? "rgba(51,65,85,0.72)" : "rgba(226,232,240,0.72)",
          "--meter-border": isLightOverlay ? "rgba(15,23,42,0.16)" : "rgba(255,255,255,0.1)",
          "--meter-soft-border": isLightOverlay
            ? "rgba(15,23,42,0.1)"
            : "rgba(255,255,255,0.06)",
          "--meter-row-bg": isLightOverlay ? "rgba(15,23,42,0.055)" : "rgba(0,0,0,0.25)",
          "--meter-row-hover": isLightOverlay
            ? "rgba(15,23,42,0.095)"
            : "rgba(255,255,255,0.055)",
          "--meter-row-selected": isLightOverlay
            ? "rgba(14,165,233,0.16)"
            : "rgba(255,255,255,0.08)",
          "--meter-row-selected-border": isLightOverlay
            ? "rgba(14,116,144,0.35)"
            : "rgba(255,255,255,0.3)",
          "--meter-control-bg": isLightOverlay ? "rgba(15,23,42,0.06)" : "rgba(255,255,255,0.03)",
          "--meter-control-hover": isLightOverlay
            ? "rgba(15,23,42,0.12)"
            : "rgba(255,255,255,0.1)",
          "--meter-control-border": isLightOverlay
            ? "rgba(15,23,42,0.12)"
            : "rgba(255,255,255,0)",
          "--meter-icon-ring": isLightOverlay ? "rgba(15,23,42,0.12)" : "rgba(255,255,255,0.1)",
          "--meter-tint": isLightOverlay ? "rgba(255,255,255,0.5)" : "rgba(255,255,255,0.035)",
          "--meter-shine": isLightOverlay
            ? "linear-gradient(90deg,rgba(255,255,255,0.3),rgba(255,255,255,0)_46%)"
            : "linear-gradient(90deg,rgba(255,255,255,0.08),rgba(255,255,255,0)_46%)",
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
        <div style={{ opacity: meterListOpacity }}>
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
