import { useCallback, useEffect, useRef, useState } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
import { useHotkeyCapture } from "@/hooks/useHotkeyCapture";
import { formatHotkey } from "@/utils/hotKey";
import { Button } from "@/components/ui/button";
import { FolderOpen, Play, RotateCcw, Square } from "lucide-react";
import type {
  DisplayMode,
  FontFamily,
  NameDisplay,
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
  { value: "dps_percent", label: "DPS / 기여도", description: "45,000/s (35.5%)" },
  {
    value: "amount_dps_percent",
    label: "전투력 / DPS / 기여도",
    description: "143.0k 45,000/s (35.5%)",
  },
  { value: "amount_percent", label: "전투력 / 기여도", description: "143.0k (35.5%)" },
  {
    value: "amount_full_dps_percent",
    label: "전투력 / DPS / 기여도",
    description: "143.0k 45,000/s (35.5%)",
  },
  { value: "amount_full_percent", label: "전투력 / 기여도", description: "143.0k (35.5%)" },
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

const FONT_FAMILIES: { value: FontFamily; label: string }[] = [
  { value: "Malgun Gothic", label: "맑은 고딕 (윈도우 기본 폰트)" },
  { value: "NEXON Lv2 Gothic", label: "NEXON Lv2 Gothic" },
  { value: "Spoqa Han Sans Neo", label: "Spoqa Han Sans Neo" },
  { value: "Freesentation", label: "Freesentation" },
  { value: "Tmoney Round Wind", label: "Tmoney Round Wind" },
  { value: "Pretendard", label: "Pretendard" },
];

interface PacketLogStatus {
  running: boolean;
  path: string;
  captureCount: number;
  captureBytes: number;
  assembledCount: number;
  dispatchCount: number;
  parsedDamageCount: number;
  parsedBattleCount: number;
  parsedMetaCount: number;
  unknownOpcodeCount: number;
  errorCount: number;
}

const emptyPacketLogStatus: PacketLogStatus = {
  running: false,
  path: "",
  captureCount: 0,
  captureBytes: 0,
  assembledCount: 0,
  dispatchCount: 0,
  parsedDamageCount: 0,
  parsedBattleCount: 0,
  parsedMetaCount: 0,
  unknownOpcodeCount: 0,
  errorCount: 0,
};

const parsePacketLogStatus = (raw: string | undefined): PacketLogStatus => {
  if (!raw) return emptyPacketLogStatus;
  try {
    return { ...emptyPacketLogStatus, ...JSON.parse(raw) };
  } catch {
    return emptyPacketLogStatus;
  }
};

const formatBytes = (bytes: number) => {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
};

export const SettingsPanel = ({
  onClose,
  onReady,
  currentVersion,
  updateInfo,
  onCheckUpdate,
  registerHeaderClose,
}: Props) => {
  const {
    hideHotkey,
    displayMode,
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
  } = useSettingsStore(
    useShallow((s) => ({
      hideHotkey: s.hideHotkey,
      displayMode: s.displayMode,
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
    })),
  );

  const {
    setHideHotkey,
    setDisplayMode,
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
    resetJoinPanelPosition,
    resetSidePanelPosition,
    resetMeterPosition,
  } = useSettingsStore.getState();
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
  const [packetLogStatus, setPacketLogStatus] =
    useState<PacketLogStatus>(emptyPacketLogStatus);

  const refreshPacketLogStatus = useCallback(() => {
    setPacketLogStatus(parsePacketLogStatus(window.javaBridge?.getPacketLoggingStatus?.()));
  }, []);

  const handleStartPacketLogging = useCallback(() => {
    setPacketLogStatus(parsePacketLogStatus(window.javaBridge?.startPacketLogging?.()));
  }, []);

  const handleStopPacketLogging = useCallback(() => {
    setPacketLogStatus(parsePacketLogStatus(window.javaBridge?.stopPacketLogging?.()));
  }, []);

  const handleOpenPacketLogFolder = useCallback(() => {
    window.javaBridge?.openPacketLogFolder?.();
  }, []);

  const [snapshot] = useState(() => ({
    hideHotkey,
    displayMode,
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
    theme: structuredClone(theme),
  }));

  useEffect(() => {
    onReady?.();
  }, []);

  useEffect(() => {
    refreshPacketLogStatus();
    const id = window.setInterval(refreshPacketLogStatus, 1000);
    return () => window.clearInterval(id);
  }, [refreshPacketLogStatus]);

  const handleSave = useCallback(() => {
    setHideHotkey(pendingHide);
    setClickThroughHotkey(pendingClickThrough);
    onClose();
  }, [setHideHotkey, pendingHide, setClickThroughHotkey, pendingClickThrough, onClose]);

  const handleCancel = useCallback(() => {
    setDisplayMode(snapshot.displayMode);
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
    resetClickThrough(snapshot.clickThroughHotkey);
    setMultiMonitorMode(snapshot.multiMonitorMode);
    onClose();
  }, [
    onClose,
    resetClickThrough,
    resetHide,
    setContributionMode,
    setDisplayMode,
    setFontFamily,
    setIsMinimal,
    setMeterOpacity,
    setMultiMonitorMode,
    setNameDisplay,
    setRowHeight,
    setShowCombatTimerInMinimal,
    setShowTargetInfoInMinimal,
    setTheme,
    setTargetInfoDisplayMode,
    snapshot,
  ]);

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

  return (
    <div
      className="flex pr-3 min-h-0 flex-1 flex-col overflow-y-auto overflow-x-hidden py-2"
      style={{
        contain: "layout style paint",
      }}
      >
      <div className="flex pr-3 min-h-0 flex-1 flex-col overflow-y-auto overflow-x-hidden scrollbar-gutter:stable py-2">
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
            title="패킷 진단 로그"
            description={
              packetLogStatus.running
                ? `${packetLogStatus.captureCount.toLocaleString()}개 / ${formatBytes(packetLogStatus.captureBytes)}`
                : packetLogStatus.path
                  ? "로깅 종료됨"
                  : "문제 재현 구간만 짧게 기록합니다"
            }
            align="center"
            rightClassName="w-44">
            <div className="flex items-center justify-end gap-2">
              <Button
                onClick={
                  packetLogStatus.running ? handleStopPacketLogging : handleStartPacketLogging
                }
                variant="ghost"
                size="icon"
                className={
                  packetLogStatus.running
                    ? "h-8 w-8 text-red-300 border border-red-300/30 hover:bg-red-400/10"
                    : "h-8 w-8 text-emerald-300 border border-emerald-300/30 hover:bg-emerald-400/10"
                }
                title={packetLogStatus.running ? "로깅 종료" : "로깅 시작"}>
                {packetLogStatus.running ? (
                  <Square className="h-4 w-4" />
                ) : (
                  <Play className="h-4 w-4" />
                )}
              </Button>
              <Button
                onClick={handleOpenPacketLogFolder}
                variant="ghost"
                size="icon"
                className="h-8 w-8 opacity-70 hover:opacity-100"
                title="로그 폴더 열기">
                <FolderOpen className="h-4 w-4" />
              </Button>
            </div>
          </SettingsRow>
          {(packetLogStatus.running || packetLogStatus.path) && (
            <div className="px-3 pb-2 text-[11px] leading-5 text-white/45">
              <div className="truncate" title={packetLogStatus.path}>
                {packetLogStatus.path || "로그 파일 준비 중"}
              </div>
              <div className="tabular-nums">
                조립 {packetLogStatus.assembledCount.toLocaleString()} · 파서{" "}
                {packetLogStatus.dispatchCount.toLocaleString()} · 데미지{" "}
                {packetLogStatus.parsedDamageCount.toLocaleString()} · 전투{" "}
                {packetLogStatus.parsedBattleCount.toLocaleString()} · 미확인 opcode{" "}
                {packetLogStatus.unknownOpcodeCount.toLocaleString()} · 오류{" "}
                {packetLogStatus.errorCount.toLocaleString()}
              </div>
            </div>
          )}
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
