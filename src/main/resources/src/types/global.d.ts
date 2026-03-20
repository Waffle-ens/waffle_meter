export {};

declare global {
  interface Window {
    dpsData?: {
      getDpsData?: () => void;
      getBattleDetail?: (id: string) => Promise<any>;
      getVersion?: () => void;
    };

    javaBridge?: {
      resetDps?: () => void;
      moveWindow?: (x: number, y: number) => void;
      startKeyCapture: () => void;
      stopKeyCapture: () => void;
    };
  }
}
