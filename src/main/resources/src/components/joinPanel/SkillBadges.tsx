import { SkillIcon } from "../SkillIcon";
import { getClassColor } from "@/utils/classColor";
import { useSettingsStore } from "@/stores/useSettingsStore";

interface SkillBadge {
  name: string;
  code: string;
  lv: number;
}

export const SkillBadges = ({ badges, job }: { badges: SkillBadge[]; job?: string }) => {
  const overlayTheme = useSettingsStore((s) => s.overlayTheme);
  const isLightOverlay = overlayTheme === "light";
  const badgeClass = getClassColor(job, isLightOverlay ? "light" : "dark");

  return (
    <div className="flex flex-wrap gap-1">
      {badges.map(({ code, name, lv }) => (
        <div
          key={code}
          className={`${badgeClass} flex items-center gap-1.5 rounded-md px-2 py-1 text-xs`}>
          <SkillIcon code={code} size={14} />
          <span>{name} Lv{lv}</span>
        </div>
      ))}
    </div>
  );
};
