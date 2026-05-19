interface Props {
  isInCombat: boolean;
  combatTime: string;
}
import { useSettingsStore } from "@/stores/useSettingsStore";

export const CombatTimer = ({ isInCombat, combatTime }: Props) => {
  const combatTimeColor = useSettingsStore((s) => s.theme.combatTimeColor);
  const overlayTheme = useSettingsStore((s) => s.overlayTheme);
  const standbyColor = overlayTheme === "light" ? "#334155" : combatTimeColor;
  return (
    <div className="mt-2 flex items-center gap-2 rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-row-bg)] px-2 py-1.5">
      <div
        className="h-2 w-2 rounded-full transition-colors duration-300"
        style={{
          background: isInCombat ? "#55c42a" : standbyColor,
          boxShadow: isInCombat ? "0 0 6px #55c42a" : "none",
        }}
      />
      <span
        className="text-xs font-semibold"
        style={{ color: isInCombat ? "#16a34a" : standbyColor }}>
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
