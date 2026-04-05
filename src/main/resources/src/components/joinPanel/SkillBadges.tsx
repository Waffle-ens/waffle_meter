import { useRef, useLayoutEffect, useState } from "react";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { SkillIcon } from "../SkillIcon";
import { getBadgeColor } from "@/utils/badgeColor";

interface SkillBadge {
  name: string;
  code: string;
  lv: number;
}
export const SkillBadges = ({ badges, job }: { badges: SkillBadge[]; job?: string }) => {
  const badgeClass = getBadgeColor(job);
  const containerRef = useRef<HTMLDivElement>(null);
  const measureRef = useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = useState(badges.length);
  useLayoutEffect(() => {
    setVisibleCount(badges.length);
  }, [badges.length]);
  useLayoutEffect(() => {
    if (!containerRef.current || !measureRef.current) return;
    const containerWidth = containerRef.current.clientWidth - 10;
    const reservedWidth = 44;
    const children = Array.from(measureRef.current.children) as HTMLElement[];

    let total = 0;
    let count = 0;
    for (const child of children) {
      const w = child.offsetWidth + 4;
      if (total + w > containerWidth - reservedWidth) break;
      total += w;
      count++;
    }
    setVisibleCount(count === badges.length ? badges.length : Math.max(0, count));
  }, [badges]);

  const visible = badges.slice(0, visibleCount);
  const hidden = badges.slice(visibleCount);

  return (
    <div
      ref={containerRef}
      className="relative w-full overflow-hidden ">
      <div className="flex flex-wrap gap-1">
        {visible.map(({ code, name, lv }) => (
          <div
            key={code}
            className={`${badgeClass} flex items-center text-xs px-2 py-1 gap-1 rounded-xl `}>
            <SkillIcon
              code={code}
              size={14}
            />
            <span key={name}>
              {name} Lv{lv}
            </span>
          </div>
        ))}
        {hidden.length > 0 && (
          <TooltipProvider delayDuration={10}>
            <Tooltip>
              <TooltipTrigger asChild>
                <div className="py-0 px-2 rounded-full border border-white/30 cursor-pointer">
                  <span className="text-xs">+{hidden.length}</span>
                </div>
              </TooltipTrigger>
              <TooltipContent
                side="top"
                className="p-2 bg-[rgba(12,22,40,0.8)] ">
                <div className=" flex flex-wrap gap-2 p-2 ">
                  {hidden.map(({ code, name, lv }) => (
                    <div
                      className={`${badgeClass} flex items-center text-xs px-2 py-1 gap-1 rounded-xl `}>
                      <SkillIcon
                        code={code}
                        size={14}
                      />
                      <span key={name}>
                        {name} Lv{lv}
                      </span>
                    </div>
                  ))}
                </div>
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        )}
      </div>

      <div
        ref={measureRef}
        className="flex gap-1 absolute invisible pointer-events-none top-0 left-0 w-full overflow-hidden">
        {badges.map(({ code, name, lv }) => (
          <div
            key={code}
            className="flex items-center text-xs px-2 py-1 gap-1 rounded-xl  whitespace-nowrap">
            <SkillIcon
              code={code}
              size={14}
            />
            <span>
              {name} Lv{lv}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
};
