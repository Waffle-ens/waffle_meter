import { useEffect, useRef } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";

type Direction = "vertical" | "horizontal";

export const useResizableDetail = () => {
  const { detailHeight, setDetailHeight, detailWidth, setDetailWidth } = useSettingsStore();
  const isResizing = useRef<Direction | null>(null);
  const startY = useRef(0);
  const startX = useRef(0);
  const startHeight = useRef(0);
  const startWidth = useRef(0);

  const onMouseDownVertical = (e: React.MouseEvent) => {
    e.preventDefault();
    isResizing.current = "vertical";
    startY.current = e.clientY;
    startHeight.current = detailHeight;
  };

  const onMouseDownHorizontal = (e: React.MouseEvent) => {
    e.preventDefault();
    isResizing.current = "horizontal";
    startX.current = e.clientX;
    startWidth.current = detailWidth;
  };

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      if (!isResizing.current) return;

      if (isResizing.current === "vertical") {
        const dy = e.clientY - startY.current;
        const newH = Math.max(300, Math.min(900, startHeight.current + dy));
        useSettingsStore.setState({ detailHeight: newH });
      } else {
        const dx = e.clientX - startX.current;
        const newW = Math.max(480, Math.min(1600, startWidth.current + dx));
        useSettingsStore.setState({ detailWidth: newW });
      }
    };

    const onMouseUp = () => {
      if (!isResizing.current) return;
      const { detailHeight, detailWidth } = useSettingsStore.getState();
      setDetailHeight(detailHeight);
      setDetailWidth(detailWidth);
      isResizing.current = null;
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };
  }, []);

  return { detailHeight, detailWidth, onMouseDownVertical, onMouseDownHorizontal };
};
