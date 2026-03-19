import { useEffect, useState } from "react";

const MOD_CTRL = 0x0002;
const MOD_ALT = 0x0001;
const MODIFIER_VKS = new Set([16, 17, 18, 91, 92]);

export const useHotkeyCapture = (initialHotkey: any) => {
  const [pending, setPending] = useState(initialHotkey);
  const [isCapturing, setIsCapturing] = useState(false);

  useEffect(() => {
    if (!isCapturing) return;

    const handleNative = (event: any) => {
      const d = event?.detail || {};

      if (d.shift || d.meta) return;

      const mods = (d.ctrl ? MOD_CTRL : 0) | (d.alt ? MOD_ALT : 0);
      if (!mods) return;

      const vkCode = Number(d.keyCode);
      if (!Number.isFinite(vkCode)) return;
      if (MODIFIER_VKS.has(vkCode)) return;

      setPending({ modifiers: mods, vkCode });
    };

    const handleKeydown = (e: KeyboardEvent) => {
      if (e.shiftKey || e.metaKey) return;
      if (!(e.ctrlKey || e.altKey)) return;
      if (MODIFIER_VKS.has(e.keyCode)) return; 

      e.preventDefault();
      const mods = (e.ctrlKey ? MOD_CTRL : 0) | (e.altKey ? MOD_ALT : 0);
      setPending({ modifiers: mods, vkCode: e.keyCode });
    };

    window.addEventListener("settings:captureKey", handleNative);

    if (window.javaBridge?.startKeyCapture) {
      window.javaBridge.startKeyCapture();
    } else {
      window.addEventListener("keydown", handleKeydown);
    }

    return () => {
      window.removeEventListener("settings:captureKey", handleNative);
      window.removeEventListener("keydown", handleKeydown);
      if (window.javaBridge?.stopKeyCapture) {
        window.javaBridge.stopKeyCapture();
      }
    };
  }, [isCapturing]);

  return {
    pending,
    isCapturing,
    start: () => setIsCapturing(true),
    stop: () => setIsCapturing(false),
    reset: (hotkey: any) => setPending(hotkey),
  };
};
