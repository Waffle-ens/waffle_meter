import { memo, useMemo } from "react";
import { getJobIconSrc } from "@/utils/icons";
import { formatPower } from "@/utils/format";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";

interface Props {
  rank: number;
  id: number;
  name: string;
  job?: string;
  dps: number;
  contribution: number;
  entireContribution: number;
  isUser: boolean;
  isSelected: boolean;
  onSelect: (id: number) => void;
  topDps: number;
  rowHeight: number;
  server: number;
  power: number;
}

const makeGradient = (from: string, to: string) => `linear-gradient(to right, ${from}, ${to})`;

export const MeterRow = memo(
  ({
    id,
    rank,
    name,
    job,
    dps,
    server,
    contribution,
    entireContribution,
    isUser,
    isSelected,
    onSelect,
    topDps,
    rowHeight,
    power,
  }: Props) => {
    const { displayMode, nameDisplay, theme, contributionMode, overlayTheme } = useSettingsStore(
      useShallow((s) => ({
        displayMode: s.displayMode,
        nameDisplay: s.nameDisplay,
        theme: s.theme,
        contributionMode: s.contributionMode,
        overlayTheme: s.overlayTheme,
      })),
    );
    const gradients = useMemo(
      () => ({
        user: makeGradient(...theme.userBar),
        normal: makeGradient(...theme.normalBar),
        warning: makeGradient(...theme.warningBar),
        error: makeGradient(...theme.errorBar),
      }),
      [theme.errorBar, theme.normalBar, theme.userBar, theme.warningBar],
    );
    const isLightOverlay = overlayTheme === "light";
    const nameColor = !server
      ? isLightOverlay
        ? "#1e293b"
        : theme.serverDefaultColor
      : server >= 1001 && server <= 1021
        ? isLightOverlay
          ? "#0369a1"
          : theme.serverAColor
        : server >= 2001 && server <= 2021
          ? isLightOverlay
            ? "#a21caf"
            : theme.serverBColor
          : isLightOverlay
            ? "#1e293b"
            : theme.serverDefaultColor;

    const maskedName = (name: string) => (name ? `${name[0]}***` : "***");
    const iconSize = Math.round(rowHeight * 0.68);
    const fontSize = `${Math.max(10, Math.floor(rowHeight * 0.4))}px`;
    const secondaryFontSize = `${Math.max(9, Math.floor(rowHeight * 0.32))}px`;

    const ratio = Math.max(0, Math.min(1, dps / topDps));
    const iconSrc = getJobIconSrc(job);
    const fillGradient = isUser
      ? gradients.user
      : Number(contribution) < 3
        ? gradients.error
        : Number(contribution) < 5
          ? gradients.warning
          : gradients.normal;
    const progressWidth = ratio > 0 ? `${Math.max(1.5, ratio * 100)}%` : "0%";

    const statItems = useMemo(() => {
      const powerColor = isLightOverlay ? "#8a5a00" : theme.meterStatAmount;
      const dpsColor = isLightOverlay ? "#102033" : theme.meterStatDps;
      const percentColor = isLightOverlay ? "#047857" : theme.meterStatPercent;
      const pct = contributionMode === "entireContribution" ? entireContribution : contribution;
      const powerText = formatPower(power);
      const dpsText = `${dps.toLocaleString()}/s`;
      const pctText = `${pct.toFixed(1)}%`;

      switch (displayMode) {
        case "amount_dps_percent":
          return [
            { key: "power", color: powerColor, value: powerText },
            { key: "dps", color: dpsColor, value: dpsText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_percent":
          return [
            { key: "power", color: powerColor, value: powerText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_full_dps_percent":
          return [
            { key: "power", color: powerColor, value: powerText },
            { key: "dps", color: dpsColor, value: dpsText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_full_percent":
          return [
            { key: "power", color: powerColor, value: powerText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "dps_percent":
        default:
          return [
            { key: "dps", color: dpsColor, value: dpsText },
            { key: "percent", color: percentColor, value: pctText },
          ];
      }
    }, [
      contribution,
      contributionMode,
      displayMode,
      dps,
      entireContribution,
      power,
      theme.meterStatAmount,
      theme.meterStatDps,
      theme.meterStatPercent,
      isLightOverlay,
    ]);

    const displayName = useMemo(() => {
      switch (nameDisplay) {
        case "all":
          return name;
        case "me_only":
          return isUser ? name : maskedName(name);
        case "hidden":
          return maskedName(name);
      }
    }, [isUser, name, nameDisplay]);

    return (
      <div
        onClick={() => onSelect(id)}
        data-selected={isSelected}
        style={{ height: rowHeight }}
        className="meter-row group/row relative w-full cursor-pointer overflow-hidden rounded-md border px-2.5 transition-colors">
        <div
          className="absolute inset-y-1 left-1 w-1 rounded-full"
          style={{
            background: fillGradient,
            opacity: isUser ? 0.95 : 0.82,
          }}
        />
        <div
          className="absolute inset-x-0 bottom-0 h-[2px] transition-[width] duration-150 ease-out"
          style={{
            width: progressWidth,
            background: fillGradient,
            opacity: isUser ? 0.95 : 0.74,
          }}
        />
        <div
          className="absolute inset-0 opacity-80"
          style={{ background: "var(--meter-shine)" }}
        />
        <div className="relative flex h-full items-center gap-2.5 overflow-hidden pl-1.5">
          <span
            className="flex h-5 w-6 shrink-0 items-center justify-center rounded bg-[var(--meter-stat-bg)] text-center font-semibold tabular-nums text-[var(--meter-muted)] ring-1 ring-[var(--meter-soft-border)]"
            style={{ fontSize: secondaryFontSize }}>
            {rank}
          </span>
          <div
            style={{ width: iconSize, height: iconSize }}
            className="flex shrink-0 items-center justify-center rounded-md bg-[var(--meter-stat-bg)] ring-1 ring-[var(--meter-icon-ring)]">
            {iconSrc && (
              <img
                src={iconSrc}
                draggable={false}
                className="h-full w-full object-contain p-0.5"
                style={{ filter: "drop-shadow(0 0 3px rgba(20,20,20,0.6))" }}
              />
            )}
          </div>
          <span
            className="text-shadow-meter min-w-0 flex-1 truncate font-semibold"
            style={{ color: nameColor, fontSize }}>
            {displayName}
          </span>
          <div className="text-shadow-meter flex shrink-0 items-center gap-1.5 font-semibold tabular-nums">
            {statItems.map((item) => (
              <span
                key={item.key}
                className="rounded bg-[var(--meter-stat-bg)] px-1.5 py-0.5 text-end whitespace-nowrap ring-1 ring-[var(--meter-soft-border)]"
                style={{ color: item.color, fontSize }}>
                {item.value}
              </span>
            ))}
          </div>
        </div>
      </div>
    );
  },
  (prev, next) => {
    return (
      prev.dps === next.dps &&
      prev.rank === next.rank &&
      prev.power === next.power &&
      prev.contribution === next.contribution &&
      prev.entireContribution === next.entireContribution &&
      prev.server === next.server &&
      prev.isUser === next.isUser &&
      prev.isSelected === next.isSelected &&
      prev.topDps === next.topDps &&
      prev.name === next.name &&
      prev.job === next.job &&
      prev.rowHeight === next.rowHeight
    );
  },
);
