import { useEffect, useRef } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";

export const useResizable = () => {
  const { meterWidth, setMeterWidth } = useSettingsStore();
  const isResizing = useRef(false);
  const startX = useRef(0);
  const startWidth = useRef(0);

  const onMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    isResizing.current = true;
    startX.current = e.clientX;
    startWidth.current = meterWidth;
  };

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      if (!isResizing.current) return;
      const dx = e.clientX - startX.current;
      const newW = Math.max(300, Math.min(800, startWidth.current + dx));
      useSettingsStore.setState({ meterWidth: newW });
    };

    const onMouseUp = () => {
      if (isResizing.current) {
        isResizing.current = false;
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

  return { meterWidth, onMouseDown };
};