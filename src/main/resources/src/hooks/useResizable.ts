import { useEffect, useRef, useState } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";

const throttle = <T extends (...args: any[]) => void>(fn: T, ms: number): T => {
  let last = 0;
  return ((...args) => {
    const now = Date.now();
    if (now - last >= ms) { last = now; fn(...args); }
  }) as T;
};

export const useResizable = () => {
  const { meterWidth, setMeterWidth } = useSettingsStore();
  const isResizing = useRef(false);
  const startX = useRef(0);
  const startWidth = useRef(0);
  const [isDragging, setIsDragging] = useState(false);

  const onMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    isResizing.current = true;
    setIsDragging(true);
    startX.current = e.clientX;
    startWidth.current = meterWidth;
  };

  useEffect(() => {
    const throttledSet = throttle((w: number) => {
      useSettingsStore.setState({ meterWidth: w });
    }, 16);

    const onMouseMove = (e: MouseEvent) => {
      if (!isResizing.current) return;
      const dx = e.clientX - startX.current;
      const newW = Math.max(240, Math.min(800, startWidth.current + dx));
      throttledSet(newW);
    };

    const onMouseUp = () => {
      if (isResizing.current) {
        isResizing.current = false;
        setIsDragging(false);
        const { meterWidth } = useSettingsStore.getState();
        setMeterWidth(meterWidth);
      }
    };

    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };
  }, []);

  return { meterWidth, onMouseDown, isDragging };
};