import { useSettingsStore } from "@/stores/useSettingsStore";
import { useEffect, useRef, type RefObject } from "react";

export const useMoveWindow = (target: string | RefObject<HTMLElement | null>) => {
  const wasDraggingRef = useRef(false);
  const setWindowPosition = useSettingsStore((s) => s.setWindowPosition);

  useEffect(() => {
    const el =
      typeof target === "string" ? document.querySelector<HTMLElement>(target) : target.current;
    if (!el) return;

    let isDragging = false;
    let startX = 0;
    let startY = 0;
    let initialStageX = 0;
    let initialStageY = 0;

    const handleMouseDown = (e: globalThis.MouseEvent) => {
      const ignoreTarget = (e.target as HTMLElement).closest(
        "input, button, .settingsPanel, .detailsPanel, .console, .resizeHandle, .drag-handle",
      );
      if (ignoreTarget) return;

      isDragging = true;
      startX = e.screenX;
      startY = e.screenY;
      initialStageX = window.screenX;
      initialStageY = window.screenY;
      // (window as any).javaBridge?.onDragStart(window.outerWidth, window.outerHeight);
    };

    const handleMouseMove = (e: globalThis.MouseEvent) => {
      if (!isDragging) return;
      if (!(window as any).javaBridge) return;

      const deltaX = e.screenX - startX;
      const deltaY = e.screenY - startY;

      if (Math.abs(deltaX) > 3 || Math.abs(deltaY) > 3) {
        wasDraggingRef.current = true;
      }

      const newX = initialStageX + deltaX;
      const newY = initialStageY + deltaY;

      (window as any).javaBridge.moveWindow(newX, newY);
      // (window as any).javaBridge.onDragMove(newX, newY);
    };

    const handleMouseUp = () => {
      if (isDragging) {
        if (wasDraggingRef.current) {
          setWindowPosition(window.screenX, window.screenY);
        }
        // (window as any).javaBridge?.onDragEnd();
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
  }, [target, setWindowPosition]);

  return { wasDraggingRef };
};
