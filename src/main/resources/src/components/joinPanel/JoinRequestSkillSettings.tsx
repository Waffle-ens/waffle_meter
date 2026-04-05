import { useRef, useState, useEffect } from "react";
import { Settings, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { SkillIcon } from "../SkillIcon";
import { getBadgeColor } from "@/utils/badgeColor";
import { GROUPED_BY_JOB, getSkillName } from "@/constants/codes";

export const JoinRequestSkillSettings = () => {
  const [open, setOpen] = useState(false);
  const { visibleSkillCodes, setVisibleSkillCodes } = useSettingsStore();
  const panelRef = useRef<HTMLDivElement>(null);
  const btnRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (
        panelRef.current &&
        !panelRef.current.contains(e.target as Node) &&
        btnRef.current &&
        !btnRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  const toggle = (code: number) => {
    if (visibleSkillCodes.includes(code)) {
      setVisibleSkillCodes(visibleSkillCodes.filter((c) => c !== code));
    } else {
      setVisibleSkillCodes([...visibleSkillCodes, code]);
    }
  };

  const toggleAll = (codes: number[], checked: boolean) => {
    if (checked) {
      setVisibleSkillCodes(Array.from(new Set([...visibleSkillCodes, ...codes])));
    } else {
      setVisibleSkillCodes(visibleSkillCodes.filter((c) => !codes.includes(c)));
    }
  };

  return (
    <div className=" cursor-auto">
      <Button
        ref={btnRef}
        variant="ghost"
        size="icon"
        onClick={() => setOpen((v) => !v)}>
        <Settings />
      </Button>

      {open && (
        <div
          ref={panelRef}
          className="min-w-0 absolute w-100 top-0 left-full ml-2 h-auto z-50 bg-[rgba(12,22,40,0.8)] text-white rounded-lg">
          <div className="p-4 flex items-center justify-between">
            <span className="text-sm">표시할 스킬 선택</span>
            <Button
              variant="ghost"
              className="opacity-70 hover:opacity-100"
              onClick={() => setOpen(false)}>
              <X />
            </Button>
          </div>

          <div className="px-5 pb-3 space-y-4 max-h-96 overflow-y-auto">
            {GROUPED_BY_JOB.map(({ job, skills }) => {
              if (skills.length === 0) return null;
              const allChecked = skills.every((c) => visibleSkillCodes.includes(c));

              return (
                <div
                  key={job}
                  className="py-1">
                  <div className="flex items-center justify-between mb-3">
                    <span className="text-xs text-white/40 font-semibold">{job}</span>
                    <button
                      onClick={() => toggleAll(skills, !allChecked)}
                      className="cursor-pointer text-xs text-white/30 hover:text-white/60 transition-colors">
                      {allChecked ? "전체 해제" : "전체 선택"}
                    </button>
                  </div>

                  <div className="flex flex-wrap gap-2">
                    {skills.map((code) => {
                      const name = getSkillName(code) ?? code;
                      const active = visibleSkillCodes.includes(code);
                      return (
                        <Button
                          key={code}
                          size="sm"
                          onClick={() => toggle(code)}
                          className={`flex items-center text-xs px-3 gap-1 rounded-xl ${getBadgeColor(job)} ${
                            active ? "opacity-100" : "opacity-40"
                          }`}>
                          <SkillIcon
                            code={code}
                            size={14}
                          />
                          <span>{name}</span>
                        </Button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};
