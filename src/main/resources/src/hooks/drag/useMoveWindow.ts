import { useSettingsStore } from "@/stores/useSettingsStore";
import { useEffect, useRef, type RefObject } from "react";

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

export const useMoveWindow = (target: string | RefObject<HTMLElement | null>) => {
  const wasDraggingRef = useRef(false);
  const setUiPosition = useSettingsStore((s) => s.setUiPosition);

  useEffect(() => {
    const el =
      typeof target === "string" ? document.querySelector<HTMLElement>(target) : target.current;
    if (!el) return;

    let isDragging = false;
    let startX = 0;
    let startY = 0;
    let startUiX = 0;
    let startUiY = 0;
    let currentX = 0;
    let currentY = 0;
    const rafId = { current: null as number | null };

    const handleMouseDown = (e: globalThis.MouseEvent) => {
      const ignoreTarget = (e.target as HTMLElement).closest(
        "input, button, .settingsPanel, .detailsPanel, .console, .resizeHandle, .drag-handle",
      );
      if (ignoreTarget) return;

      const rootEl = el.closest<HTMLElement>(".drag-area");
      if (!rootEl) return;
      const rect = rootEl.getBoundingClientRect();
      isDragging = true;
      startX = e.clientX;
      startY = e.clientY;
      startUiX = rect.left;
      startUiY = rect.top;
      currentX = startUiX;
      currentY = startUiY;
    };

    const handleMouseMove = (e: globalThis.MouseEvent) => {
      if (!isDragging) return;

      const rootEl = el.closest<HTMLElement>(".drag-area");
      if (!rootEl) return;

      const deltaX = e.clientX - startX;
      const deltaY = e.clientY - startY;

      if (Math.abs(deltaX) > 3 || Math.abs(deltaY) > 3) {
        wasDraggingRef.current = true;
        rootEl.style.willChange = "left, top";
      }

      if (rafId.current !== null) cancelAnimationFrame(rafId.current);
      rafId.current = requestAnimationFrame(() => {
        const panelWidth = rootEl.offsetWidth;
        const panelHeight = rootEl.offsetHeight;
        const nextX = clamp(startUiX + deltaX, 0, Math.max(0, window.innerWidth - panelWidth));
        const nextY = clamp(startUiY + deltaY, 0, Math.max(0, window.innerHeight - panelHeight));

        currentX = nextX;
        currentY = nextY;
        rootEl.style.left = `${nextX}px`;
        rootEl.style.top = `${nextY}px`;
      });
    };

    const handleMouseUp = () => {
      const rootEl = el.closest<HTMLElement>(".drag-area");
      if (isDragging) {
        if (wasDraggingRef.current) {
          setUiPosition(currentX, currentY);
          if (rootEl) rootEl.style.willChange = "auto";
        }
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

    el.addEventListener("mousedown", handleMouseDown);
    document.addEventListener("mousemove", handleMouseMove);
    document.addEventListener("mouseup", handleMouseUp);

    return () => {
      el.removeEventListener("mousedown", handleMouseDown);
      document.removeEventListener("mousemove", handleMouseMove);
      document.removeEventListener("mouseup", handleMouseUp);
    };
  }, [target, setUiPosition]);

  return { wasDraggingRef };
};
