export {};

declare global {
  interface Window {

    javaBridge?: {
      resetDps?: () => void;
      moveWindow?: (x: number, y: number) => void;
      startKeyCapture: () => void;
      stopKeyCapture: () => void;
      getDpsData?: () => void;
      getBattleDetail?: (id: string) => Promise<any>;
      getVersion?: () => void;
    };
  }
}
