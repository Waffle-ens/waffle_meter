import { create } from "zustand";

export interface Hotkey {
  modifiers: number;
  vkCode: number;
}

interface SettingsState {
  hotkey: Hotkey;

  meterWidth: number;
  meterHeight: number;

  setHotkey: (h: Hotkey) => void;
  setMeterSize: (w: number, h: number) => void;
}

export const useSettingsStore = create<SettingsState>((set) => ({
  hotkey: { modifiers: 2, vkCode: 0x52 }, // ctrl r

  meterWidth: 400,
  meterHeight: 300,

  setHotkey: (hotkey) => set({ hotkey }),
  setMeterSize: (w, h) => set({ meterWidth: w, meterHeight: h }),
}));