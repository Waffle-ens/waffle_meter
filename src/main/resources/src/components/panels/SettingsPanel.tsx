import { useCallback, useEffect, useRef, useState } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
import { useHotkeyCapture } from "@/hooks/useHotkeyCapture";
import { formatHotkey } from "@/utils/hotKey";
import { Button } from "@/components/ui/button";
import { FolderOpen, RotateCcw } from "lucide-react";
import type {
  DisplayMode,
  DamageValueMode,
  CloseAction,
  FontFamily,
  NameDisplay,
  StatsConsentInfo,
  TargetInfoDisplayMode,
  ThemeColors,
} from "@/stores/useSettingsStore";
import type { ContributionMode } from "@/types";
import { Slider } from "@/components/ui/slider";
import { Switch } from "@/components/ui/switch";
import { SettingsItem } from "./SettingsItem";
import { SettingsRow } from "./SettingsRow";
import { SettingsControlInput } from "./SettingsControlInput";
import { ColorSwatch, GradientRow } from "@/components/colorpicker";
import type { UpdateInfo } from "@/types";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface Props {
  onClose: () => void;
  onReady?: () => void;
  currentVersion?: string;
  updateInfo?: UpdateInfo | null;
  onCheckUpdate?: () => void;
  registerHeaderClose?: (handler: (() => void) | null) => void;
}

const DISPLAY_MODES: { value: DisplayMode; label: string; description: string }[] = [
  { value: "dps_percent", label: "딜량 / 기여도", description: "45,000/s 또는 1.20M (35.5%)" },
  {
    value: "amount_dps_percent",
    label: "딜량 / 기여도",
    description: "전투력은 닉네임 옆 배지로 표시",
  },
  { value: "amount_percent", label: "기여도", description: "전투력은 닉네임 옆 배지로 표시" },
  {
    value: "amount_full_dps_percent",
    label: "딜량 / 기여도",
    description: "전투력은 닉네임 옆 배지로 표시",
  },
  { value: "amount_full_percent", label: "기여도", description: "전투력은 닉네임 옆 배지로 표시" },
];

const DAMAGE_VALUE_MODES: { value: DamageValueMode; label: string }[] = [
  { value: "dps", label: "DPS" },
  { value: "total", label: "총딜량" },
];

const TARGET_INFO_DISPLAY_MODES: {
  value: TargetInfoDisplayMode;
  label: string;
  description: string;
}[] = [
  {
    value: "hp_full_percent",
    label: "남은/최대(전체) / 퍼센트",
    description: "1,234,567 / 9,876,543 12.5%",
  },
  {
    value: "hp_percent",
    label: "남은/최대(축약) / 퍼센트",
    description: "1.2M / 9.9M 12.5%",
  },
  {
    value: "remain_full_percent",
    label: "남은 체력(전체) / 퍼센트",
    description: "1,234,567 12.5%",
  },
  {
    value: "remain_percent",
    label: "남은 체력(축약) / 퍼센트",
    description: "1.2M 12.5%",
  },
  { value: "percent", label: "퍼센트만", description: "12.5%" },
];

const NAME_DISPLAY_MODES: { value: NameDisplay; label: string }[] = [
  { value: "all", label: "모두 표기" },
  { value: "me_only", label: "나만 표기" },
  { value: "hidden", label: "모두 숨김" },
];

const CLOSE_ACTION_MODES: { value: CloseAction; label: string }[] = [
  { value: "ask", label: "처음 한 번 묻기" },
  { value: "tray", label: "트레이로 보내기" },
  { value: "exit", label: "완전히 종료" },
];

const FONT_FAMILIES: { value: FontFamily; label: string }[] = [
  { value: "Malgun Gothic", label: "맑은 고딕 (윈도우 기본 폰트)" },
  { value: "NEXON Lv2 Gothic", label: "NEXON Lv2 Gothic" },
  { value: "Spoqa Han Sans Neo", label: "Spoqa Han Sans Neo" },
  { value: "Freesentation", label: "Freesentation" },
  { value: "Tmoney Round Wind", label: "Tmoney Round Wind" },
  { value: "Pretendard", label: "Pretendard" },
];

interface StatsUploadStatus {
  enabled?: boolean;
  pending?: number;
  uploaded?: number;
  skipped?: number;
  failed?: number;
  lastPath?: string;
  lastReason?: string;
  lastUpdatedAt?: number;
}

const parseStatsUploadStatus = (raw?: string): StatsUploadStatus => {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as StatsUploadStatus;
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return { lastReason: raw };
  }
};

const parseStatsCharacterDetected = (raw?: string): boolean => {
  if (!raw) return false;
  try {
    return Boolean((JSON.parse(raw) as { detected?: boolean }).detected);
  } catch {
    return false;
  }
};

const STATS_CONSENT_LABEL: Record<StatsConsentInfo["state"], string> = {
  unknown: "미선택",
  accepted: "동의됨",
  declined: "거절됨",
  revoked: "철회됨",
};

type SettingsTab = "display" | "overlay" | "theme" | "stats" | "reset";

const SETTINGS_TABS: { value: SettingsTab; label: string }[] = [
  { value: "display", label: "표시" },
  { value: "overlay", label: "오버레이" },
  { value: "theme", label: "테마" },
  { value: "stats", label: "통계" },
  { value: "reset", label: "초기화" },
];

export const SettingsPanel = ({
  onClose,
  onReady,
  currentVersion,
  updateInfo,
  onCheckUpdate,
  registerHeaderClose,
}: Props) => {
  const {
    hotkey,
    hideHotkey,
    displayMode,
    damageValueMode,
    targetInfoDisplayMode,
    nameDisplay,
    fontFamily,
    rowHeight,
    isMinimal,
    theme,
    showCombatTimerInMinimal,
    showTargetInfoInMinimal,
    meterOpacity,
    contributionMode,
    clickThroughHotkey,
    isClickThrough,
    isAutoHide,
    multiMonitorMode,
    closeAction,
    gpuAcceleration,
    meterFrameRate,
    statsConsent,
  } = useSettingsStore(
    useShallow((s) => ({
      hotkey: s.hotkey,
      hideHotkey: s.hideHotkey,
      displayMode: s.displayMode,
      damageValueMode: s.damageValueMode,
      targetInfoDisplayMode: s.targetInfoDisplayMode,
      nameDisplay: s.nameDisplay,
      fontFamily: s.fontFamily,
      rowHeight: s.rowHeight,
      isMinimal: s.isMinimal,
      theme: s.theme,
      showCombatTimerInMinimal: s.showCombatTimerInMinimal,
      showTargetInfoInMinimal: s.showTargetInfoInMinimal,
      meterOpacity: s.meterOpacity,
      contributionMode: s.contributionMode,
      clickThroughHotkey: s.clickThroughHotkey,
      isClickThrough: s.isClickThrough,
      isAutoHide: s.isAutoHide,
      multiMonitorMode: s.multiMonitorMode,
      closeAction: s.closeAction,
      gpuAcceleration: s.gpuAcceleration,
      meterFrameRate: s.meterFrameRate,
      statsConsent: s.statsConsent,
    })),
  );

  const {
    setHotkey,
    setHideHotkey,
    setDisplayMode,
    setDamageValueMode,
    setTargetInfoDisplayMode,
    setNameDisplay,
    setFontFamily,
    setRowHeight,
    setIsMinimal,
    setThemeColor,
    setTheme,
    resetTheme,
    setShowCombatTimerInMinimal,
    setShowTargetInfoInMinimal,
    setMeterOpacity,
    setContributionMode,
    setClickThroughHotkey,
    toggleAutoHide,
    setMultiMonitorMode,
    setCloseAction,
    setGpuAcceleration,
    setMeterFrameRate,
    setStatsConsent,
    refreshStatsConsent,
    resetJoinPanelPosition,
    resetSidePanelPosition,
    resetMeterPosition,
  } = useSettingsStore.getState();
  const {
    pending: pendingReset,
    start: startReset,
    stop: stopReset,
    reset: resetReset,
  } = useHotkeyCapture(hotkey);
  const {
    pending: pendingHide,
    start: startHide,
    stop: stopHide,
    reset: resetHide,
  } = useHotkeyCapture(hideHotkey);
  const {
    pending: pendingClickThrough,
    start: startClickThrough,
    stop: stopClickThrough,
    reset: resetClickThrough,
  } = useHotkeyCapture(clickThroughHotkey);
  const [snapshot] = useState(() => ({
    hotkey,
    hideHotkey,
    displayMode,
    damageValueMode,
    targetInfoDisplayMode,
    nameDisplay,
    fontFamily,
    rowHeight,
    isMinimal,
    showCombatTimerInMinimal,
    showTargetInfoInMinimal,
    meterOpacity,
    contributionMode,
    clickThroughHotkey,
    multiMonitorMode,
    closeAction,
    gpuAcceleration,
    meterFrameRate,
    statsConsent,
    theme: structuredClone(theme),
  }));
  const [statsUploadStatus, setStatsUploadStatus] = useState<StatsUploadStatus>(() =>
    parseStatsUploadStatus(window.javaBridge?.getStatsUploadStatus?.()),
  );
  const [statsCharacterDetected, setStatsCharacterDetected] = useState<boolean>(() =>
    parseStatsCharacterDetected(window.javaBridge?.getStatsOwnCharacter?.()),
  );

  useEffect(() => {
    onReady?.();
    refreshStatsConsent();
    setStatsUploadStatus(parseStatsUploadStatus(window.javaBridge?.getStatsUploadStatus?.()));
    setStatsCharacterDetected(parseStatsCharacterDetected(window.javaBridge?.getStatsOwnCharacter?.()));
  }, []);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setStatsUploadStatus(parseStatsUploadStatus(window.javaBridge?.getStatsUploadStatus?.()));
      setStatsCharacterDetected(parseStatsCharacterDetected(window.javaBridge?.getStatsOwnCharacter?.()));
    }, 2500);
    return () => window.clearInterval(timer);
  }, []);

  const handleSave = useCallback(() => {
    setHotkey(pendingReset);
    setHideHotkey(pendingHide);
    setClickThroughHotkey(pendingClickThrough);
    onClose();
  }, [
    setHotkey,
    pendingReset,
    setHideHotkey,
    pendingHide,
    setClickThroughHotkey,
    pendingClickThrough,
    onClose,
  ]);

  const handleCancel = useCallback(() => {
    setDisplayMode(snapshot.displayMode);
    setDamageValueMode(snapshot.damageValueMode);
    setTargetInfoDisplayMode(snapshot.targetInfoDisplayMode);
    setNameDisplay(snapshot.nameDisplay);
    setFontFamily(snapshot.fontFamily);
    setRowHeight(snapshot.rowHeight);
    setIsMinimal(snapshot.isMinimal);
    setShowCombatTimerInMinimal(snapshot.showCombatTimerInMinimal);
    setShowTargetInfoInMinimal(snapshot.showTargetInfoInMinimal);
    resetHide(snapshot.hideHotkey);
    setTheme(snapshot.theme as ThemeColors);
    setMeterOpacity(snapshot.meterOpacity);
    setContributionMode(snapshot.contributionMode);
    resetReset(snapshot.hotkey);
    resetClickThrough(snapshot.clickThroughHotkey);
    setMultiMonitorMode(snapshot.multiMonitorMode);
    setCloseAction(snapshot.closeAction);
    setGpuAcceleration(snapshot.gpuAcceleration);
    setMeterFrameRate(snapshot.meterFrameRate);
    setStatsConsent(snapshot.statsConsent);
    onClose();
  }, [
    onClose,
    resetClickThrough,
    resetReset,
    resetHide,
    setContributionMode,
    setDisplayMode,
    setDamageValueMode,
    setFontFamily,
    setIsMinimal,
    setMeterOpacity,
    setMultiMonitorMode,
    setCloseAction,
    setGpuAcceleration,
    setMeterFrameRate,
    setNameDisplay,
    setRowHeight,
    setShowCombatTimerInMinimal,
    setShowTargetInfoInMinimal,
    setStatsConsent,
    setTheme,
    setTargetInfoDisplayMode,
    snapshot,
  ]);

  const handleAcceptStats = useCallback(() => {
    setStatsConsent({
      ...statsConsent,
      state: "accepted",
      uploadEnabled: true,
      publicCharacter: statsConsent.publicCharacter,
      updatedAt: Date.now(),
    });
  }, [setStatsConsent, statsConsent]);

  const handleRevokeStats = useCallback(() => {
    setStatsConsent({
      ...statsConsent,
      state: "revoked",
      uploadEnabled: false,
      updatedAt: Date.now(),
    });
  }, [setStatsConsent, statsConsent]);

  const handleDeclineStats = useCallback(() => {
    setStatsConsent({
      ...statsConsent,
      state: "declined",
      uploadEnabled: false,
      updatedAt: Date.now(),
    });
  }, [setStatsConsent, statsConsent]);

  const handleToggleStatsUpload = useCallback(
    (uploadEnabled: boolean) => {
      setStatsConsent({
        ...statsConsent,
        state: uploadEnabled ? "accepted" : statsConsent.state,
        uploadEnabled,
        updatedAt: Date.now(),
      });
    },
    [setStatsConsent, statsConsent],
  );

  const handleToggleStatsPublic = useCallback(
    (publicCharacter: boolean) => {
      setStatsConsent({
        ...statsConsent,
        publicCharacter,
        updatedAt: Date.now(),
      });
    },
    [setStatsConsent, statsConsent],
  );

  const handleOpenStatsUploadFolder = useCallback(() => {
    window.javaBridge?.openStatsUploadFolder?.();
    setStatsUploadStatus(parseStatsUploadStatus(window.javaBridge?.getStatsUploadStatus?.()));
  }, []);

  const handleCancelRef = useRef(handleCancel);
  useEffect(() => {
    handleCancelRef.current = handleCancel;
  }, [handleCancel]);

  const stableHandleCancel = useCallback(() => {
    handleCancelRef.current();
  }, []);

  useEffect(() => {
    registerHeaderClose?.(stableHandleCancel);
    return () => registerHeaderClose?.(null);
  }, [registerHeaderClose, stableHandleCancel]);

  const statsAccepted = statsConsent.state === "accepted";
  const statsConsentDescription = `상태: ${STATS_CONSENT_LABEL[statsConsent.state]}${
    statsConsent.syncStatus === "synced"
      ? " · 서버 동기화됨"
      : statsConsent.syncStatus === "sync_failed"
        ? " · 서버 동기화 실패"
        : ""
  }`;
  const statsUploadDescription =
    statsUploadStatus.lastReason || statsUploadStatus.lastPath
      ? statsUploadStatus.lastReason ?? statsUploadStatus.lastPath
      : `완료 ${statsUploadStatus.uploaded ?? 0} · 제외 ${statsUploadStatus.skipped ?? 0} · 실패 ${
          statsUploadStatus.failed ?? 0
        }`;
  const [activeTab, setActiveTab] = useState<SettingsTab>("display");
  const sectionClass = (tab: SettingsTab) => (activeTab === tab ? "contents" : "hidden");

  return (
    <div
      className="flex pr-3 min-h-0 flex-1 flex-col overflow-y-auto overflow-x-hidden py-2"
      style={{
        contain: "layout style paint",
      }}
      >
      <div className="flex pr-3 min-h-0 flex-1 flex-col overflow-y-auto overflow-x-hidden scrollbar-gutter:stable py-2">
        <div className="sticky top-0 z-10 mb-3 grid grid-cols-5 gap-1 rounded-md border border-white/10 bg-[#08111f]/95 p-1 backdrop-blur-md">
          {SETTINGS_TABS.map((tab) => (
            <button
              key={tab.value}
              type="button"
              onClick={() => setActiveTab(tab.value)}
              className={
                activeTab === tab.value
                  ? "rounded px-2 py-2 text-xs font-bold text-cyan-100 bg-cyan-400/15 ring-1 ring-cyan-300/20"
                  : "rounded px-2 py-2 text-xs text-slate-400 transition-colors hover:bg-white/8 hover:text-slate-100"
              }>
              {tab.label}
            </button>
          ))}
        </div>

        <div className={sectionClass("display")}>
        <SettingsItem>
          <SettingsRow
            title="버전 정보"
            description={currentVersion ? `v${currentVersion}` : "-"}
            rightClassName="flex items-center">
            <Button
              onClick={onCheckUpdate}
              variant="ghost"
              size="lg"
              className={
                updateInfo
                  ? " py-3 transition-all text-green-400 border border-green-400/30 hover:bg-green-400/10"
                  : " py-3 transition-all opacity-60 hover:opacity-100"
              }>
              {updateInfo ? `v${updateInfo.latestVersion} 업데이트` : "업데이트 확인"}
            </Button>
          </SettingsRow>
        </SettingsItem>

        <SettingsItem>
          <SettingsRow
            title="폰트"
            description="표시 글꼴을 선택합니다"
            align="center"
            rightClassName="w-44">
            <Select
              value={fontFamily}
              onValueChange={(v) => setFontFamily(v as FontFamily)}>
              <SelectTrigger className="w-44 bg-white/5 border-white/10 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {FONT_FAMILIES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>
        </SettingsItem>
        </div>

        <div className={sectionClass("overlay")}>
        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">오버레이</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem>
          <SettingsRow
            title="자동 숨김"
            description="아이온2가 포커싱 상태가 아닐 경우 자동으로 숨깁니다.">
            <Switch
              checked={isAutoHide}
              onCheckedChange={toggleAutoHide}
              className="data-[state=checked]:bg-emerald-500"
            />
          </SettingsRow>
          <SettingsRow
            title="다중 모니터 이동"
            description="미터기를 게임 화면 밖의 다른 모니터로 이동할 수 있게 합니다.">
            <Switch
              checked={multiMonitorMode}
              onCheckedChange={setMultiMonitorMode}
              className="data-[state=checked]:bg-emerald-500"
            />
          </SettingsRow>
          <SettingsRow
            title="종료 버튼 동작"
            description="메인 전원 버튼을 눌렀을 때의 동작입니다."
            align="center"
            rightClassName="w-44">
            <Select
              value={closeAction}
              onValueChange={(v) => setCloseAction(v as CloseAction)}>
              <SelectTrigger className="w-44 bg-white/5 border-white/10 text-sm">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CLOSE_ACTION_MODES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>
          <SettingsRow
            title="GPU 가속"
            description="끄면 다음 실행부터 소프트웨어 렌더링으로 동작합니다.">
            <Switch
              checked={gpuAcceleration}
              onCheckedChange={setGpuAcceleration}
              className="data-[state=checked]:bg-emerald-500"
            />
          </SettingsRow>
          <SettingsRow
            title="렌더링 프레임 제한"
            description="낮출수록 GPU 부하가 줄어듭니다. 다음 실행부터 적용됩니다."
            align="center"
            rightClassName="w-44">
            <div className="flex h-8 items-center gap-3">
              <Slider
                min={30}
                max={60}
                step={5}
                className="cursor-pointer"
                value={[meterFrameRate]}
                onValueChange={(value) => setMeterFrameRate(value[0])}
              />
              <span className="text-xs opacity-60 w-12 text-right tabular-nums">
                {meterFrameRate}fps
              </span>
            </div>
          </SettingsRow>
          <SettingsRow
            title="전투 초기화 단축키 설정"
            align="center"
            rightClassName="w-44">
            <SettingsControlInput
              readOnly
              onFocus={startReset}
              onBlur={stopReset}
              value={formatHotkey(pendingReset.modifiers, pendingReset.vkCode)}
              className="cursor-pointer"
            />
          </SettingsRow>
          <SettingsRow
            title="최소화 단축키 설정"
            align="center"
            rightClassName="w-44">
            <SettingsControlInput
              readOnly
              onFocus={startHide}
              onBlur={stopHide}
              value={formatHotkey(pendingHide.modifiers, pendingHide.vkCode)}
              className="cursor-pointer"
            />
          </SettingsRow>
        </SettingsItem>

        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">패스스루</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem>
          <SettingsRow
            title="패스스루"
            description="클릭이 미터기를 통과해 게임으로 전달됩니다.">
            <Switch
              checked={isClickThrough}
              disabled
              className="data-[state=checked]:bg-emerald-500"
            />
          </SettingsRow>

          <SettingsRow
            title="패스스루 단축키 설정"
            align="center"
            rightClassName="w-44">
            <SettingsControlInput
              readOnly
              onFocus={startClickThrough}
              onBlur={stopClickThrough}
              value={formatHotkey(pendingClickThrough.modifiers, pendingClickThrough.vkCode)}
              className="cursor-pointer"
            />
          </SettingsRow>
        </SettingsItem>
        </div>

        <div className={sectionClass("display")}>
        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">컴팩트 모드</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem>
          <SettingsRow title="컴팩트 모드">
            <Switch
              checked={isMinimal}
              onCheckedChange={(v) => setIsMinimal(v)}
              className="data-[state=checked]:bg-emerald-500"
            />
          </SettingsRow>

          <SettingsRow title="컴팩트 모드 중 전투 시간 표시">
            <Switch
              checked={showCombatTimerInMinimal}
              onCheckedChange={(v) => setShowCombatTimerInMinimal(v)}
              className="data-[state=checked]:bg-emerald-500 disabled:opacity-30"
            />
          </SettingsRow>

          <SettingsRow title="컴팩트 모드 중 보스 표시">
            <Switch
              checked={showTargetInfoInMinimal}
              onCheckedChange={(v) => setShowTargetInfoInMinimal(v)}
              className="data-[state=checked]:bg-emerald-500 disabled:opacity-30"
            />
          </SettingsRow>
        </SettingsItem>

        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">미터기 설정</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem>
          <SettingsRow
            title="기여도 표시 방식"
            align="center"
            rightClassName="w-44">
            <Select
              value={contributionMode}
              onValueChange={(v) => setContributionMode(v as ContributionMode)}>
              <SelectTrigger className="text-xs w-44 bg-white/5 border-white/10">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  value="contribution"
                  className="px-4 py-2">
                  파티 기여도 (상대)
                </SelectItem>
                <SelectItem
                  value="entireContribution"
                  className="px-4 py-2">
                  보스 체력 기여도 (절대)
                </SelectItem>
              </SelectContent>
            </Select>
          </SettingsRow>

          <SettingsRow
            title="표시 형식"
            align="center"
            rightClassName="w-44">
            <Select
              value={displayMode}
              onValueChange={(v) => setDisplayMode(v as DisplayMode)}>
              <SelectTrigger className="text-xs w-44 bg-white/5 border-white/10 ">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DISPLAY_MODES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>

          <SettingsRow
            title="딜량 기준"
            align="center"
            rightClassName="w-44">
            <Select
              value={damageValueMode}
              onValueChange={(v) => setDamageValueMode(v as DamageValueMode)}>
              <SelectTrigger className="text-xs w-44 bg-white/5 border-white/10 ">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DAMAGE_VALUE_MODES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>

          <SettingsRow
            title="보스 표시 형식"
            align="center"
            rightClassName="w-44">
            <Select
              value={targetInfoDisplayMode}
              onValueChange={(v) => setTargetInfoDisplayMode(v as TargetInfoDisplayMode)}>
              <SelectTrigger className="text-xs w-44 bg-white/5 border-white/10 ">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TARGET_INFO_DISPLAY_MODES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>

          <SettingsRow
            title="아이디 표기"
            align="center"
            rightClassName="w-44">
            <Select
              value={nameDisplay}
              onValueChange={(v) => setNameDisplay(v as NameDisplay)}>
              <SelectTrigger className="text-xs w-44 bg-white/5 border-white/10 ">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {NAME_DISPLAY_MODES.map(({ value, label }) => (
                  <SelectItem
                    key={value}
                    value={value}
                    className="px-4 py-2">
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </SettingsRow>

          <SettingsRow
            title="행 높이"
            align="center"
            rightClassName="w-44">
            <div className="flex h-8 items-center gap-3 ">
              <Slider
                min={24}
                max={80}
                step={1}
                className="cursor-pointer"
                value={[rowHeight]}
                onValueChange={(value) => setRowHeight(value[0])}
              />
              <span className="text-xs opacity-60 w-12 text-right tabular-nums">{rowHeight}px</span>
            </div>
          </SettingsRow>
        </SettingsItem>
        </div>

        <div className={sectionClass("theme")}>
        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">테마 설정</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem title="투명도 조정">
          <SettingsRow
            title="미터 투명도"
            align="center"
            rightClassName="w-44">
            <div className="flex h-8 items-center gap-3">
              <Slider
                min={0}
                max={1}
                step={0.05}
                className="cursor-pointer"
                value={[meterOpacity]}
                onValueChange={(value) => setMeterOpacity(value[0])}
              />
              <span className="text-xs opacity-60 w-12 text-right tabular-nums">
                {Math.round(meterOpacity * 100)}%
              </span>
            </div>
          </SettingsRow>
        </SettingsItem>

        <SettingsItem title="유저 이름 색상">
          <div className="flex flex-col gap-2.5">
            <ColorSwatch
              label="천족"
              value={theme.serverAColor}
              onChange={(v) => setThemeColor("serverAColor", v)}
            />
            <ColorSwatch
              label="마족"
              value={theme.serverBColor}
              onChange={(v) => setThemeColor("serverBColor", v)}
            />
          </div>
        </SettingsItem>
        <SettingsItem title="미터 바 색상">
          <div className="flex flex-col gap-2.5">
            <GradientRow
              label="내 캐릭터"
              value={theme.userBar}
              onChange={(v) => setThemeColor("userBar", v)}
            />
            <GradientRow
              label="일반"
              value={theme.normalBar}
              onChange={(v) => setThemeColor("normalBar", v)}
            />
            <GradientRow
              label="경고 (기여도 5% 미만)"
              value={theme.warningBar}
              onChange={(v) => setThemeColor("warningBar", v)}
            />
            <GradientRow
              label="에러 (기여도 3% 미만)"
              value={theme.errorBar}
              onChange={(v) => setThemeColor("errorBar", v)}
            />
          </div>
        </SettingsItem>

        <SettingsItem title="미터 텍스트 색상">
          <div className="flex flex-col gap-2.5">
            <ColorSwatch
              label="누적"
              value={theme.meterStatAmount}
              onChange={(v) => setThemeColor("meterStatAmount", v)}
            />
            <ColorSwatch
              label="DPS"
              value={theme.meterStatDps}
              onChange={(v) => setThemeColor("meterStatDps", v)}
            />
            <ColorSwatch
              label="퍼센트"
              value={theme.meterStatPercent}
              onChange={(v) => setThemeColor("meterStatPercent", v)}
            />
            <ColorSwatch
              label="전투 시간"
              value={theme.combatTimeColor}
              onChange={(v) => setThemeColor("combatTimeColor", v)}
            />
          </div>
        </SettingsItem>

        <SettingsItem title="보스 / 전투 기록">
          <div className="flex flex-col gap-2.5">
            <GradientRow
              label="타겟 / 전투 기록"
              value={theme.bossBar}
              onChange={(v) => setThemeColor("bossBar", v)}
            />
          </div>
          <div className="flex flex-col gap-2.5">
            <ColorSwatch
              label="남은 체력 / 경과 시간"
              value={theme.bossRightValue}
              onChange={(v) => setThemeColor("bossRightValue", v)}
            />
          </div>
        </SettingsItem>
        </div>

        <div className={sectionClass("stats")}>
        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">웹 통계</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem>
          <SettingsRow
            title="통계 수집 동의"
            description={statsConsentDescription}
            rightClassName="w-44">
            {statsAccepted ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={handleRevokeStats}
                className="w-full text-xs text-rose-200 hover:bg-rose-500/10 hover:text-rose-100">
                동의 철회
              </Button>
            ) : (
              <div className="flex w-full gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleAcceptStats}
                  disabled={!statsCharacterDetected}
                  title={!statsCharacterDetected ? "캐릭터 접속 후 동의할 수 있어요" : undefined}
                  className="flex-1 text-xs text-emerald-200 hover:bg-emerald-500/10 hover:text-emerald-100 disabled:opacity-40">
                  동의
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleDeclineStats}
                  className="flex-1 text-xs opacity-70 hover:opacity-100">
                  거절
                </Button>
              </div>
            )}
          </SettingsRow>
          {!statsCharacterDetected && (
            <p className="px-1 pb-1 text-[11px] leading-4 text-amber-300/80">
              캐릭터가 감지되면 설정할 수 있어요. 게임에 접속해 캐릭터를 불러온 뒤 다시 시도하세요.
            </p>
          )}
          <SettingsRow
            title="자동 업로드"
            description="보스를 처치해 종료된 내 전투 요약만 전송합니다.">
            <Switch
              checked={statsAccepted && statsConsent.uploadEnabled}
              disabled={!statsAccepted || !statsCharacterDetected}
              onCheckedChange={handleToggleStatsUpload}
              className="data-[state=checked]:bg-emerald-500 disabled:opacity-30"
            />
          </SettingsRow>
          <SettingsRow
            title="내 캐릭터 공개"
            description="끄면 비공개 지표로만 집계됩니다.">
            <Switch
              checked={statsConsent.publicCharacter}
              disabled={!statsAccepted || !statsCharacterDetected}
              onCheckedChange={handleToggleStatsPublic}
              className="data-[state=checked]:bg-emerald-500 disabled:opacity-30"
            />
          </SettingsRow>
          <SettingsRow
            title="업로드 상태"
            description={
              <span
                className="block truncate"
                title={statsUploadDescription}>
                {statsUploadDescription}
              </span>
            }
            rightClassName="w-44">
            <Button
              variant="ghost"
              size="sm"
              onClick={handleOpenStatsUploadFolder}
              className="w-full flex items-center gap-2 text-xs">
              <FolderOpen className="w-3 h-3" />
              기록 폴더
            </Button>
          </SettingsRow>
        </SettingsItem>
        </div>

        <div className={sectionClass("reset")}>
        <div className="my-3 flex items-center gap-2">
          <div className="flex-1 h-px bg-white/10" />
          <span className="text-xs opacity-40 px-2 shrink-0">설정 초기화</span>
          <div className="flex-1 h-px bg-white/10" />
        </div>
        <SettingsItem className="pb-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={resetTheme}
            className="w-full opacity-50 hover:opacity-100 hover:bg-transition transition-opacity flex items-center gap-2 text-xs">
            <RotateCcw className="w-3 h-3" />
            테마 초기화
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={resetMeterPosition}
            className="w-full opacity-50 hover:opacity-100 hover:bg-transition transition-opacity flex items-center gap-2 text-xs">
            <RotateCcw className="w-3 h-3" />
            미터기 위치 초기화
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={resetJoinPanelPosition}
            className="w-full opacity-50 hover:opacity-100 hover:bg-transition transition-opacity flex items-center gap-2 text-xs">
            <RotateCcw className="w-3 h-3" />
            파티 신청 위치 초기화
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={resetSidePanelPosition}
            className="w-full opacity-50 hover:opacity-100 hover:bg-transition transition-opacity flex items-center gap-2 text-xs">
            <RotateCcw className="w-3 h-3" />
            사이드 패널 위치 초기화
          </Button>
        </SettingsItem>
        </div>
      </div>
      <div className="flex w-full min-w-0 shrink-0 justify-end gap-2 border-t border-white/10 pt-4">
        <Button
          onClick={handleCancel}
          size="lg"
          className="p-4 w-20 opacity-60 hover:opacity-100 transition-opacity">
          취소
        </Button>
        <Button
          onClick={handleSave}
          className="bg-cyan-600 p-4 w-20 transition-colors hover:bg-cyan-500">
          저장
        </Button>
      </div>
    </div>
  );
};
