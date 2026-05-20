import { memo, useEffect, useLayoutEffect, useMemo, useState } from "react";
import { useJoinRequestStore } from "@/stores/useJoinRequestStore";
import { Settings, CircleX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { getServerLabel } from "@/utils/parser";
import { getJobIconSrc } from "@/utils/icons";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
import { getSkillName, SKILL_MAP, SKILL_ORDER_MAP } from "@/constants/codes";
import { JoinRequestSkillSettings } from "./JoinRequestSkillSettings";
import { SkillBadges } from "./SkillBadges";
import { cn } from "@/lib/utils";
import { getClassColor } from "@/utils/classColor";
import { useResizableJoinPanel } from "@/hooks/resize/useResizableJoinPanel";
import { useDraggablePanel } from "@/hooks/drag/useDraggablePanel";
import { ResizeHandle } from "../ResizeHandle";

const TOTAL_SEC = 20;
const DEFAULT_JOIN_PANEL_GAP = 8;
const DEFAULT_JOIN_PANEL_X = 0;
const DEFAULT_JOIN_PANEL_Y = 8;
const JOIN_PANEL_HEADER_HEIGHT = 44;

const getMeasuredDefaultJoinPanelY = (layout: "standard" | "bottom", panelHeight: number) => {
  const meterRoot = document.querySelector("[data-meter-root-anchor]");
  if (!meterRoot) return DEFAULT_JOIN_PANEL_Y;

  const rect = meterRoot.getBoundingClientRect();
  if (layout === "bottom") {
    return Math.max(DEFAULT_JOIN_PANEL_Y, rect.top - panelHeight - DEFAULT_JOIN_PANEL_GAP);
  }

  return rect.bottom + DEFAULT_JOIN_PANEL_GAP;
};

const useDefaultJoinPanelY = (layout: "standard" | "bottom", panelHeight: number) => {
  const [defaultY, setDefaultY] = useState(DEFAULT_JOIN_PANEL_Y);

  useLayoutEffect(() => {
    const meterRoot = document.querySelector("[data-meter-root-anchor]");
    if (!meterRoot) return;

    let rafId: number | null = null;
    const updateDefaultY = () => {
      if (rafId !== null) cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(() => {
        setDefaultY(getMeasuredDefaultJoinPanelY(layout, panelHeight));
        rafId = null;
      });
    };

    updateDefaultY();

    const resizeObserver = new ResizeObserver(updateDefaultY);
    resizeObserver.observe(meterRoot);
    window.addEventListener("resize", updateDefaultY);

    return () => {
      if (rafId !== null) cancelAnimationFrame(rafId);
      resizeObserver.disconnect();
      window.removeEventListener("resize", updateDefaultY);
    };
  }, [layout, panelHeight]);

  return defaultY;
};

const clampPanelPosition = (x: number, y: number, width: number, height: number) => {
  const maxX = Math.max(0, window.innerWidth - width);
  const maxY = Math.max(0, window.innerHeight - height);

  return {
    x: Math.min(Math.max(0, x), maxX),
    y: Math.min(Math.max(0, y), maxY),
  };
};

const TimerBar = ({ arrivedAt, now }: { arrivedAt: number; now: number }) => {
  const remaining = Math.max(0, TOTAL_SEC - (now - arrivedAt) / 1000);
  const pct = (remaining / TOTAL_SEC) * 100;
  const color =
    pct > 50
      ? "linear-gradient(to right, #2dd4bf, #0f766e)"
      : pct > 25
        ? "linear-gradient(to right, #f59e0b, #b45309)"
        : "linear-gradient(to right, #fb7185, #be123c)";

  return (
    <div className="flex items-center gap-2">
      <div className="relative h-1.5 flex-1 overflow-hidden rounded bg-[var(--meter-stat-bg)] ring-1 ring-[var(--meter-soft-border)]">
        <div
          className="absolute inset-y-0 left-0 rounded transition-[width] duration-300"
          style={{ width: `${pct}%`, background: color }}
        />
      </div>
      <span className="text-shadow-meter text-xs w-6 text-right">{Math.ceil(remaining)}s</span>
    </div>
  );
};

export const JoinRequestPanel = memo(() => {
  const requests = useJoinRequestStore((s) => s.requests);
  const isOpen = useJoinRequestStore((s) => s.isOpen);
  const setOpen = useJoinRequestStore((s) => s.setOpen);
  const [skillSettingsOpen, setSkillSettingsOpen] = useState(false);
  const [rendered, setRendered] = useState(false);
  const [visible, setVisible] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const { joinPanelHeight, joinPanelWidth, onMouseDownCorner } = useResizableJoinPanel();

  const {
    visibleSkillCodes,
    joinPanelX,
    joinPanelY,
    joinPanelPositioned,
    overlayLayout,
    overlayTheme,
    setJoinPanelPosition,
  } = useSettingsStore(
    useShallow((s) => ({
      visibleSkillCodes: s.visibleSkillCodes,
      joinPanelX: s.joinPanelX,
      joinPanelY: s.joinPanelY,
      joinPanelPositioned: s.joinPanelPositioned,
      overlayLayout: s.overlayLayout,
      overlayTheme: s.overlayTheme,
      setJoinPanelPosition: s.setJoinPanelPosition,
    })),
  );
  const isLightOverlay = overlayTheme === "light";
  const defaultJoinPanelX = DEFAULT_JOIN_PANEL_X;
  const defaultJoinPanelY = useDefaultJoinPanelY(overlayLayout, joinPanelHeight);
  const { x: panelX, y: panelY } = clampPanelPosition(
    joinPanelPositioned ? joinPanelX : defaultJoinPanelX,
    joinPanelPositioned ? joinPanelY : defaultJoinPanelY,
    joinPanelWidth,
    JOIN_PANEL_HEADER_HEIGHT,
  );

  const { panelRef, onMouseDownPanel } = useDraggablePanel({
    initialX: panelX,
    initialY: panelY,
    onPositionChange: setJoinPanelPosition,
    viewportConstraintHeight: JOIN_PANEL_HEADER_HEIGHT,
  });

  useEffect(() => {
    let showTimer: ReturnType<typeof setTimeout> | null = null;
    let hideTimer: ReturnType<typeof setTimeout> | null = null;
    if (isOpen) {
      setRendered(true);
      showTimer = setTimeout(() => setVisible(true), 10);
    } else {
      setVisible(false);
      hideTimer = setTimeout(() => setRendered(false), 200);
    }
    return () => {
      if (showTimer) clearTimeout(showTimer);
      if (hideTimer) clearTimeout(hideTimer);
    };
  }, [isOpen]);

  useEffect(() => {
    if (!rendered || requests.length === 0) return;
    const timer = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(timer);
  }, [rendered, requests.length]);

  const orderedRequests = useMemo(() => [...requests].reverse(), [requests]);
  const latestArrivedAt = orderedRequests[0]?.arrivedAt ?? 0;
  const effectiveNow = Math.max(now, latestArrivedAt);

  if (!rendered) return null;

  const positionStyle: React.CSSProperties = {
    left: panelX,
    top: panelY,
    width: joinPanelWidth,
    height: joinPanelHeight,
  };

  const rootClass = cn(
    "rounded-md border border-[var(--meter-border)] text-[var(--meter-fg)] font-semibold shadow-[0_20px_44px_rgba(0,0,0,0.32)] backdrop-blur-md",
    "transition-opacity duration-200 ease-in-out",
    "bg-[var(--join-panel-bg)]",
    visible ? "opacity-100" : "opacity-0 pointer-events-none",
  );

  const headerClass = "transition duration-150";
  return (
    <div
      ref={panelRef}
      style={{
        ...positionStyle,
      }}
      className={cn(rootClass, "fixed flex flex-col")}
      onMouseDown={onMouseDownPanel}>
      <div>
        <div
          className={`${headerClass} flex items-center justify-between border-b border-[var(--meter-soft-border)] bg-[var(--meter-section-bg)] px-3 py-1.5`}>
          <div className="flex items-center h-8">
            <span className={`mr-2 pl-2 flex-1 text-sm`}>파티 신청</span>
            <span className="rounded bg-[var(--meter-stat-bg)] px-2 py-0.5 text-center text-xs ring-1 ring-[var(--meter-soft-border)]">
              {requests.length}건
            </span>
          </div>
          <div
            className="flex items-center gap-2 h-8"
            onMouseDown={(e) => e.stopPropagation()}>
            <Button
              size="icon"
              variant="ghost"
              className="meter-control h-7 w-7 rounded-md"
              onMouseDown={(e) => e.stopPropagation()}
              onClick={() => setSkillSettingsOpen((v) => !v)}>
              <Settings className="size-4.5" />
            </Button>
            <Button
              size="icon"
              variant="ghost"
              className="meter-control h-7 w-7 rounded-md"
              onClick={() => setOpen(false)}>
              <CircleX className="size-4.5" />
            </Button>
          </div>
        </div>

        <JoinRequestSkillSettings
          open={skillSettingsOpen}
          onOpenChange={setSkillSettingsOpen}
        />
      </div>
      <div className="mb-4 min-h-0 flex-1 overflow-y-auto pt-2 scrollbar-gutter:stable">
        {requests.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <span className="text-sm">파티 신청이 없습니다</span>
          </div>
        ) : (
          <div className="py-1">
            {orderedRequests.map((r, i) => {
              const allBadges = Object.entries(r.skill ?? {})
                .filter(([code]) => visibleSkillCodes.includes(Number(code)))
                .sort(
                  ([a], [b]) =>
                    (SKILL_ORDER_MAP.get(Number(a)) ?? 999) -
                    (SKILL_ORDER_MAP.get(Number(b)) ?? 999),
                )
                .map(([code, lv]) => ({
                  code,
                  name: getSkillName(Number(code)) ?? code,
                  lv,
                  isStigma: SKILL_MAP.get(Number(code))?.isStigma ?? false,
                }));

              const normalBadges = allBadges.filter((b) => !b.isStigma);
              const stigmaBadges = allBadges.filter((b) => b.isStigma);

              return (
                <div
                  key={r.requester}
                  className={`${i == 0 ? "py-0" : "py-1.5"} px-3`}>
                  <div
                    className={`${getClassColor(r.job ?? undefined, isLightOverlay ? "light" : "dark")} rounded-md px-3 py-2`}>
                    <div className="flex items-center gap-1">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        <img
                          src={getJobIconSrc(r.job ?? undefined)}
                          className="w-6 h-6 object-contain shrink-0"
                          style={{ filter: "drop-shadow(0 0 3px rgba(20,20,20,0.6))" }}
                        />
                        <span className="text-sm text-shadow-meter truncate">
                          {r.nickname}
                          {getServerLabel(r.server) ? `[${getServerLabel(r.server)}]` : ""}
                        </span>
                      </div>
                      <span
                        className="shrink-0 rounded bg-[var(--meter-stat-bg)] px-1.5 py-0.5 text-sm tabular-nums ring-1 ring-[var(--meter-soft-border)]"
                        style={{ color: isLightOverlay ? "#0f766e" : "#2dd4bf" }}>
                        {`${(r.power / 1000).toFixed(1)}k`}
                      </span>
                    </div>

                    {(normalBadges.length > 0 || stigmaBadges.length > 0) && (
                      <div className="mt-1.5 mb-1.5 space-y-1">
                        {normalBadges.length > 0 && (
                          <div>
                            <span className="mb-0.5 block text-[10px] text-current opacity-45">일반</span>
                            <SkillBadges
                              badges={normalBadges}
                              job={r.job ?? ""}
                            />
                          </div>
                        )}
                        {stigmaBadges.length > 0 && (
                          <div>
                            <span className="mb-0.5 block text-[10px] text-current opacity-45">스티그마</span>
                            <SkillBadges
                              badges={stigmaBadges}
                              job={r.job ?? ""}
                            />
                          </div>
                        )}
                      </div>
                    )}
                    <TimerBar
                      arrivedAt={r.arrivedAt}
                      now={effectiveNow}
                    />
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
      <ResizeHandle onMouseDown={onMouseDownCorner}></ResizeHandle>
    </div>
  );
});
