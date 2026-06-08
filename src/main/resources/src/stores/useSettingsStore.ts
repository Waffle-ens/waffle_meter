import { create } from "zustand";
import type { Hotkey, ContributionMode } from "@/types";
import { parseHotkeyString } from "@/utils/hotKey";
import { DEFAULT_VISIBLE_SKILL_CODES } from "@/constants/codes";
import { clampMeterRootPosition } from "@/utils/meterBounds";

export type DisplayMode =
  | "dps_percent"
  | "amount_dps_percent"
  | "amount_percent"
  | "amount_full_dps_percent"
  | "amount_full_percent";
export type DamageValueMode = "dps" | "total";
export type TargetInfoDisplayMode =
  | "hp_full_percent"
  | "hp_percent"
  | "remain_full_percent"
  | "remain_percent"
  | "percent";
export type NameDisplay = "all" | "me_only" | "hidden";
export type OverlayTheme = "dark" | "light";
export type OverlayLayout = "standard" | "bottom";
export type StatsConsentState = "unknown" | "accepted" | "declined" | "revoked";
export type CloseAction = "ask" | "tray" | "exit";
export type FontFamily =
  | "Malgun Gothic"
  | "Spoqa Han Sans Neo"
  | "Freesentation"
  | "Tmoney Round Wind"
  | "Pretendard"
  | "NEXON Lv2 Gothic";

export interface ThemeColors {
  userBar: [string, string];
  normalBar: [string, string];
  warningBar: [string, string];
  errorBar: [string, string];
  bossBar: [string, string];
  serverAColor: string;
  serverBColor: string;
  serverDefaultColor: string;
  meterStatAmount: string;
  meterStatDps: string;
  meterStatPercent: string;
  bossRightValue: string;
  combatTimeColor: string;
}

export interface StatsConsentInfo {
  state: StatsConsentState;
  uploadEnabled: boolean;
  publicCharacter: boolean;
  consentVersion: string;
  updatedAt: number;
  identityHash?: string | null;
  remoteExists?: boolean;
  syncStatus?: string;
  syncError?: string | null;
  serverUpdatedAt?: string | null;
  lastSeenAt?: string | null;
}

export const DEFAULT_THEME: ThemeColors = {
  userBar: ["#15c98f", "#0b8f72"],
  normalBar: ["#f6c65b", "#d68a21"],
  warningBar: ["#ff9f45", "#d96d19"],
  errorBar: ["#ef4444", "#991b1b"],
  bossBar: ["#e11d48", "#7f1d1d"],
  serverAColor: "#7dd3fc",
  serverBColor: "#f0abfc",
  serverDefaultColor: "#ffffff",
  meterStatAmount: "#f8d66d",
  meterStatDps: "#f8fafc",
  meterStatPercent: "#8ee6cf",
  bossRightValue: "#fecdd3",
  combatTimeColor: "#cbd5e1",
};

interface SettingsState {
  hotkey: Hotkey;
  displayMode: DisplayMode;
  setDisplayMode: (mode: DisplayMode) => void;
  damageValueMode: DamageValueMode;
  setDamageValueMode: (mode: DamageValueMode) => void;
  targetInfoDisplayMode: TargetInfoDisplayMode;
  setTargetInfoDisplayMode: (mode: TargetInfoDisplayMode) => void;
  nameDisplay: NameDisplay;
  setNameDisplay: (mode: NameDisplay) => void;
  fontFamily: FontFamily;
  setFontFamily: (v: FontFamily) => void;
  meterWidth: number;
  setMeterWidth: (w: number) => void;
  rowHeight: number;
  setRowHeight: (h: number) => void;
  detailWidth: number;
  setDetailWidth: (w: number) => void;
  isLoaded: boolean;
  detailHeight: number;
  setDetailHeight: (h: number) => void;
  setHotkey: (h: Hotkey) => void;
  isMinimal: boolean;
  showCombatTimerInMinimal: boolean;
  setShowCombatTimerInMinimal: (v: boolean) => void;
  showTargetInfoInMinimal: boolean;
  setShowTargetInfoInMinimal: (v: boolean) => void;

  hideHotkey: Hotkey;
  setHideHotkey: (h: Hotkey) => void;
  isDebugMode: boolean;
  overlayTheme: OverlayTheme;
  setOverlayTheme: (v: OverlayTheme) => void;
  toggleOverlayTheme: () => void;
  overlayLayout: OverlayLayout;
  setOverlayLayout: (v: OverlayLayout) => void;
  toggleOverlayLayout: () => void;
  setIsMinimal: (v: boolean) => void;
  toggleMinimal: () => void;
  theme: ThemeColors;
  setTheme: (theme: ThemeColors) => void;
  setThemeColor: <K extends keyof ThemeColors>(key: K, value: ThemeColors[K]) => void;
  resetTheme: () => void;
  windowX: number;
  windowY: number;
  setWindowPosition: (x: number, y: number) => void;
  visibleSkillCodes: number[];
  setVisibleSkillCodes: (codes: number[]) => void;
  // showPower: boolean;
  // setShowPower: (v: boolean) => void;
  meterOpacity: number;
  setMeterOpacity: (v: number) => void;
  contributionMode: ContributionMode;
  setContributionMode: (v: ContributionMode) => void;
  clickThroughHotkey: Hotkey;
  setClickThroughHotkey: (h: Hotkey) => void;
  isClickThrough: boolean;
  isAutoHide: boolean;
  toggleAutoHide: () => void;
  multiMonitorMode: boolean;
  setMultiMonitorMode: (v: boolean) => void;
  closeAction: CloseAction;
  setCloseAction: (v: CloseAction) => void;
  gpuAcceleration: boolean;
  setGpuAcceleration: (v: boolean) => void;
  meterFrameRate: number;
  setMeterFrameRate: (v: number) => void;
  statsConsent: StatsConsentInfo;
  setStatsConsent: (v: StatsConsentInfo) => void;
  refreshStatsConsent: () => void;
  joinPanelWidth: number;
  setJoinPanelWidth: (w: number) => void;
  joinPanelHeight: number;
  setJoinPanelHeight: (h: number) => void;
  joinPanelX: number;
  joinPanelY: number;
  joinPanelPositioned: boolean;
  setJoinPanelPosition: (x: number, y: number) => void;
  resetJoinPanelPosition: () => void;
  sidePanelX: number;
  sidePanelY: number;
  sidePanelPositioned: boolean;
  setSidePanelPosition: (x: number, y: number) => void;
  resetSidePanelPosition: () => void;
  settingsPanelWidth: number;
  settingsPanelHeight: number;
  setSettingsPanelWidth: (w: number) => void;
  setSettingsPanelHeight: (h: number) => void;
  historyPanelWidth: number;
  historyPanelHeight: number;
  setHistoryPanelWidth: (w: number) => void;
  setHistoryPanelHeight: (h: number) => void;
  updatePanelWidth: number;
  updatePanelHeight: number;
  setUpdatePanelWidth: (w: number) => void;
  setUpdatePanelHeight: (h: number) => void;
  uiX: number;
  uiY: number;
  resetMeterPosition: () => void;
  setUiPosition: (x: number, y: number) => void;
}

const jb = () => (window as any).javaBridge;
const MAX_INIT_ATTEMPTS = 200;
const readSavedNumber = (
  value: string | null | undefined,
  fallback: number,
): number => {
  if (value == null || value === "") return fallback;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

export const DEFAULT_STATS_CONSENT: StatsConsentInfo = {
  state: "unknown",
  uploadEnabled: false,
  publicCharacter: true,
  consentVersion: "2026-06-04",
  updatedAt: 0,
};

const parseStatsConsent = (raw?: string | null): StatsConsentInfo => {
  if (!raw) return DEFAULT_STATS_CONSENT;
  try {
    const parsed = JSON.parse(raw) as Partial<StatsConsentInfo>;
    const state =
      parsed.state === "accepted" ||
      parsed.state === "declined" ||
      parsed.state === "revoked"
        ? parsed.state
        : "unknown";
    return {
      state,
      uploadEnabled: parsed.uploadEnabled === true,
      publicCharacter: parsed.publicCharacter !== false,
      consentVersion:
        typeof parsed.consentVersion === "string"
          ? parsed.consentVersion
          : DEFAULT_STATS_CONSENT.consentVersion,
      updatedAt: Number.isFinite(Number(parsed.updatedAt)) ? Number(parsed.updatedAt) : 0,
      identityHash: typeof parsed.identityHash === "string" ? parsed.identityHash : null,
      remoteExists: parsed.remoteExists === true,
      syncStatus: typeof parsed.syncStatus === "string" ? parsed.syncStatus : undefined,
      syncError: typeof parsed.syncError === "string" ? parsed.syncError : null,
      serverUpdatedAt:
        typeof parsed.serverUpdatedAt === "string" ? parsed.serverUpdatedAt : null,
      lastSeenAt: typeof parsed.lastSeenAt === "string" ? parsed.lastSeenAt : null,
    };
  } catch {
    return DEFAULT_STATS_CONSENT;
  }
};

interface OverlayBoundsSync {
  offsetX?: number;
  offsetY?: number;
  width?: number;
  height?: number;
}

const readOverlayBoundsSync = (): OverlayBoundsSync => {
  const raw = jb()?.syncOverlayBounds?.();
  if (!raw || typeof raw !== "string") return {};
  try {
    const parsed = JSON.parse(raw) as OverlayBoundsSync;
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
};

const finiteOr = (value: unknown, fallback: number) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const clampPanelPosition = (
  x: number,
  y: number,
  width: number,
  height: number,
  viewportWidth: number,
  viewportHeight: number,
) => ({
  x: Math.min(Math.max(0, x), Math.max(0, viewportWidth - width)),
  y: Math.min(Math.max(0, y), Math.max(0, viewportHeight - height)),
});

const defaultSettings = {
  hotkey: { modifiers: 2, vkCode: 0x52 },
  hideHotkey: { modifiers: 2, vkCode: 0x48 },
  meterWidth: 400,
  rowHeight: 36,
  isDebugMode: false,
  detailHeight: 600,
  detailWidth: 800,
  windowX: 0,
  windowY: 0,
  isLoaded: false,
  displayMode: "dps_percent" as DisplayMode,
  damageValueMode: "dps" as DamageValueMode,
  targetInfoDisplayMode: "hp_full_percent" as TargetInfoDisplayMode,
  nameDisplay: "all" as NameDisplay,
  fontFamily: "NEXON Lv2 Gothic" as FontFamily,
  overlayTheme: "dark" as OverlayTheme,
  overlayLayout: "standard" as OverlayLayout,
  isMinimal: false,
  showCombatTimerInMinimal: true,
  showTargetInfoInMinimal: true,
  theme: DEFAULT_THEME,
  visibleSkillCodes: DEFAULT_VISIBLE_SKILL_CODES,
  // showPower: true,
  meterOpacity: 0.4,
  contributionMode: "contribution" as ContributionMode,
  clickThroughHotkey: { modifiers: 2, vkCode: 0x54 },
  isClickThrough: false,
  isAutoHide: true,
  multiMonitorMode: false,
  closeAction: "ask" as CloseAction,
  gpuAcceleration: true,
  meterFrameRate: 40,
  statsConsent: DEFAULT_STATS_CONSENT,
  joinPanelWidth: 400,
  joinPanelHeight: 330,
  joinPanelX: 0,
  joinPanelY: 0,
  joinPanelPositioned: false,
  sidePanelX: 0,
  sidePanelY: 0,
  sidePanelPositioned: false,
  settingsPanelWidth: 380,
  settingsPanelHeight: 640,
  historyPanelWidth: 380,
  historyPanelHeight: 520,
  updatePanelWidth: 300,
  updatePanelHeight: 160,
  uiX: 0,
  uiY: 0,
};

export const useSettingsStore = create<SettingsState>((set) => {
  let initAttempts = 0;
  const interval = setInterval(() => {
    const j = jb();
    if (!j || typeof j.loadProps !== "function") {
      initAttempts += 1;
      if (initAttempts >= MAX_INIT_ATTEMPTS) {
        set({ isLoaded: true });
        clearInterval(interval);
      }
      return;
    }

    const raw = j.getHotkey?.();
    const rawHide = j.getHideHotkey?.();
    const rawClickThrough = j.getClickThroughHotkey?.();
    const parsedHotkey = raw ? parseHotkeyString(raw) : null;
    const parsedHideHotkey = rawHide ? parseHotkeyString(rawHide) : null;
    const parsedClickThroughHotkey = rawClickThrough ? parseHotkeyString(rawClickThrough) : null;
    const savedIsMinimal = j.loadProps("isMinimal") === "true";

    const savedThemeRaw = j.loadProps?.("theme");
    let savedTheme: ThemeColors = DEFAULT_THEME;

    const savedSkillCodesRaw = j.loadProps?.("visibleSkillCodes");
    let savedSkillCodes = DEFAULT_VISIBLE_SKILL_CODES;
    try {
      if (savedSkillCodesRaw) {
        const parsedSkillCodes = JSON.parse(savedSkillCodesRaw);
        if (Array.isArray(parsedSkillCodes)) {
          savedSkillCodes = parsedSkillCodes
            .map((code) => Number(code))
            .filter((code) => Number.isFinite(code));
        }
      }
    } catch {}
    if (DEFAULT_VISIBLE_SKILL_CODES.length > 100 && savedSkillCodes.length < 40) {
      savedSkillCodes = DEFAULT_VISIBLE_SKILL_CODES;
    }

    try {
      if (savedThemeRaw) savedTheme = { ...DEFAULT_THEME, ...JSON.parse(savedThemeRaw) };
    } catch {}

    const savedSidePanelXRaw = j.loadProps?.("sidePanelX");
    const savedSidePanelYRaw = j.loadProps?.("sidePanelY");
    const hasSavedSidePanelX = savedSidePanelXRaw != null && savedSidePanelXRaw !== "";
    const hasSavedSidePanelY = savedSidePanelYRaw != null && savedSidePanelYRaw !== "";
    const sidePanelPositioned = hasSavedSidePanelX || hasSavedSidePanelY;
    const savedJoinPanelXRaw = j.loadProps?.("joinPanelX");
    const savedJoinPanelYRaw = j.loadProps?.("joinPanelY");
    const hasSavedJoinPanelX = savedJoinPanelXRaw != null && savedJoinPanelXRaw !== "";
    const hasSavedJoinPanelY = savedJoinPanelYRaw != null && savedJoinPanelYRaw !== "";
    const joinPanelPositioned = hasSavedJoinPanelX || hasSavedJoinPanelY;
    const savedMeterOpacityRaw = j.loadProps?.("meterOpacity");
    const savedOverlayTheme = j.loadProps?.("overlayTheme");
    const savedOverlayLayout = j.loadProps?.("overlayLayout");
    const savedMultiMonitorMode = j.loadProps?.("multiMonitorMode") === "true";
    const savedCloseActionRaw = j.loadProps?.("closeAction");
    const savedCloseAction: CloseAction =
      savedCloseActionRaw === "tray" || savedCloseActionRaw === "exit"
        ? savedCloseActionRaw
        : defaultSettings.closeAction;
    const savedGpuAcceleration = j.loadProps?.("gpuAcceleration") !== "false";
    const savedMeterFrameRate = Math.round(
      Math.min(
        60,
        Math.max(30, readSavedNumber(j.loadProps?.("meterFrameRate"), defaultSettings.meterFrameRate)),
      ),
    );
    const savedStatsConsent = parseStatsConsent(
      j.getStatsConsent?.() ?? j.loadProps?.("statsConsent"),
    );
    const savedWindowXRaw = j.loadProps?.("windowX");
    const savedWindowYRaw = j.loadProps?.("windowY");
    const savedUiXRaw = j.loadProps?.("uiX");
    const savedUiYRaw = j.loadProps?.("uiY");
    const hasSavedWindowX = savedWindowXRaw != null && savedWindowXRaw !== "";
    const hasSavedWindowY = savedWindowYRaw != null && savedWindowYRaw !== "";
    const hasSavedUiX = savedUiXRaw != null && savedUiXRaw !== "";
    const hasSavedUiY = savedUiYRaw != null && savedUiYRaw !== "";
    const initialUiX = hasSavedUiX
      ? readSavedNumber(savedUiXRaw, defaultSettings.uiX)
      : hasSavedWindowX
        ? readSavedNumber(savedWindowXRaw, defaultSettings.uiX)
        : defaultSettings.uiX;
    const initialUiY = hasSavedUiY
      ? readSavedNumber(savedUiYRaw, defaultSettings.uiY)
      : hasSavedWindowY
        ? readSavedNumber(savedWindowYRaw, defaultSettings.uiY)
        : defaultSettings.uiY;

    set({
      hotkey: parsedHotkey ?? defaultSettings.hotkey,
      hideHotkey: parsedHideHotkey ?? defaultSettings.hideHotkey,
      meterWidth: Number(j.loadProps?.("meterWidth")) || defaultSettings.meterWidth,
      rowHeight: Number(j.loadProps?.("rowHeight")) || defaultSettings.rowHeight,
      detailHeight: Number(j.loadProps?.("detailHeight")) || defaultSettings.detailHeight,
      detailWidth: Number(j.loadProps?.("detailWidth")) || defaultSettings.detailWidth,
      displayMode: j.loadProps?.("displayMode") ?? defaultSettings.displayMode,
      damageValueMode:
        j.loadProps?.("damageValueMode") === "total"
          ? "total"
          : defaultSettings.damageValueMode,
      targetInfoDisplayMode:
        j.loadProps?.("targetInfoDisplayMode") ?? defaultSettings.targetInfoDisplayMode,
      isDebugMode: j.isDebuggingMode?.() ?? false,
      nameDisplay: j.loadProps?.("nameDisplay") ?? defaultSettings.nameDisplay,
      fontFamily: (j.loadProps?.("fontFamily") as FontFamily) ?? defaultSettings.fontFamily,
      isMinimal: savedIsMinimal,
      showCombatTimerInMinimal: j.loadProps?.("showCombatTimerInMinimal") === "true",
      showTargetInfoInMinimal: j.loadProps?.("showTargetInfoInMinimal") === "true",
      overlayTheme: savedOverlayTheme === "light" ? "light" : defaultSettings.overlayTheme,
      overlayLayout: savedOverlayLayout === "bottom" ? "bottom" : defaultSettings.overlayLayout,
      theme: savedTheme,
      visibleSkillCodes: savedSkillCodes,
      windowX: defaultSettings.windowX,
      windowY: defaultSettings.windowY,
      // showPower: j.loadProps?.("showPower") === "false" ? false : true,
      meterOpacity:
        savedMeterOpacityRaw != null && savedMeterOpacityRaw !== ""
          ? Number(savedMeterOpacityRaw)
          : defaultSettings.meterOpacity,
      contributionMode:
        (j.loadProps?.("contributionMode") as ContributionMode) ?? defaultSettings.contributionMode,
      clickThroughHotkey: parsedClickThroughHotkey ?? defaultSettings.clickThroughHotkey,
      isClickThrough: j.isClickThrough?.() ?? false,
      isAutoHide: j.isAutoHide?.() ?? false,
      multiMonitorMode: savedMultiMonitorMode,
      closeAction: savedCloseAction,
      gpuAcceleration: savedGpuAcceleration,
      meterFrameRate: savedMeterFrameRate,
      statsConsent: savedStatsConsent,
      joinPanelWidth: Number(j.loadProps?.("joinPanelWidth")) || defaultSettings.joinPanelWidth,
      joinPanelHeight: Number(j.loadProps?.("joinPanelHeight")) || defaultSettings.joinPanelHeight,
      joinPanelX: hasSavedJoinPanelX ? Number(savedJoinPanelXRaw) : defaultSettings.joinPanelX,
      joinPanelY: hasSavedJoinPanelY ? Number(savedJoinPanelYRaw) : defaultSettings.joinPanelY,
      joinPanelPositioned,
      sidePanelX: hasSavedSidePanelX ? Number(savedSidePanelXRaw) : defaultSettings.sidePanelX,
      sidePanelY: hasSavedSidePanelY ? Number(savedSidePanelYRaw) : defaultSettings.sidePanelY,
      sidePanelPositioned,
      settingsPanelWidth:
        Number(j.loadProps?.("settingsPanelWidth")) || defaultSettings.settingsPanelWidth,
      settingsPanelHeight:
        Number(j.loadProps?.("settingsPanelHeight")) || defaultSettings.settingsPanelHeight,
      historyPanelWidth:
        Number(j.loadProps?.("historyPanelWidth")) || defaultSettings.historyPanelWidth,
      historyPanelHeight:
        Number(j.loadProps?.("historyPanelHeight")) || defaultSettings.historyPanelHeight,
      updatePanelWidth:
        Number(j.loadProps?.("updatePanelWidth")) || defaultSettings.updatePanelWidth,
      updatePanelHeight:
        Number(j.loadProps?.("updatePanelHeight")) || defaultSettings.updatePanelHeight,
      uiX: initialUiX,
      uiY: initialUiY,

      isLoaded: true,
    });
    clearInterval(interval);
  }, 100);

  return {
    hotkey: defaultSettings.hotkey,
    hideHotkey: defaultSettings.hideHotkey,
    isMinimal: defaultSettings.isMinimal,
    showCombatTimerInMinimal: defaultSettings.showCombatTimerInMinimal,
    showTargetInfoInMinimal: defaultSettings.showTargetInfoInMinimal,

    meterWidth: defaultSettings.meterWidth,
    rowHeight: defaultSettings.rowHeight,
    detailHeight: defaultSettings.detailHeight,
    detailWidth: defaultSettings.detailWidth,
    visibleSkillCodes: defaultSettings.visibleSkillCodes,
    displayMode: defaultSettings.displayMode,
    damageValueMode: defaultSettings.damageValueMode,
    targetInfoDisplayMode: defaultSettings.targetInfoDisplayMode,
    nameDisplay: defaultSettings.nameDisplay,
    fontFamily: defaultSettings.fontFamily,
    isDebugMode: defaultSettings.isDebugMode,
    overlayTheme: defaultSettings.overlayTheme,
    overlayLayout: defaultSettings.overlayLayout,
    theme: defaultSettings.theme,
    windowX: defaultSettings.windowX,
    windowY: defaultSettings.windowY,
    // showPower: defaultSettings.showPower,
    meterOpacity: defaultSettings.meterOpacity,
    contributionMode: defaultSettings.contributionMode,
    clickThroughHotkey: defaultSettings.clickThroughHotkey,
    isClickThrough: defaultSettings.isClickThrough,
    isAutoHide: defaultSettings.isAutoHide,
    multiMonitorMode: defaultSettings.multiMonitorMode,
    closeAction: defaultSettings.closeAction,
    gpuAcceleration: defaultSettings.gpuAcceleration,
    meterFrameRate: defaultSettings.meterFrameRate,
    statsConsent: defaultSettings.statsConsent,
    isLoaded: defaultSettings.isLoaded,

    joinPanelWidth: defaultSettings.joinPanelWidth,
    joinPanelHeight: defaultSettings.joinPanelHeight,
    joinPanelX: defaultSettings.joinPanelX,
    joinPanelY: defaultSettings.joinPanelY,
    joinPanelPositioned: defaultSettings.joinPanelPositioned,
    sidePanelX: defaultSettings.sidePanelX,
    sidePanelY: defaultSettings.sidePanelY,
    sidePanelPositioned: defaultSettings.sidePanelPositioned,
    settingsPanelWidth: defaultSettings.settingsPanelWidth,
    settingsPanelHeight: defaultSettings.settingsPanelHeight,
    historyPanelWidth: defaultSettings.historyPanelWidth,
    historyPanelHeight: defaultSettings.historyPanelHeight,
    updatePanelWidth: defaultSettings.updatePanelWidth,
    updatePanelHeight: defaultSettings.updatePanelHeight,
    uiX: defaultSettings.uiX,
    uiY: defaultSettings.uiY,

    setHotkey: (hotkey) => {
      set({ hotkey });
      jb()?.updateHotkey?.(hotkey.modifiers, hotkey.vkCode);
    },
    setHideHotkey: (hideHotkey) => {
      set({ hideHotkey });
      jb()?.updateHideHotkey?.(hideHotkey.modifiers, hideHotkey.vkCode);
    },
    setIsMinimal: (isMinimal) => {
      set({ isMinimal });
      jb()?.saveProps?.("isMinimal", String(isMinimal));
    },
    setShowCombatTimerInMinimal: (v) => {
      set({ showCombatTimerInMinimal: v });
      jb()?.saveProps?.("showCombatTimerInMinimal", String(v));
    },
    setShowTargetInfoInMinimal: (v) => {
      set({ showTargetInfoInMinimal: v });
      jb()?.saveProps?.("showTargetInfoInMinimal", String(v));
    },
    toggleMinimal: () =>
      set((s) => {
        const next = !s.isMinimal;
        jb()?.saveProps?.("isMinimal", String(next));
        return { isMinimal: next };
      }),
    setDisplayMode: (displayMode) => {
      set({ displayMode });
      jb()?.saveProps?.("displayMode", displayMode);
    },
    setDamageValueMode: (damageValueMode) => {
      set({ damageValueMode });
      jb()?.saveProps?.("damageValueMode", damageValueMode);
    },
    setTargetInfoDisplayMode: (targetInfoDisplayMode) => {
      set({ targetInfoDisplayMode });
      jb()?.saveProps?.("targetInfoDisplayMode", targetInfoDisplayMode);
    },
    setNameDisplay: (nameDisplay) => {
      set({ nameDisplay });
      jb()?.saveProps?.("nameDisplay", nameDisplay);
    },
    setFontFamily: (fontFamily) => {
      set({ fontFamily });
      jb()?.saveProps?.("fontFamily", fontFamily);
    },
    setMeterWidth: (meterWidth) => {
      set({ meterWidth });
      jb()?.saveProps?.("meterWidth", meterWidth);
    },
    setRowHeight: (rowHeight) => {
      set({ rowHeight });
      jb()?.saveProps?.("rowHeight", rowHeight);
    },
    setDetailHeight: (detailHeight) => {
      set({ detailHeight });
      jb()?.saveProps?.("detailHeight", detailHeight);
    },
    setDetailWidth: (detailWidth) => {
      set({ detailWidth });
      jb()?.saveProps?.("detailWidth", detailWidth);
    },
    setOverlayTheme: (overlayTheme) => {
      set({ overlayTheme });
      jb()?.saveProps?.("overlayTheme", overlayTheme);
    },
    toggleOverlayTheme: () =>
      set((s) => {
        const overlayTheme = s.overlayTheme === "dark" ? "light" : "dark";
        jb()?.saveProps?.("overlayTheme", overlayTheme);
        return { overlayTheme };
      }),
    setOverlayLayout: (overlayLayout) => {
      set({ overlayLayout });
      jb()?.saveProps?.("overlayLayout", overlayLayout);
    },
    toggleOverlayLayout: () =>
      set((s) => {
        const overlayLayout = s.overlayLayout === "standard" ? "bottom" : "standard";
        jb()?.saveProps?.("overlayLayout", overlayLayout);
        return { overlayLayout };
      }),
    setTheme: (theme) => {
      set({ theme });
      jb()?.saveProps?.("theme", JSON.stringify(theme));
    },
    setThemeColor: (key, value) =>
      set((s) => {
        const next = { ...s.theme, [key]: value };
        jb()?.saveProps?.("theme", JSON.stringify(next));
        return { theme: next };
      }),
    resetTheme: () => {
      set({ theme: DEFAULT_THEME });
      jb()?.saveProps?.("theme", JSON.stringify(DEFAULT_THEME));
    },
    setWindowPosition: (windowX, windowY) => {
      set({ windowX, windowY });
      jb()?.saveProps?.("windowX", String(windowX));
      jb()?.saveProps?.("windowY", String(windowY));
    },
    setVisibleSkillCodes: (visibleSkillCodes) => {
      set({ visibleSkillCodes });
      jb()?.saveProps?.("visibleSkillCodes", JSON.stringify(visibleSkillCodes));
    },
    setMeterOpacity: (meterOpacity) => {
      set({ meterOpacity });
      jb()?.saveProps?.("meterOpacity", String(meterOpacity));
    },
    setContributionMode: (contributionMode) => {
      set({ contributionMode });
      jb()?.saveProps?.("contributionMode", contributionMode);
    },
    setClickThroughHotkey: (clickThroughHotkey) => {
      set({ clickThroughHotkey });
      jb()?.updateClickThroughHotkey?.(clickThroughHotkey.modifiers, clickThroughHotkey.vkCode);
    },
    toggleAutoHide: () =>
      set((s) => {
        jb()?.toggleAutoHide?.();
        return { isAutoHide: !s.isAutoHide };
      }),
    setMultiMonitorMode: (multiMonitorMode) => {
      jb()?.saveProps?.("multiMonitorMode", String(multiMonitorMode));
      const sync = readOverlayBoundsSync();
      const offsetX = finiteOr(sync.offsetX, 0);
      const offsetY = finiteOr(sync.offsetY, 0);
      const viewportWidth = finiteOr(sync.width, window.innerWidth);
      const viewportHeight = finiteOr(sync.height, window.innerHeight);

      set((s) => {
        const meterAnchor = document.querySelector<HTMLElement>("[data-meter-root-anchor]");
        const meterRoot = meterAnchor?.closest<HTMLElement>(".drag-area");
        const meter = clampMeterRootPosition(s.uiX + offsetX, s.uiY + offsetY, {
          rootEl: meterRoot,
          anchorEl: meterAnchor,
          fallbackWidth: s.meterWidth,
          fallbackHeight: s.rowHeight,
          viewportWidth,
          viewportHeight,
        });

        jb()?.saveProps?.("uiX", String(meter.x));
        jb()?.saveProps?.("uiY", String(meter.y));

        const next: Partial<SettingsState> = {
          multiMonitorMode,
          uiX: meter.x,
          uiY: meter.y,
        };

        if (s.joinPanelPositioned) {
          const joinPanel = clampPanelPosition(
            s.joinPanelX + offsetX,
            s.joinPanelY + offsetY,
            s.joinPanelWidth,
            s.joinPanelHeight,
            viewportWidth,
            viewportHeight,
          );
          jb()?.saveProps?.("joinPanelX", String(joinPanel.x));
          jb()?.saveProps?.("joinPanelY", String(joinPanel.y));
          next.joinPanelX = joinPanel.x;
          next.joinPanelY = joinPanel.y;
        }

        if (s.sidePanelPositioned) {
          const sidePanel = clampPanelPosition(
            s.sidePanelX + offsetX,
            s.sidePanelY + offsetY,
            Math.max(s.settingsPanelWidth, s.historyPanelWidth, s.updatePanelWidth, s.detailWidth),
            Math.max(
              s.settingsPanelHeight,
              s.historyPanelHeight,
              s.updatePanelHeight,
              s.detailHeight,
            ) + 44,
            viewportWidth,
            viewportHeight,
          );
          jb()?.saveProps?.("sidePanelX", String(sidePanel.x));
          jb()?.saveProps?.("sidePanelY", String(sidePanel.y));
          next.sidePanelX = sidePanel.x;
          next.sidePanelY = sidePanel.y;
        }

        return next;
      });
    },
    setCloseAction: (closeAction) => {
      set({ closeAction });
      jb()?.saveProps?.("closeAction", closeAction);
    },
    setGpuAcceleration: (gpuAcceleration) => {
      set({ gpuAcceleration });
      jb()?.saveProps?.("gpuAcceleration", String(gpuAcceleration));
    },
    setMeterFrameRate: (meterFrameRate) => {
      const clamped = Math.round(Math.min(60, Math.max(30, meterFrameRate)));
      set({ meterFrameRate: clamped });
      jb()?.saveProps?.("meterFrameRate", String(clamped));
    },
    setStatsConsent: (statsConsent) => {
      const raw = jb()?.setStatsConsent?.(
        statsConsent.state,
        statsConsent.uploadEnabled,
        statsConsent.publicCharacter,
      );
      const next = raw ? parseStatsConsent(raw) : statsConsent;
      set({ statsConsent: next });
      jb()?.saveProps?.("statsConsent", JSON.stringify(next));
    },
    refreshStatsConsent: () => {
      const next = parseStatsConsent(jb()?.getStatsConsent?.());
      set({ statsConsent: next });
      jb()?.saveProps?.("statsConsent", JSON.stringify(next));
    },
    // setShowPower: (showPower) => {
    //   set({ showPower });
    //   jb()?.saveProps?.("showPower", String(showPower));
    // },
    setJoinPanelWidth: (joinPanelWidth) => {
      set({ joinPanelWidth });
      jb()?.saveProps?.("joinPanelWidth", String(joinPanelWidth));
    },
    setJoinPanelHeight: (joinPanelHeight) => {
      set({ joinPanelHeight });
      jb()?.saveProps?.("joinPanelHeight", String(joinPanelHeight));
    },
    setJoinPanelPosition: (joinPanelX, joinPanelY) => {
      set({ joinPanelX, joinPanelY, joinPanelPositioned: true });
      jb()?.saveProps?.("joinPanelX", String(joinPanelX));
      jb()?.saveProps?.("joinPanelY", String(joinPanelY));
    },
    resetJoinPanelPosition: () => {
      set((s) => {
        const meterRoot = document.querySelector("[data-meter-root-anchor]");
        const rect = meterRoot?.getBoundingClientRect();
        const x = rect ? rect.left : 0;
        const y = rect
          ? s.overlayLayout === "bottom"
            ? Math.max(8, rect.top - s.joinPanelHeight - 8)
            : rect.bottom + 8
          : 8;

        jb()?.saveProps?.("joinPanelX", String(x));
        jb()?.saveProps?.("joinPanelY", String(y));
        return {
          joinPanelX: x,
          joinPanelY: y,
          joinPanelPositioned: true,
        };
      });
    },
    setSidePanelPosition: (sidePanelX, sidePanelY) => {
      set({ sidePanelX, sidePanelY, sidePanelPositioned: true });
      jb()?.saveProps?.("sidePanelX", String(sidePanelX));
      jb()?.saveProps?.("sidePanelY", String(sidePanelY));
    },
    resetSidePanelPosition: () => {
      set((s) => {
        const meterRoot = document.querySelector("[data-meter-root-anchor]");
        const rect = meterRoot?.getBoundingClientRect();
        const x = rect ? rect.right + 8 : 408;
        const sideHeight = s.settingsPanelHeight + 44;
        const y = rect
          ? s.overlayLayout === "bottom"
            ? Math.max(8, rect.top - sideHeight - 8)
            : rect.top
          : 8;

        jb()?.saveProps?.("sidePanelX", String(x));
        jb()?.saveProps?.("sidePanelY", String(y));
        return {
          sidePanelX: x,
          sidePanelY: y,
          sidePanelPositioned: true,
        };
      });
    },
    resetMeterPosition: () => {
      set({
        windowX: defaultSettings.windowX,
        windowY: defaultSettings.windowY,
        uiX: defaultSettings.uiX,
        uiY: defaultSettings.uiY,
      });
      jb()?.saveProps?.("windowX", "0");
      jb()?.saveProps?.("windowY", "0");
      jb()?.saveProps?.("uiX", "0");
      jb()?.saveProps?.("uiY", "0");
      jb()?.syncOverlayBounds?.();
    },
    setSettingsPanelWidth: (settingsPanelWidth) => {
      set({ settingsPanelWidth });
      jb()?.saveProps?.("settingsPanelWidth", String(settingsPanelWidth));
    },
    setSettingsPanelHeight: (settingsPanelHeight) => {
      set({ settingsPanelHeight });
      jb()?.saveProps?.("settingsPanelHeight", String(settingsPanelHeight));
    },
    setHistoryPanelWidth: (historyPanelWidth) => {
      set({ historyPanelWidth });
      jb()?.saveProps?.("historyPanelWidth", String(historyPanelWidth));
    },
    setHistoryPanelHeight: (historyPanelHeight) => {
      set({ historyPanelHeight });
      jb()?.saveProps?.("historyPanelHeight", String(historyPanelHeight));
    },
    setUpdatePanelWidth: (updatePanelWidth) => {
      set({ updatePanelWidth });
      jb()?.saveProps?.("updatePanelWidth", String(updatePanelWidth));
    },
    setUpdatePanelHeight: (updatePanelHeight) => {
      set({ updatePanelHeight });
      jb()?.saveProps?.("updatePanelHeight", String(updatePanelHeight));
    },
    setUiPosition: (uiX, uiY) => {
      set({ uiX, uiY });
      jb()?.saveProps?.("uiX", String(uiX));
      jb()?.saveProps?.("uiY", String(uiY));
    },
  };
});
