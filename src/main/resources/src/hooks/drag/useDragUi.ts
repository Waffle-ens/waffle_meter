import type { OverlayMode } from "@/hooks/overlay/useOverlayWindow";
import { setOverlayDragging } from "@/hooks/overlay/overlayDrag";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { clampMeterRootPosition } from "@/utils/meterBounds";
import { useEffect, useRef } from "react";
import { useShallow } from "zustand/react/shallow";

/**
 * 미터기 드래그. 모드별로:
 * - `fullscreen`: DOM .drag-area 의 left/top 을 옮기고 뷰포트 안으로 clamp(기존 동작).
 * - `meterOnly`: 미터기는 창 (0,0) 고정 → 네이티브 Stage 를 직접 이동(moveWindowTo). screen 좌표 델타.
 * - `union`: 미터기 화면좌표(uiX/uiY)를 갱신 → useOverlayWindow 가 측정해 창을 따라 움직인다. screen 델타.
 *
 * meterOnly/union 모두 창이 마우스를 따라가며 생기는 client 좌표 피드백을 피하려고 screen 좌표 델타를 쓴다.
 */
export const useDragUi = (mode: OverlayMode) => {
  const wasDraggingRef = useRef(false);
  const modeRef = useRef(mode);
  modeRef.current = mode;
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
      if (modeRef.current === "fullscreen") {
        const rect = rootEl.getBoundingClientRect();
        startMouseX = e.clientX;
        startMouseY = e.clientY;
        startUiX = rect.left;
        startUiY = rect.top;
      } else {
        // meterOnly / union: 화면좌표 기준, 시작 창/미터 위치 = 저장된 uiX/uiY.
        const { uiX, uiY } = useSettingsStore.getState();
        startMouseX = e.screenX;
        startMouseY = e.screenY;
        startUiX = uiX;
        startUiY = uiY;
      }
      currentX = startUiX;
      currentY = startUiY;
    };

    const handleMouseMove = (e: globalThis.MouseEvent) => {
      if (!isDragging) return;
      const m = modeRef.current;

      if (m !== "fullscreen") {
        const deltaX = e.screenX - startMouseX;
        const deltaY = e.screenY - startMouseY;
        if (!wasDraggingRef.current && (Math.abs(deltaX) > 3 || Math.abs(deltaY) > 3)) {
          wasDraggingRef.current = true;
        }
        if (rafId.current !== null) cancelAnimationFrame(rafId.current);
        rafId.current = requestAnimationFrame(() => {
          currentX = startUiX + deltaX;
          currentY = startUiY + deltaY;
          if (m === "meterOnly") {
            // 네이티브 창만 이동(빠른 경로). 미터 DOM 은 창 (0,0) 그대로.
            // 반환된 clamp 위치로 currentX/Y 보정 → mouseup 에 저장되는 uiX/uiY 도 화면 안 값.
            const applied = window.javaBridge?.moveWindowTo?.(currentX, currentY);
            if (typeof applied === "string") {
              const [ax, ay] = applied.split(",").map(Number);
              if (Number.isFinite(ax) && Number.isFinite(ay)) {
                currentX = ax;
                currentY = ay;
              }
            }
          } else {
            // union: 드래그 중엔 전체화면 고정(떨림 방지) 후 화면좌표 갱신. mouseup 에 union 재적합+영구 저장.
            setOverlayDragging(true);
            useSettingsStore.setState({ uiX: currentX, uiY: currentY });
          }
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
        if (modeRef.current === "fullscreen") rootEl.style.willChange = "auto";
      }
      if (rafId.current !== null) {
        cancelAnimationFrame(rafId.current);
        rafId.current = null;
      }
      isDragging = false;
      setOverlayDragging(false); // union 재적합 트리거(다른 모드면 no-op)

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
