import { create } from "zustand";

interface DebugStore {
  logs: string[];
  addLog: (msg: string) => void;
  clear: () => void;
}

export const useDebugStore = create<DebugStore>((set) => ({
  logs: [],
  addLog: (msg: string) => {
    const time = new Date().toLocaleTimeString();
    set((prev) => ({
      logs: [...prev.logs.slice(-30), `${time} ${msg}`],
    }));
  },
  clear: () => set({ logs: [] }),
}));
