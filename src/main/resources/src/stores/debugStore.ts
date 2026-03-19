import { create } from "zustand";

interface DebugState {
  logs: string[];
  addLog: (log: any) => void;
  clear: () => void;
}

export const useDebugStore = create<DebugState>((set) => ({
  logs: [],

  addLog: (log) =>
    set((state) => {
      const text =
        typeof log === "string"
          ? log
          : JSON.stringify(log, null, 2);

      return {
        logs: [...state.logs.slice(-200), text], 
      };
    }),

  clear: () => set({ logs: [] }),
}));