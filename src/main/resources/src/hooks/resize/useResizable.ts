import { useCallback, useEffect, useRef, useState } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";

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
        startX: e.clientX,
        startY: e.clientY,
        startWidth: state.meterWidth,
        startRowHeight: state.rowHeight,
        startUiX: state.uiX,
        startUiY: state.uiY,
        verticalUnits: estimateVerticalUnits(),
      };
      setIsDragging(true);
    },
    [],
  );

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      const current = resizeRef.current;
      if (!current) return;
      if (rafId.current !== null) cancelAnimationFrame(rafId.current);

      const clientX = e.clientX;
      const clientY = e.clientY;

      rafId.current = requestAnimationFrame(() => {
        const dx = clientX - current.startX;
        const dy = clientY - current.startY;
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
      bridge?.syncOverlayBounds?.();
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
