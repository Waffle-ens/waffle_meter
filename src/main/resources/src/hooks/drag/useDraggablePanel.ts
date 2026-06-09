import { useRef, useCallback } from "react";

interface UseDraggablePanelOptions {
  initialX: number;
  initialY: number;
  onPositionChange: (x: number, y: number) => void;
  minX?: number;
  minY?: number;
  constrainToViewport?: boolean;
  viewportConstraintWidth?: number;
  viewportConstraintHeight?: number;
  /** 작은 창(union) 모드: 화면좌표 델타로 드래그하고 onDragMove 로 매 프레임 반영. */
  smallWindow?: boolean;
  /** 작은 창 모드에서 드래그 중(미저장) 위치 반영. onPositionChange 는 mouseup 에 영구 저장. */
  onDragMove?: (x: number, y: number) => void;
}

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

const DRAG_THRESHOLD = 5;

// 현재 적용된 창 origin(--ovx/--ovy). 창-상대 rect → 화면좌표 복원에 사용.
const readOrigin = () => {
  const s = getComputedStyle(document.documentElement);
  return {
    x: parseFloat(s.getPropertyValue("--ovx")) || 0,
    y: parseFloat(s.getPropertyValue("--ovy")) || 0,
  };
};

const isInteractiveTarget = (target: Element): boolean => {
  if (
    target.closest(
      "button, input, select, textarea, a, label, [role='slider'], [role='checkbox'], [data-no-drag], .no-drag, .cursor-pointer, .resizeHandle",
    )
  )
    return true;
  const style = window.getComputedStyle(target);
  const overflow = style.overflowY;
  if ((overflow === "auto" || overflow === "scroll") && target.scrollHeight > target.clientHeight)
    return true;

  return false;
};

export const useDraggablePanel = ({
  initialX,
  initialY,
  onPositionChange,
  minX = 0,
  minY = 0,
  constrainToViewport = true,
  viewportConstraintWidth,
  viewportConstraintHeight,
  smallWindow = false,
  onDragMove,
}: UseDraggablePanelOptions) => {
  const posRef = useRef({ x: Math.max(minX, initialX), y: Math.max(minY, initialY) });
  const panelRef = useRef<HTMLDivElement>(null);
  const isPositioned = initialX !== 0 || initialY !== 0;
  const rafId = useRef<number | null>(null);

  const startDrag = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      e.stopPropagation();

      const panel = panelRef.current;
      if (!panel) return;

      // 작은 창(union): 화면좌표 델타로 드래그. 창이 콘텐츠 따라 움직여도 좌표 피드백이 없도록.
      if (smallWindow) {
        const origin = readOrigin();
        const rect = panel.getBoundingClientRect();
        const startPanelScreenX = rect.left + origin.x;
        const startPanelScreenY = rect.top + origin.y;
        const startMouseX = e.screenX;
        const startMouseY = e.screenY;
        posRef.current = { x: startPanelScreenX, y: startPanelScreenY };
        let isDragging = false;

        const handleMouseMove = (moveEvent: MouseEvent) => {
          const deltaX = moveEvent.screenX - startMouseX;
          const deltaY = moveEvent.screenY - startMouseY;

          if (!isDragging) {
            if (Math.abs(deltaX) <= DRAG_THRESHOLD && Math.abs(deltaY) <= DRAG_THRESHOLD) return;
            isDragging = true;
            panel.style.willChange = "left, top";
          }

          if (rafId.current !== null) cancelAnimationFrame(rafId.current);
          rafId.current = requestAnimationFrame(() => {
            const nextX = startPanelScreenX + deltaX;
            const nextY = startPanelScreenY + deltaY;
            posRef.current = { x: nextX, y: nextY };
            // 화면좌표를 store 에 반영 → React 가 left:calc(screen - --ovx) 로 렌더, useOverlayWindow 가 창 추종.
            onDragMove?.(nextX, nextY);
          });
        };

        const handleMouseUp = () => {
          if (rafId.current !== null) {
            cancelAnimationFrame(rafId.current);
            rafId.current = null;
          }
          if (isDragging) {
            panel.style.willChange = "auto";
            onPositionChange(posRef.current.x, posRef.current.y);
          }
          document.removeEventListener("mousemove", handleMouseMove);
          document.removeEventListener("mouseup", handleMouseUp);
        };

        document.addEventListener("mousemove", handleMouseMove);
        document.addEventListener("mouseup", handleMouseUp);
        return;
      }

      const rect = panel.getBoundingClientRect();
      const startMouseX = e.clientX;
      const startMouseY = e.clientY;
      const startPanelX = rect.left;
      const startPanelY = rect.top;
      const constraintWidth = viewportConstraintWidth ?? rect.width;
      const constraintHeight = viewportConstraintHeight ?? rect.height;
      const maxX = constrainToViewport
        ? Math.max(minX, window.innerWidth - constraintWidth)
        : Infinity;
      const maxY = constrainToViewport
        ? Math.max(minY, window.innerHeight - constraintHeight)
        : Infinity;

      let isDragging = false;

      const handleMouseMove = (moveEvent: MouseEvent) => {
        const deltaX = moveEvent.clientX - startMouseX;
        const deltaY = moveEvent.clientY - startMouseY;

        if (!isDragging) {
          if (Math.abs(deltaX) <= DRAG_THRESHOLD && Math.abs(deltaY) <= DRAG_THRESHOLD) return;
          isDragging = true;
          posRef.current = { x: clamp(startPanelX, minX, maxX), y: clamp(startPanelY, minY, maxY) };
          panel.style.willChange = "left, top";
        }

        if (rafId.current !== null) cancelAnimationFrame(rafId.current);
        rafId.current = requestAnimationFrame(() => {
          const nextX = clamp(startPanelX + deltaX, minX, maxX);
          const nextY = clamp(startPanelY + deltaY, minY, maxY);
          posRef.current = { x: nextX, y: nextY };
          if (panel) {
            panel.style.left = `${nextX}px`;
            panel.style.top = `${nextY}px`;
            panel.style.transform = "none";
          }
        });
      };

      const handleMouseUp = () => {
        if (rafId.current !== null) {
          cancelAnimationFrame(rafId.current);
          rafId.current = null;
        }

        if (isDragging) {
          panel.style.willChange = "auto";
          onPositionChange(posRef.current.x, posRef.current.y);
        }

        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
      };

      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
    },
    [
      constrainToViewport,
      minX,
      minY,
      onPositionChange,
      viewportConstraintHeight,
      viewportConstraintWidth,
      smallWindow,
      onDragMove,
    ],
  );

  const onMouseDownHandle = startDrag;

  const onMouseDownPanel = useCallback(
    (e: React.MouseEvent) => {
      const panel = panelRef.current;
      if (!panel) return;
      if (isInteractiveTarget(e.target as Element)) return;
      startDrag(e);
    },
    [startDrag],
  );

  return { panelRef, onMouseDownHandle, onMouseDownPanel, isPositioned };
};
