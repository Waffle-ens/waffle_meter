import { create } from "zustand";
import type { Hotkey } from "@/types";
import { parseHotkeyString } from "@/utils/hotKey";

export type DisplayMode = "dps_percent" | "amount_dps_percent" | "amount_percent";
export type NameDisplay = "all" | "me_only" | "hidden";

interface SettingsState {
  hotkey: Hotkey;
  displayMode: DisplayMode;
  setDisplayMode: (mode: DisplayMode) => void;
  nameDisplay: NameDisplay;
  setNameDisplay: (mode: NameDisplay) => void;
  meterWidth: number;
  setMeterWidth: (w: number) => void;
  rowHeight: number;
  setRowHeight: (h: number) => void;
  detailHeight: number;
  setDetailHeight: (h: number) => void;
  setHotkey: (h: Hotkey) => void;
}

const jb = () => (window as any).javaBridge;

const defaultSettings = {
  hotkey: { modifiers: 2, vkCode: 0x52 },
  meterWidth: 400,
  rowHeight: 40,
  detailHeight: 600,

  displayMode: "dps_percent" as DisplayMode,
  nameDisplay: "all" as NameDisplay,
};
export const useSettingsStore = create<SettingsState>((set) => {
  const raw = jb()?.getHotkey?.();
  const parsedHotkey = raw ? parseHotkeyString(raw) : null;
  const interval = setInterval(() => {
    const j = jb();
    if (!j) return;

    const raw = j.getHotkey?.();
    const parsedHotkey = raw ? parseHotkeyString(raw) : null;

    set({
      hotkey: parsedHotkey ?? defaultSettings.hotkey,
      meterWidth: Number(j.loadProps?.("meterWidth")) || defaultSettings.meterWidth,
      rowHeight: Number(j.loadProps?.("rowHeight")) || defaultSettings.rowHeight,
      detailHeight: Number(j.loadProps?.("detailHeight")) || defaultSettings.detailHeight,
      displayMode: j.loadProps?.("displayMode") ?? defaultSettings.displayMode,
      nameDisplay: j.loadProps?.("nameDisplay") ?? defaultSettings.nameDisplay,
    });

    clearInterval(interval);
  }, 200);

  return {
    hotkey: parsedHotkey ?? defaultSettings.hotkey,
    meterWidth: defaultSettings.meterWidth,
    rowHeight: defaultSettings.rowHeight,
    detailHeight: defaultSettings.detailHeight,
    displayMode: defaultSettings.displayMode,
    nameDisplay: defaultSettings.nameDisplay,

    setHotkey: (hotkey) => {
      set({ hotkey });
      jb()?.updateHotkey?.(hotkey.modifiers, hotkey.vkCode);
    },
    setDisplayMode: (displayMode) => {
      set({ displayMode });
      jb()?.saveProps?.("displayMode", displayMode);
    },
    setNameDisplay: (nameDisplay) => {
      set({ nameDisplay });
      jb()?.saveProps?.("nameDisplay", nameDisplay);
    },
    setMeterWidth: (meterWidth) => {
      set({ meterWidth });
      jb()?.saveProps?.("meterWidth", meterWidth);
    },
    setRowHeight: (rowHeight) => {
      set({ rowHeight });
      jb()?.saveProps?.("rowHeight", rowHeight);
    },
    setDetailHeight: (detailHeight) => {
      set({ detailHeight });
      jb()?.saveProps?.("detailHeight", detailHeight);
    },
  };
});
