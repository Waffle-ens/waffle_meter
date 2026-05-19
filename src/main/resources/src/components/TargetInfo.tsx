import { memo } from "react";
import bossIcon from "@/assets/bossIcon.png";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { formatAmount } from "@/utils/format";
import { useShallow } from "zustand/react/shallow";

interface Props {
  targetName: string;
  rowHeight: number;
  remainHp: number;
  maxHp: number;
}

export const TargetInfo = memo(({ targetName, rowHeight, remainHp, maxHp }: Props) => {
  const { theme, targetInfoDisplayMode, overlayTheme } = useSettingsStore(
    useShallow((s) => ({
      theme: s.theme,
      targetInfoDisplayMode: s.targetInfoDisplayMode,
      overlayTheme: s.overlayTheme,
    })),
  );
  const displayName = targetName || "타겟 인식 실패";
  const isFailed = !targetName;
  const isLightOverlay = overlayTheme === "light";

  const iconSize = Math.round(rowHeight * 0.7);
  const fontSize = `${Math.max(10, Math.round(rowHeight * 0.4))}px`;
  const percent = maxHp > 0 ? `${((remainHp / maxHp) * 100).toFixed(1)}%` : "0%";
  const hpRatio = maxHp > 0 ? Math.max(0, Math.min(1, remainHp / maxHp)) : 0;

  const renderHpValue = (value: string) => <span>{value}</span>;

  const renderStats = () => {
    switch (targetInfoDisplayMode) {
      case "hp_percent":
        return (
          <>
            <span>{renderHpValue(formatAmount(remainHp))}</span>
            <span className="mx-1 text-white/45">/</span>
            <span>{formatAmount(maxHp)}</span>{" "}
            <span className="ml-2">{renderHpValue(percent)}</span>
          </>
        );
      case "remain_full_percent":
        return (
          <>
            <span>{renderHpValue(remainHp.toLocaleString())}</span>
            <span className="ml-2">{renderHpValue(percent)}</span>
          </>
        );
      case "remain_percent":
        return (
          <>
            <span>{renderHpValue(formatAmount(remainHp))}</span>
            <span className="ml-2">{renderHpValue(percent)}</span>
          </>
        );
      case "percent":
        return <span className="ml-2">{renderHpValue(percent)}</span>;
      case "hp_full_percent":
      default:
        return (
          <>
            <span>{renderHpValue(remainHp.toLocaleString())}</span>
            <span className="mx-1 text-white/45">/</span>
            <span>{maxHp.toLocaleString()}</span>
            <span className="ml-2">{renderHpValue(percent)}</span>
          </>
        );
    }
  };

  return (
    <div
      className="relative mb-2 w-full overflow-hidden rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-row-bg)] px-2"
      style={{ height: rowHeight }}>
      <div
        className="absolute inset-0 bg-[var(--meter-tint)]"
      />
      <div
        className="absolute inset-y-0 left-0 origin-left transition-transform duration-150 ease-out"
        style={{
          background: `linear-gradient(to right, ${theme.bossBar[0]}, ${theme.bossBar[1]})`,
          opacity: isFailed ? 0.22 : 0.78,
          width: "100%",
          transform: `scaleX(${isFailed ? 1 : hpRatio})`,
        }}
      />
      <div
        className="absolute inset-0"
        style={{ background: "var(--meter-shine)" }}
      />
      <div className="relative flex h-full items-center gap-2">
        <div
          className="flex shrink-0 items-center justify-center rounded-full bg-[var(--meter-row-bg)] ring-1 ring-[var(--meter-icon-ring)]"
          style={{ width: iconSize, height: iconSize }}>
          <img
            src={bossIcon}
            draggable={false}
            className={`h-full w-full object-contain p-0.5 ${isFailed ? "opacity-40" : ""}`}
          />
        </div>
        <span
          className="text-shadow-meter min-w-0 truncate font-semibold"
          style={{
            color: isFailed
              ? "var(--meter-muted)"
              : isLightOverlay
                ? "#111827"
                : "#ffffff",
            fontSize,
          }}>
          {displayName}
        </span>

        {!isFailed && (
          <div
            className="text-shadow-meter ml-auto flex shrink-0 items-center whitespace-nowrap text-end font-semibold tabular-nums"
            style={{ color: isLightOverlay ? "#991b1b" : theme.bossRightValue, fontSize }}>
            {renderStats()}
          </div>
        )}
      </div>
    </div>
  );
});
