import { memo, useMemo } from "react";
import { getJobIconSrc } from "@/utils/icons";
import { formatAmount } from "@/utils/format";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";

interface Props {
  rank: number;
  id: number;
  name: string;
  job?: string;
  dps: number;
  amount: number;
  contribution: number;
  entireContribution: number;
  isUser: boolean;
  isSelected: boolean;
  onSelect: (id: number) => void;
  topDps: number;
  rowHeight: number;
  server: number;
  // power: number;
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
    amount,
    rowHeight,
    // power,
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
    // const showPower = useSettingsStore((s) => s.showPower);

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

    const statItems = useMemo(() => {
      const amountColor = isLightOverlay ? "#8a5a00" : theme.meterStatAmount;
      const dpsColor = isLightOverlay ? "#102033" : theme.meterStatDps;
      const percentColor = isLightOverlay ? "#047857" : theme.meterStatPercent;
      const pct = contributionMode === "entireContribution" ? entireContribution : contribution;
      const compactAmount = formatAmount(amount);
      const fullAmount = amount.toLocaleString();
      const dpsText = `${dps.toLocaleString()}/초`;
      const pctText = `${pct.toFixed(1)}%`;

      switch (displayMode) {
        case "amount_dps_percent":
          return [
            { key: "amount", color: amountColor, value: compactAmount },
            { key: "dps", color: dpsColor, value: dpsText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_percent":
          return [
            { key: "amount", color: amountColor, value: compactAmount },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_full_dps_percent":
          return [
            { key: "amount", color: amountColor, value: fullAmount },
            { key: "dps", color: dpsColor, value: dpsText },
            { key: "percent", color: percentColor, value: pctText },
          ];
        case "amount_full_percent":
          return [
            { key: "amount", color: amountColor, value: fullAmount },
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
      amount,
      contribution,
      contributionMode,
      displayMode,
      dps,
      entireContribution,
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
        className="meter-row group/row relative w-full cursor-pointer overflow-hidden rounded-md border px-2 transition-colors">
        <div
          className="absolute inset-y-0 left-0 origin-left transition-transform duration-150 ease-out"
          style={{
            background: fillGradient,
            width: "100%",
            opacity: isUser ? 0.78 : 0.62,
            transform: `scaleX(${ratio})`,
          }}
        />
        <div
          className="absolute inset-0 opacity-70"
          style={{ background: "var(--meter-shine)" }}
        />
        <div className="relative flex h-full items-center gap-2 overflow-hidden">
          <span
            className="w-5 shrink-0 text-center font-semibold tabular-nums text-[var(--meter-muted)]"
            style={{ fontSize: secondaryFontSize }}>
            {rank}
          </span>
          <div
            style={{ width: iconSize, height: iconSize }}
            className="flex shrink-0 items-center justify-center rounded-full bg-[var(--meter-row-bg)] ring-1 ring-[var(--meter-icon-ring)]">
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
          {/* <div className="flex gap-1.5 flex-1 items-center "> */}
          {/* {showPower && power > 0 && (
              <div
                className={`bg-black/30 px-2     text-shadow-meter flex items-center rounded-xl `}>
                <span
                  className="text-[#10f1e2] font-semibold  py-1 leading-none"
                  style={{
                    fontSize: `${parseInt(fontSize) - 2}px`,
                  }}>{`${(power / 1000).toFixed(1)}k`}</span>
              </div>
            )} */}
          {/* </div> */}
          <div className="text-shadow-meter flex shrink-0 items-center gap-2 font-semibold tabular-nums">
            {statItems.map((item) => (
              <span
                key={item.key}
                className="whitespace-nowrap text-end"
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
      prev.amount === next.amount &&
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
