export {};

declare global {
  interface Window {
    javaBridge?: {
      resetDps?: () => void;
      hardResetDps?: () => void;
      startUpdate: (msiUrl: string) => void;
      applyUpdate?: () => void;
      armUpdateOnExit?: () => void;
      getDpsData?: () => void;

      getVersion?: () => string;
      isDevBuild?: () => boolean;
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
      hideToTray?: () => void;
      saveProps?: (key: string, value: string) => void;
      loadProps?: (key: string) => string | null | undefined;
      getHotkey?: () => string;
      updateHotkey?: (modifiers: number, vkCode: number) => void;
      getHideHotkey?: () => string;
      updateHideHotkey?: (modifiers: number, vkCode: number) => void;

      isClickThrough?: () => boolean;
      getClickThroughHotkey?: () => string;
      updateClickThroughHotkey?: (modifiers: number, vkCode: number) => void;
      fitToCurrentMonitor?: () => void;
      onMonitorFit?: (monitorX: number, monitorY: number, width: number, height: number) => void;
      onMeterPositionChanged?: (x: number, y: number) => void;
      moveWindow?: (x: number, y: number) => void;
      syncOverlayBounds?: () => string | undefined;
      // small-window 작은 창 오버레이(Phase 1): Stage 를 콘텐츠 bbox 로 직접 배치/이동.
      setWindowBounds?: (x: number, y: number, w: number, h: number) => void;
      moveWindowTo?: (x: number, y: number) => void;

      isAutoHide?: () => boolean;
      toggleAutoHide?: () => void;
      isPacketLoggingEnabled?: () => boolean;
      setPacketLoggingEnabled?: (enabled: boolean) => void;
      exportPacketLog?: () => string;
      startPacketLogging?: () => string;
      stopPacketLogging?: () => string;
      getPacketLoggingStatus?: () => string;
      openPacketLogFolder?: () => string;
      getStatsConsent?: () => string;
      setStatsConsent?: (
        state: "unknown" | "accepted" | "declined" | "revoked",
        uploadEnabled: boolean,
        publicCharacter: boolean,
      ) => string;
      getStatsOwnCharacter?: () => string;
      getStatsUploadStatus?: () => string;
      openStatsUploadFolder?: () => string;
    };
  }
}
