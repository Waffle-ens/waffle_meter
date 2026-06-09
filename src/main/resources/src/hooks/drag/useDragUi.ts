import { useSettingsStore } from "@/stores/useSettingsStore";
import { clampMeterRootPosition } from "@/utils/meterBounds";
import { useEffect, useRef } from "react";
import { useShallow } from "zustand/react/shallow";

/**
 * 미터기 드래그.
 * - 일반(전체화면) 모드: DOM .drag-area 의 left/top 을 옮기고 뷰포트 안으로 clamp.
 * - compact(작은 창) 모드: 미터기는 창 (0,0) 고정이므로 네이티브 Stage 를 직접 이동(moveWindowTo).
 *   창이 마우스를 따라 움직여 client 좌표는 피드백이 생기므로, screen 좌표 델타로 위치를 계산한다.
 */
export const useDragUi = (compact: boolean) => {
  const wasDraggingRef = useRef(false);
  const compactRef = useRef(compact);
  compactRef.current = compact;
  const { setUiPosition } = useSettingsStore(
    useShallow((s) => ({ setUiPosition: s.setUiPosition })),
  );

  useEffect(() => {
    const anchor = document.querySelector<HTMLElement>("[data-meter-root-anchor]");
    const rootEl = anchor?.closest<HTMLElement>(".drag-area") ?? null;
    if (!anchor || !rootEl) return;

    let isDragging = false;
    let startMouseX = 0;
    let startMouseY = 0;
    let startUiX = 0;
    let startUiY = 0;
    let currentX = 0;
    let currentY = 0;
    const rafId = { current: null as number | null };

    const handleMouseDown = (e: globalThis.MouseEvent) => {
      const target = e.target as HTMLElement;
      // button, input 등 인터랙티브 요소만 제외
      if (
        target.closest(
          "input, button, [role='slider'], [data-no-drag], .settingsPanel, .detailsPanel, .console, .resizeHandle, .drag-handle, .window-drag-handle",
        )
      )
        return;

      isDragging = true;
      if (compactRef.current) {
        // 네이티브 창 이동: screen 좌표 기준, 시작 창 위치 = 저장된 uiX/uiY.
        const { uiX, uiY } = useSettingsStore.getState();
        startMouseX = e.screenX;
        startMouseY = e.screenY;
        startUiX = uiX;
        startUiY = uiY;
      } else {
        const rect = rootEl.getBoundingClientRect();
        startMouseX = e.clientX;
        startMouseY = e.clientY;
        startUiX = rect.left;
        startUiY = rect.top;
      }
      currentX = startUiX;
      currentY = startUiY;
    };

    const handleMouseMove = (e: globalThis.MouseEvent) => {
      if (!isDragging) return;

      if (compactRef.current) {
        const deltaX = e.screenX - startMouseX;
        const deltaY = e.screenY - startMouseY;
        if (!wasDraggingRef.current && (Math.abs(deltaX) > 3 || Math.abs(deltaY) > 3)) {
          wasDraggingRef.current = true;
        }
        if (rafId.current !== null) cancelAnimationFrame(rafId.current);
        rafId.current = requestAnimationFrame(() => {
          currentX = startUiX + deltaX;
          currentY = startUiY + deltaY;
          window.javaBridge?.moveWindowTo?.(currentX, currentY);
        });
        return;
      }

      const deltaX = e.clientX - startMouseX;
      const deltaY = e.clientY - startMouseY;
      if (!wasDraggingRef.current && (Math.abs(deltaX) > 3 || Math.abs(deltaY) > 3)) {
        wasDraggingRef.current = true;
        rootEl.style.willChange = "left, top";
      }

      if (rafId.current !== null) cancelAnimationFrame(rafId.current);
      rafId.current = requestAnimationFrame(() => {
        const next = clampMeterRootPosition(startUiX + deltaX, startUiY + deltaY, {
          rootEl,
          anchorEl: anchor,
        });

        currentX = next.x;
        currentY = next.y;
        rootEl.style.left = `${next.x}px`;
        rootEl.style.top = `${next.y}px`;
      });
    };

    const handleMouseUp = () => {
      if (isDragging && wasDraggingRef.current) {
        setUiPosition(currentX, currentY);
        if (!compactRef.current) rootEl.style.willChange = "auto";
      }
      if (rafId.current !== null) {
        cancelAnimationFrame(rafId.current);
        rafId.current = null;
      }
      isDragging = false;

      setTimeout(() => {
        wasDraggingRef.current = false;
      }, 0);
    };

    anchor.addEventListener("mousedown", handleMouseDown);
    document.addEventListener("mousemove", handleMouseMove);
    document.addEventListener("mouseup", handleMouseUp);

    return () => {
      anchor.removeEventListener("mousedown", handleMouseDown);
      document.removeEventListener("mousemove", handleMouseMove);
      document.removeEventListener("mouseup", handleMouseUp);
    };
  }, [setUiPosition]);

  return { wasDraggingRef };
};
