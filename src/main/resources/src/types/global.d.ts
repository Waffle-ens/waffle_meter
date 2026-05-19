export {};

declare global {
  interface Window {
    javaBridge?: {
      // resetDps?: () => void;
      startUpdate: (msiUrl: string) => void;
      getDpsData?: () => void;

      getVersion?: () => string;
      upload?: (idx: number) => Promise<any>;

      getBattleList?: () => void;
      getBattleDetail?: (id: number) => Promise<any>;

      getBattleDetailFromList?: (idx: number, id: number) => Promise<any>;
      getLiveBuffOperatingRate?: (id: number) => Promise<any>;
      getLiveBossBuffOperatingRate?: () => Promise<any>;
      getBuffOperatingRate?: (idx: number, id: number) => Promise<any>;
      getBossBuffOperatingRate?: (idx: number) => Promise<any>;

      openBrowser?: (url: string) => void;
      exitApp?: () => void;

      isClickThrough?: () => boolean;
      getClickThroughHotkey?: () => string;
      updateClickThroughHotkey?: (modifiers: number, vkCode: number) => void;
      fitToCurrentMonitor?: () => void;
      onMonitorFit?: (monitorX: number, monitorY: number, width: number, height: number) => void;
      onMeterPositionChanged?: (x: number, y: number) => void;
      moveWindow?: (x: number, y: number) => void;
      syncOverlayBounds?: () => void;

      isAutoHide?: () => boolean;
      toggleAutoHide?: () => void;
    };
  }
}
