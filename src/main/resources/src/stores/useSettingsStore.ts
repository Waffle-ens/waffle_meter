import { create } from "zustand";
import type { Hotkey } from "@/types";

export type DisplayMode = "dps_percent" | "amount_dps_percent" | "amount_percent";
export type NameDisplay = "all" | "me_only" | "hidden";

interface SettingsState {
  hotkey: Hotkey;
  displayMode: DisplayMode;
  setDisplayMode: (mode: DisplayMode) => void;
  nameDisplay: NameDisplay;
  setNameDisplay: (mode: NameDisplay) => void;
  meterWidth: number;
  meterHeight: number;
  setHotkey: (h: Hotkey) => void;
  setMeterSize: (w: number, h: number) => void;
}

export const useSettingsStore = create<SettingsState>((set) => ({
  hotkey: { modifiers: 2, vkCode: 0x52 },
  meterWidth: 400,
  meterHeight: 300,
  displayMode: "dps_percent",
  setDisplayMode: (displayMode) => set({ displayMode }),
  nameDisplay: "all",
  setNameDisplay: (nameDisplay) => set({ nameDisplay }),
  setHotkey: (hotkey) => set({ hotkey }),
  setMeterSize: (w, h) => set({ meterWidth: w, meterHeight: h }),
}));
