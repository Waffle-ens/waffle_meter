import { create } from "zustand";

interface DebugStore {
  logs: string[];
  addLog: (msg: string) => void;
  clear: () => void;
}
const isDebugMode = () => (window as any).javaBridge?.isDebuggingMode?.() === true;

export const useDebugStore = create<DebugStore>((set) => ({
  logs: [],
  addLog: (msg: string) => {
    if (!isDebugMode()) return;

    const time = new Date().toLocaleTimeString();
    set((prev) => ({
      logs: [...prev.logs.slice(-30), `${time} ${msg}`],
    }));
  },
  clear: () => set({ logs: [] }),
}));
