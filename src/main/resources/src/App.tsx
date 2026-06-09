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
import { useResizable, type MeterResizeDirection } from "@/hooks/resize/useResizable";
import { useOverlayWindow, type OverlayMode } from "@/hooks/overlay/useOverlayWindow";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
// import { TooltipProvider } from "@/components/ui/tooltip";
import { useJoinRequestStore } from "@/stores/useJoinRequestStore";
import { JoinRequestPanel } from "@/components/joinPanel/JoinRequestPanel";
import { cn } from "@/lib/utils";
import { DebugConsole } from "./components/DebugConsole";
import { StatsConsentModal, type StatsOwnCharacter } from "@/components/StatsConsentModal";
import { UpdateToast } from "@/components/UpdateToast";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Power, SendToBack } from "lucide-react";
import lock from "@/assets/lock.png";

const METER_RESIZE_HANDLES: {
  direction: MeterResizeDirection;
  className: string;
  indicatorClassName: string;
}[] = [
  {
    direction: "n",
    className: "left-8 right-8 top-0 h-2 cursor-n-resize",
    indicatorClassName: "mx-auto mt-1 h-0.5 w-12 rounded-full",
  },
  {
    direction: "s",
    className: "left-8 right-8 bottom-0 h-2 cursor-s-resize",
    indicatorClassName: "mx-auto mt-1.5 h-0.5 w-12 rounded-full",
  },
  {
    direction: "e",
    className: "right-0 top-8 bottom-8 w-2 cursor-e-resize",
    indicatorClassName: "ml-1 mt-8 h-12 w-0.5 rounded-full",
  },
  {
    direction: "w",
    className: "left-0 top-8 bottom-8 w-2 cursor-w-resize",
    indicatorClassName: "ml-1.5 mt-8 h-12 w-0.5 rounded-full",
  },
  {
    direction: "ne",
    className: "right-0 top-0 h-4 w-4 cursor-ne-resize",
    indicatorClassName: "ml-2 mt-2 h-2 w-2 rounded-sm",
  },
  {
    direction: "nw",
    className: "left-0 top-0 h-4 w-4 cursor-nw-resize",
    indicatorClassName: "ml-1 mt-2 h-2 w-2 rounded-sm",
  },
  {
    direction: "se",
    className: "right-0 bottom-0 h-4 w-4 cursor-se-resize",
    indicatorClassName: "ml-2 mt-1 h-2 w-2 rounded-sm",
  },
  {
    direction: "sw",
    className: "left-0 bottom-0 h-4 w-4 cursor-sw-resize",
    indicatorClassName: "ml-1 mt-1 h-2 w-2 rounded-sm",
  },
];

export default function App() {
  const {
    players,
    targetName,
    // isCollapse,
    isInCombat,
    remainHp,
    maxHp,
    reset,
    // toggleCollapse,
    battleTime,
    formatBattleTime,
    setHistoryData,
  } = useMeter();

  const activePanelRef = useRef<PanelType>(null);
  const selectedRef = useRef<Player | null>(null);
  const statsOwnCharacterKeyRef = useRef<string>("");
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
  const joinRequestCount = useJoinRequestStore((s) => s.requests.length);
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
    closeAction,
    uiX,
    uiY,
    statsConsent,
    setCloseAction,
    setStatsConsent,
    refreshStatsConsent,
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
      closeAction: s.closeAction,
      uiX: s.uiX,
      uiY: s.uiY,
      statsConsent: s.statsConsent,
      setCloseAction: s.setCloseAction,
      setStatsConsent: s.setStatsConsent,
      refreshStatsConsent: s.refreshStatsConsent,
    })),
  );

  const [selectedHistoryIdx, setSelectedHistoryIdx] = useState<number | undefined>(undefined);
  const [statsConsentOpen, setStatsConsentOpen] = useState(false);
  const [statsOwnCharacter, setStatsOwnCharacter] = useState<StatsOwnCharacter | null>(null);
  const [closeActionDialogOpen, setCloseActionDialogOpen] = useState(false);
  const [dismissedUpdateVersion, setDismissedUpdateVersion] = useState<string | null>(null);

  const handlePanelToggle = useCallback((panel: PanelType) => {
    setActivePanel((prev) => (prev === panel ? null : panel));
  }, []);
  const [selected, setSelected] = useState<Player | null>(null);

  // small-window 작은 창 오버레이. 모드: fullscreen / meterOnly / union.
  const updateToastVisible = Boolean(
    updateInfo && activePanel !== "update" && dismissedUpdateVersion !== updateInfo.latestVersion,
  );
  const modalOpen = statsConsentOpen || closeActionDialogOpen;
  // 작은 창 오버레이는 기본 동작(토글 없음). 미터기만 있을 때만 작은 창(meterOnly),
  // 패널/토스트/모달이 열리면 전체화면 폴백(검증된 동작). union(패널 작은 창)은 네이티브 창 이동↔
  // WebView 리페인트 프레임 비동기로 전환 깜빡임이 구조적이라 보류 — 경로는 휴면 보존(렌더러 교체 시 재활성).
  const anyOverlayExpand =
    activePanel !== null || joinRequestCount > 0 || updateToastVisible || modalOpen;
  const overlayMode: OverlayMode = !isLoaded || anyOverlayExpand ? "fullscreen" : "meterOnly";
  useOverlayWindow(overlayMode);

  const { wasDraggingRef } = useDragUi(overlayMode);
  // const handleToggleCollapse = useCallback(() => {
  //   toggleCollapse();
  //   setActivePanel(null);
  //   setSelected(null);
  // }, [toggleCollapse]);

  const handleReset = useCallback(() => {
    window.javaBridge?.hardResetDps?.();
    reset();
    setSelectedHistoryIdx(undefined);
    setActivePanel(null);
    setSelected(null);
  }, [reset]);

  const handleExitRequest = useCallback(() => {
    if (closeAction === "tray") {
      window.javaBridge?.hideToTray?.();
      return;
    }
    if (closeAction === "exit") {
      window.javaBridge?.exitApp?.();
      return;
    }
    setCloseActionDialogOpen(true);
  }, [closeAction]);

  const chooseCloseAction = useCallback(
    (action: "tray" | "exit") => {
      setCloseAction(action);
      setCloseActionDialogOpen(false);
      if (action === "tray") {
        window.javaBridge?.hideToTray?.();
      } else {
        window.javaBridge?.exitApp?.();
      }
    },
    [setCloseAction],
  );
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
    if (updateInfo) {
      handlePanelToggle("update");
      return;
    }
    checkUpdate();
  }, [checkUpdate, handlePanelToggle, updateInfo]);

  const handleOpenUpdatePanel = useCallback(() => {
    setDismissedUpdateVersion(updateInfo?.latestVersion ?? null);
    setActivePanel("update");
  }, [updateInfo?.latestVersion]);

  const handleDismissUpdateToast = useCallback(() => {
    setDismissedUpdateVersion(updateInfo?.latestVersion ?? "__download__");
  }, [updateInfo?.latestVersion]);

  useEffect(() => {
    activePanelRef.current = activePanel;
  }, [activePanel]);
  useEffect(() => {
    selectedRef.current = selected;
  }, [selected]);

  useEffect(() => {
    if (!isLoaded) return;
    refreshStatsConsent();

    const detectOwnCharacter = () => {
      const raw = window.javaBridge?.getStatsOwnCharacter?.();
      if (!raw) return;
      try {
        const parsed = JSON.parse(raw) as StatsOwnCharacter;
        if (!parsed?.detected) return;
        setStatsOwnCharacter(parsed);
        const characterKey = `${parsed.server}:${parsed.nickname ?? ""}`;
        if (characterKey !== statsOwnCharacterKeyRef.current) {
          statsOwnCharacterKeyRef.current = characterKey;
          refreshStatsConsent();
        }
        if (useSettingsStore.getState().statsConsent.state === "unknown") {
          setStatsConsentOpen(true);
        }
      } catch {}
    };

    detectOwnCharacter();
    const timer = window.setInterval(detectOwnCharacter, 2000);
    return () => window.clearInterval(timer);
  }, [isLoaded, refreshStatsConsent]);

  useEffect(() => {
    if (downloadState.status !== "idle") {
      setDismissedUpdateVersion(null);
    }
  }, [downloadState.status]);

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
    "shadow-[inset_0_1px_0_rgba(255,255,255,0.05)] backdrop-blur-md transition-colors duration-300",
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
  const handleAcceptStatsConsent = useCallback(
    (publicCharacter: boolean) => {
      setStatsConsent({
        ...statsConsent,
        state: "accepted",
        uploadEnabled: true,
        publicCharacter,
        updatedAt: Date.now(),
      });
      setStatsConsentOpen(false);
    },
    [setStatsConsent, statsConsent],
  );
  const handleDeclineStatsConsent = useCallback(() => {
    setStatsConsent({
      ...statsConsent,
      state: "declined",
      uploadEnabled: false,
      updatedAt: Date.now(),
    });
    setStatsConsentOpen(false);
  }, [setStatsConsent, statsConsent]);
  return (
    // <TooltipProvider>
    <div
      style={
        {
          position: "fixed",
          // meterOnly: 미터기는 창 (0,0). union/fullscreen: 화면좌표 - 창 origin(--ovx; fullscreen 0).
          left: overlayMode === "meterOnly" ? 0 : `calc(${uiX}px - var(--ovx, 0px))`,
          top: overlayMode === "meterOnly" ? 0 : `calc(${uiY}px - var(--ovy, 0px))`,

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
        data-overlay-content
        className={meterClass}
        style={{ width: meterWidth }}>
        {!isBottomLayout && (
          <div className="mb-2">
            <Header
              className={headerClass}
              reset={handleReset}
              onExitRequest={handleExitRequest}
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
              reset={handleReset}
              onExitRequest={handleExitRequest}
              setSettings={handlePanelToggle}
              // isCollapse={isCollapse}
              // toggleCollapse={handleToggleCollapse}
            />
          </div>
        )}
        {!isMinimal &&
          METER_RESIZE_HANDLES.map((handle) => (
            <div
              key={handle.direction}
              data-no-drag
              onMouseDown={(e) => onMouseDown(e, handle.direction)}
              className={cn(
                "resizeHandle absolute z-20 opacity-0 transition-opacity hover:opacity-80 group-hover/app:opacity-45",
                handle.className,
              )}>
              <div
                className={cn(
                  "bg-[var(--meter-muted)] shadow-[0_0_10px_rgba(255,255,255,0.18)]",
                  handle.indicatorClassName,
                )}
              />
            </div>
          ))}
        {isClickThrough && (
          // 미터 박스 안 우상단(작은 창에 잘리지 않도록 음수 오프셋 → 내부로). 클릭스루 상태 표시.
          <div className="absolute top-1 right-1 z-50 pointer-events-none">
            <img
              src={lock}
              className="w-4 h-4" />
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
          smallWindow={false /* union 보류 — 패널은 전체화면 폴백 */}
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
      <StatsConsentModal
        open={statsConsentOpen}
        character={statsOwnCharacter}
        onAccept={handleAcceptStatsConsent}
        onDecline={handleDeclineStatsConsent}
      />
      {updateInfo &&
        activePanel !== "update" &&
        dismissedUpdateVersion !== updateInfo.latestVersion && (
          <UpdateToast
            updateInfo={updateInfo}
            downloadState={downloadState}
            onUpdate={startUpdate}
            onOpenPanel={handleOpenUpdatePanel}
            onDismiss={handleDismissUpdateToast}
          />
        )}
      <Dialog
        open={closeActionDialogOpen}
        onOpenChange={setCloseActionDialogOpen}>
        <DialogContent
          showCloseButton={false}
          className="max-w-[420px] overflow-hidden border border-slate-500/30 !bg-[#08111f]/95 p-0 text-slate-50 shadow-[0_24px_72px_rgba(0,0,0,0.58)] backdrop-blur-md">
          <DialogHeader className="border-b border-white/10 px-5 pb-4 pt-5">
            <div className="mb-1 flex size-10 items-center justify-center rounded-md border border-cyan-300/20 bg-cyan-300/10 text-cyan-200">
              <Power className="size-5" />
            </div>
            <DialogTitle className="text-lg font-bold text-slate-50">종료 버튼 동작 선택</DialogTitle>
            <DialogDescription className="text-sm leading-6 text-slate-300">
              선택한 방식은 설정에 저장되고, 다음부터 전원 버튼에 바로 적용됩니다.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-2.5 px-5 py-4 text-sm">
            <button
              type="button"
              onClick={() => chooseCloseAction("tray")}
              className="flex w-full cursor-pointer items-center gap-3 rounded-md border border-emerald-300/25 bg-emerald-300/8 p-3 text-left transition-colors hover:bg-emerald-300/14">
              <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-emerald-300/12 text-emerald-200">
                <SendToBack className="size-4.5" />
              </span>
              <span className="min-w-0">
                <span className="block font-bold text-emerald-100">트레이에서 계속 실행</span>
                <span className="text-xs text-slate-400">창만 숨기고 패킷 캡처와 미터기 동작은 유지합니다.</span>
              </span>
            </button>
            <button
              type="button"
              onClick={() => chooseCloseAction("exit")}
              className="flex w-full cursor-pointer items-center gap-3 rounded-md border border-rose-300/25 bg-rose-300/8 p-3 text-left transition-colors hover:bg-rose-300/14">
              <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-rose-300/12 text-rose-200">
                <Power className="size-4.5" />
              </span>
              <span className="min-w-0">
                <span className="block font-bold text-rose-100">완전히 종료</span>
                <span className="text-xs text-slate-400">미터기와 백그라운드 동작을 모두 종료합니다.</span>
              </span>
            </button>
          </div>
          <DialogFooter className="-mx-0 -mb-0 border-t border-white/10 !bg-white/[0.03] px-5 py-3">
            <Button
              variant="ghost"
              onClick={() => setCloseActionDialogOpen(false)}
              className="text-slate-300 opacity-80 hover:bg-white/10 hover:text-white hover:opacity-100">
              취소
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
    // </TooltipProvider>
  );
}
