export {};

declare global {
  interface Window {
    javaBridge?: {
      resetDps?: () => void;
      moveWindow?: (x: number, y: number) => void;

      getDpsData?: () => void;
      getBattleDetail?: (id: number) => Promise<any>;
      getVersion?: () => void;
      getBattleList?: () => void;
      getBattleDetailFromList?: (idx: number, id: number) => Promise<any>;
    };
  }
}
