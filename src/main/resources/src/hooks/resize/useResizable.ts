import { useCallback, useEffect, useRef, useState } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { setOverlayDragging } from "@/hooks/overlay/overlayDrag";

export type MeterResizeDirection = "n" | "s" | "e" | "w" | "ne" | "nw" | "se" | "sw";

const MIN_METER_WIDTH = 260;
const MAX_METER_WIDTH = 900;
const MIN_ROW_HEIGHT = 28;
const MAX_ROW_HEIGHT = 74;

const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));

const estimateVerticalUnits = () => {
  const root = document.querySelector<HTMLElement>("[data-meter-root-anchor]");
  const rowCount = root?.querySelectorAll(".meter-row").length ?? 0;
  const targetCount = root?.querySelectorAll("[data-meter-target='true']").length ?? 0;
  const timerCount = root?.querySelectorAll("[data-meter-timer='true']").length ?? 0;
  return Math.max(2, rowCount + targetCount + timerCount);
};

export const useResizable = () => {
  const meterWidth = useSettingsStore((s) => s.meterWidth);
  const rowHeight = useSettingsStore((s) => s.rowHeight);
  const resizeRef = useRef<{
    direction: MeterResizeDirection;
    startX: number;
    startY: number;
    startWidth: number;
    startRowHeight: number;
    startUiX: number;
    startUiY: number;
    verticalUnits: number;
  } | null>(null);
  const rafId = useRef<number | null>(null);
  const [isDragging, setIsDragging] = useState(false);

  const onMouseDown = useCallback(
    (e: React.MouseEvent, direction: MeterResizeDirection = "e") => {
      e.preventDefault();
      e.stopPropagation();
      const state = useSettingsStore.getState();
      resizeRef.current = {
        direction,
        // screen 좌표 사용: 작은 창 모드에서 리사이즈 중 창이 움직이거나 커져도 델타가 흔들리지 않게.
        // (전체화면 모드는 창 원점이 고정이라 client 와 델타 동일 → 동작 변화 없음)
        startX: e.screenX,
        startY: e.screenY,
        startWidth: state.meterWidth,
        startRowHeight: state.rowHeight,
        startUiX: state.uiX,
        startUiY: state.uiY,
        verticalUnits: estimateVerticalUnits(),
      };
      setIsDragging(true);
      // union 모드: 리사이즈 중에도 창 origin 고정(전체화면) → 미터 w/n 리사이즈 시 패널 떨림 방지.
      setOverlayDragging(true);
    },
    [],
  );

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      const current = resizeRef.current;
      if (!current) return;
      if (rafId.current !== null) cancelAnimationFrame(rafId.current);

      const pointerX = e.screenX;
      const pointerY = e.screenY;

      rafId.current = requestAnimationFrame(() => {
        const dx = pointerX - current.startX;
        const dy = pointerY - current.startY;
        const horizontalSign = current.direction.includes("w") ? -1 : current.direction.includes("e") ? 1 : 0;
        const verticalSign = current.direction.includes("n") ? -1 : current.direction.includes("s") ? 1 : 0;

        let nextWidth = current.startWidth;
        let nextRowHeight = current.startRowHeight;
        let nextUiX = current.startUiX;
        let nextUiY = current.startUiY;

        if (horizontalSign !== 0) {
          nextWidth = clamp(current.startWidth + dx * horizontalSign, MIN_METER_WIDTH, MAX_METER_WIDTH);
          if (horizontalSign < 0) {
            nextUiX = current.startUiX + (current.startWidth - nextWidth);
          }
        }

        if (verticalSign !== 0) {
          const rowDelta = (dy * verticalSign) / current.verticalUnits;
          nextRowHeight = clamp(current.startRowHeight + rowDelta, MIN_ROW_HEIGHT, MAX_ROW_HEIGHT);
          if (verticalSign < 0) {
            nextUiY = current.startUiY + (current.startRowHeight - nextRowHeight) * current.verticalUnits;
          }
        }

        useSettingsStore.setState({
          meterWidth: Math.round(nextWidth),
          rowHeight: Math.round(nextRowHeight),
          uiX: Math.round(nextUiX),
          uiY: Math.round(nextUiY),
        });
      });
    };

    const onMouseUp = () => {
      if (!resizeRef.current) return;
      if (rafId.current !== null) {
        cancelAnimationFrame(rafId.current);
        rafId.current = null;
      }
      resizeRef.current = null;
      setIsDragging(false);
      const state = useSettingsStore.getState();
      const bridge = (window as any).javaBridge;
      bridge?.saveProps?.("meterWidth", String(state.meterWidth));
      bridge?.saveProps?.("rowHeight", String(state.rowHeight));
      bridge?.saveProps?.("uiX", String(state.uiX));
      bridge?.saveProps?.("uiY", String(state.uiY));
      // 작은 창이 기본 동작 — useOverlayWindow 가 창 크기를 추종하므로 전체화면 fit 을 직접 호출하지 않는다.
      setOverlayDragging(false); // union 재적합 트리거(휴면)
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
      if (rafId.current !== null) cancelAnimationFrame(rafId.current);
    };
  }, []);

  return { meterWidth, rowHeight, onMouseDown, isDragging };
};
