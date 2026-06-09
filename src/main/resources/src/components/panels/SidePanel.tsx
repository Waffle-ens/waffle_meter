import { memo, useCallback, useEffect, useRef, useState } from "react";
import type { CheckStatus, Player, UpdateInfo, PanelType, DownloadState } from "@/types";
import { DetailsPanel } from "./DetailsPanel";
import { SettingsPanel } from "./SettingsPanel.tsx";
import { UpdatePanel, UPDATE_PANEL_DOT_CLS, UPDATE_PANEL_HEADER_TITLE } from "./UpdatePanel";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { HistoryPanel } from "./HistoryPanel";
import { useDraggablePanel } from "@/hooks/drag/useDraggablePanel";
import { useSidePanelResize } from "@/hooks/resize/useSidePanelResize";
import { CircleX } from "lucide-react";
import { cn } from "@/lib/utils.ts";
import { Button } from "@/components/ui/button";
import { ResizeHandle } from "../ResizeHandle.tsx";

const SIDE_BODY_VIEWPORT = "min-h-0 shrink-0 flex flex-col overflow-hidden";

const SIDE_OUTER = "flex h-full min-h-0 w-full flex-col overflow-hidden";
const DEFAULT_SIDE_PANEL_GAP = 8;
const DEFAULT_SIDE_PANEL_Y = 8;
const SIDE_PANEL_HEADER_HEIGHT = 44;

// 화면좌표 기준 기본 위치. 미터기 width/top 은 창 origin 과 무관(전체화면=screen, 작은 창=동일 화면좌표)
// 하므로 store uiX/uiY + 미터기 크기로 계산해 두 모드에서 일관되게 동작한다.
const getDefaultSidePanelX = (fallbackWidth: number) => {
  const { uiX } = useSettingsStore.getState();
  const meterRoot = document.querySelector("[data-meter-root-anchor]");
  const meterWidth = meterRoot?.getBoundingClientRect().width ?? fallbackWidth;
  return uiX + meterWidth + DEFAULT_SIDE_PANEL_GAP;
};

const getDefaultSidePanelY = (layout: "standard" | "bottom", panelOuterHeight: number) => {
  const { uiY } = useSettingsStore.getState();
  if (layout === "bottom") {
    return Math.max(DEFAULT_SIDE_PANEL_Y, uiY - panelOuterHeight - DEFAULT_SIDE_PANEL_GAP);
  }
  return uiY;
};

const clampPanelPosition = (x: number, y: number, width: number, height: number) => {
  const maxX = Math.max(0, window.innerWidth - width);
  const maxY = Math.max(0, window.innerHeight - height);

  return {
    x: Math.min(Math.max(0, x), maxX),
    y: Math.min(Math.max(0, y), maxY),
  };
};

const SIDE_SHELL = {
  details: `${SIDE_OUTER} px-6 py-4 text-[var(--meter-fg)] font-semibold`,
  settings: `${SIDE_OUTER} pl-5 pr-3 pb-6 font-semibold`,
  history: `${SIDE_OUTER} p-4 text-[var(--meter-fg)] font-semibold`,
  update: `${SIDE_OUTER} font-semibold`,
} as const;

interface SidePanelProps {
  type: PanelType;
  smallWindow?: boolean;
  player: Player | null;
  onClose: () => void;
  combatTime: string;
  updateInfo?: UpdateInfo | null;
  onUpdate?: () => void;
  formatBattleTime: (ms: number) => string;
  onSelectHistory: (idx: number, report: any) => void;
  historyIdx?: number;
  onOpenReleasePage: () => void;
  downloadState: DownloadState;
  checkStatus: CheckStatus;
  onRetryDownload: () => void;
  currentVersion?: string;
  onCheckUpdate?: () => void;
  players: Player[];
}

const SidePanelComponent = ({
  type,
  smallWindow = false,
  player,
  players,
  onClose,
  combatTime,
  updateInfo,
  onSelectHistory,
  onUpdate,
  downloadState,
  onRetryDownload,
  onOpenReleasePage,
  historyIdx,
  formatBattleTime,
  currentVersion,
  checkStatus,
  onCheckUpdate,
}: SidePanelProps) => {
  const [visible, setVisible] = useState(false);
  const [rendered, setRendered] = useState(false);
  const [currentType, setCurrentType] = useState<PanelType>(null);
  const [currentPlayer, setCurrentPlayer] = useState<Player | null>(null);

  const sidePanelX = useSettingsStore((s) => s.sidePanelX);
  const sidePanelY = useSettingsStore((s) => s.sidePanelY);
  const sidePanelPositioned = useSettingsStore((s) => s.sidePanelPositioned);
  const meterWidth = useSettingsStore((s) => s.meterWidth);
  const overlayLayout = useSettingsStore((s) => s.overlayLayout);
  const setSidePanelPosition = useSettingsStore((s) => s.setSidePanelPosition);
  const { panelWidth, panelHeight, onMouseDownCorner } = useSidePanelResize(currentType);
  const defaultSidePanelX = getDefaultSidePanelX(meterWidth);
  const defaultSidePanelY = getDefaultSidePanelY(
    overlayLayout,
    panelHeight + SIDE_PANEL_HEADER_HEIGHT,
  );

  const { panelRef, onMouseDownPanel } = useDraggablePanel({
    initialX: sidePanelPositioned ? sidePanelX : defaultSidePanelX,
    initialY: sidePanelPositioned ? sidePanelY : defaultSidePanelY,
    onPositionChange: setSidePanelPosition,
    smallWindow,
    // 작은 창: 드래그 중 화면좌표를 store 에 반영(미저장) → useOverlayWindow 가 측정해 창 추종.
    onDragMove: smallWindow
      ? (x, y) =>
          useSettingsStore.setState({ sidePanelX: x, sidePanelY: y, sidePanelPositioned: true })
      : undefined,
    constrainToViewport: !smallWindow,
  });

  const rawX = sidePanelPositioned ? sidePanelX : defaultSidePanelX;
  const rawY = sidePanelPositioned ? sidePanelY : defaultSidePanelY;
  // 작은 창(union): 뷰포트가 작은 창이라 clamp 하면 안 됨(창이 콘텐츠 따라 커짐). 전체화면만 clamp.
  const { x: panelX, y: panelY } = smallWindow
    ? { x: rawX, y: rawY }
    : clampPanelPosition(rawX, rawY, panelWidth, panelHeight + SIDE_PANEL_HEADER_HEIGHT);

  const settingsHeaderCloseRef = useRef<(() => void) | null>(null);
  const registerSettingsHeaderClose = useCallback((handler: (() => void) | null) => {
    settingsHeaderCloseRef.current = handler;
  }, []);

  const handleHeaderClose = () => {
    if (currentType === "settings") {
      settingsHeaderCloseRef.current?.();
      return;
    }
    onClose();
  };

  useEffect(() => {
    let showTimer: ReturnType<typeof setTimeout> | null = null;
    let hideTimer: ReturnType<typeof setTimeout> | null = null;
    if (type) {
      setCurrentType(type);
      setCurrentPlayer(player);
      if (!rendered) {
        setRendered(true);
        showTimer = setTimeout(() => setVisible(true), 10);
      }
    } else {
      setVisible(false);
      hideTimer = setTimeout(() => {
        setRendered(false);
        setCurrentType(null);
      }, 200);
    }
    return () => {
      if (showTimer) clearTimeout(showTimer);
      if (hideTimer) clearTimeout(hideTimer);
    };
  }, [type, player]);
  if (!rendered) return null;

  const detailsTitle =
    currentType === "details"
      ? currentPlayer
        ? `${currentPlayer.name} 상세내역`
        : "상세내역"
      : "";

  const positionStyle: React.CSSProperties = {
    // 화면좌표 - 창 origin(--ovx; 전체화면=0). 작은 창에선 창-상대 위치로 당겨진다.
    left: `calc(${panelX}px - var(--ovx, 0px))`,
    top: `calc(${panelY}px - var(--ovy, 0px))`,
    width: panelWidth,
  };

  const rootClass = cn(
    "rounded-md border border-[var(--meter-border)] text-[var(--meter-fg)] shadow-[0_20px_48px_rgba(0,0,0,0.34)] backdrop-blur-md",
    "transition-opacity duration-200 ease-in-out",
    "bg-[var(--panel-bg)]",
    visible ? "opacity-100" : "opacity-0 pointer-events-none",
  );

  return (
    <div
      ref={panelRef}
      data-overlay-content
      style={{
        ...positionStyle,
        contain: "layout style paint",
      }}
      className={cn(rootClass, "fixed left-0 top-0 flex flex-col overflow-hidden")}
      onMouseDown={onMouseDownPanel}>
      <div className="flex shrink-0 items-center gap-2 border-b border-[var(--meter-soft-border)] bg-[var(--meter-section-bg)] px-3 py-2 pl-5">
        {currentType === "update" ? (
          <>
            <div
              className={`w-2 h-2 rounded-full shrink-0 ${UPDATE_PANEL_DOT_CLS[downloadState.status]}`}
            />
            <span className="flex-1 truncate text-sm font-semibold">
              {UPDATE_PANEL_HEADER_TITLE[downloadState.status]}
            </span>
          </>
        ) : (
          <span className="flex-1 truncate text-sm font-semibold">
            {currentType === "details"
              ? detailsTitle
              : currentType === "settings"
                ? "설정"
                : currentType === "history"
                  ? "전투 기록"
                  : null}
          </span>
        )}
        <Button
          size="icon"
          variant="ghost"
          title="닫기"
          className="meter-control h-7 w-7 rounded-md"
          onMouseDown={(e) => e.stopPropagation()}
          onClick={handleHeaderClose}>
          <CircleX className="size-4.5" />
        </Button>
      </div>

      <div
        style={{ height: panelHeight }}
        className={SIDE_BODY_VIEWPORT}>
        {currentType === "details" && (
          <div
            key={currentPlayer?.id}
            className={SIDE_SHELL.details}>
            <DetailsPanel
              player={currentPlayer}
              players={players}
              combatTime={combatTime}
              historyIdx={historyIdx}
            />
          </div>
        )}
        {currentType === "settings" && (
          <div className={SIDE_SHELL.settings}>
            <SettingsPanel
              onClose={onClose}
              currentVersion={currentVersion}
              updateInfo={updateInfo}
              onCheckUpdate={onCheckUpdate}
              registerHeaderClose={registerSettingsHeaderClose}
            />
          </div>
        )}
        {currentType === "update" && (
          <div className={SIDE_SHELL.update}>
            <UpdatePanel
              updateInfo={updateInfo ?? null}
              checkStatus={checkStatus}
              currentVersion={currentVersion}
              onClose={onClose}
              downloadState={downloadState}
              onRetryDownload={onRetryDownload}
              onUpdate={onUpdate ?? (() => {})}
              onOpenReleasePage={onOpenReleasePage}
            />
          </div>
        )}
        {currentType === "history" && (
          <div
            key={currentPlayer?.id}
            className={SIDE_SHELL.history}>
            <HistoryPanel
              formatBattleTime={formatBattleTime}
              onSelectHistory={onSelectHistory}
            />
          </div>
        )}
      </div>
      {currentType !== "update" && <ResizeHandle onMouseDown={onMouseDownCorner}></ResizeHandle>}
    </div>
  );
};

const areSidePanelPropsEqual = (prev: SidePanelProps, next: SidePanelProps) => {
  if (prev.type !== next.type) return false;
  if (prev.smallWindow !== next.smallWindow) return false;
  if (prev.player !== next.player) return false;
  if (prev.onClose !== next.onClose) return false;

  if (next.type === "details") {
    return (
      prev.players === next.players &&
      prev.combatTime === next.combatTime &&
      prev.historyIdx === next.historyIdx &&
      prev.formatBattleTime === next.formatBattleTime &&
      prev.onSelectHistory === next.onSelectHistory
    );
  }

  if (next.type === "update") {
    return (
      prev.updateInfo === next.updateInfo &&
      prev.downloadState === next.downloadState &&
      prev.checkStatus === next.checkStatus &&
      prev.currentVersion === next.currentVersion &&
      prev.onUpdate === next.onUpdate &&
      prev.onRetryDownload === next.onRetryDownload &&
      prev.onOpenReleasePage === next.onOpenReleasePage
    );
  }

  if (next.type === "settings") {
    return (
      prev.currentVersion === next.currentVersion &&
      prev.updateInfo === next.updateInfo &&
      prev.onCheckUpdate === next.onCheckUpdate
    );
  }

  if (next.type === "history") {
    return (
      prev.formatBattleTime === next.formatBattleTime &&
      prev.onSelectHistory === next.onSelectHistory
    );
  }

  return true;
};

export const SidePanel = memo(SidePanelComponent, areSidePanelPropsEqual);
