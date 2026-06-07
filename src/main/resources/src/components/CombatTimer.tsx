interface Props {
  isInCombat: boolean;
  combatTime: string;
}
import { useSettingsStore } from "@/stores/useSettingsStore";

export const CombatTimer = ({ isInCombat, combatTime }: Props) => {
  const combatTimeColor = useSettingsStore((s) => s.theme.combatTimeColor);
  const overlayTheme = useSettingsStore((s) => s.overlayTheme);
  const standbyColor = overlayTheme === "light" ? "#334155" : combatTimeColor;
  const activeColor = overlayTheme === "light" ? "#0f766e" : "#2dd4bf";
  return (
    <div
      data-meter-timer="true"
      className="mt-2 flex items-center gap-2 rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-row-bg)] px-2.5 py-1.5">
      <div
        className="h-2 w-2 rounded-full transition-colors duration-300"
        style={{
          background: isInCombat ? activeColor : standbyColor,
          boxShadow: isInCombat ? `0 0 8px ${activeColor}` : "none",
        }}
      />
      <span
        className="rounded bg-[var(--meter-stat-bg)] px-1.5 py-0.5 text-xs font-semibold ring-1 ring-[var(--meter-soft-border)]"
        style={{ color: isInCombat ? activeColor : standbyColor }}>
        {isInCombat ? "전투 중" : "대기 중"}
      </span>
      <span
        className="ml-auto text-xs font-semibold tabular-nums"
        style={{ color: standbyColor }}>
        {combatTime}
      </span>
    </div>
  );
};
