import { useEffect, useLayoutEffect, useRef, useState } from "react";
import type { Player, Details } from "@/types";
import { useDetails } from "@/hooks/useDetails";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useResizableDetail } from "@/hooks/useResizableDetail";
import { BuffRateSection } from "@/components/BuffRateSection";
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from "@/components/ui/table";
import { SkillIcon } from "../SkillIcon";
interface Props {
  player: Player | null;
  onClose: () => void;
  onReady?: () => void;
  combatTime: string;
  historyIdx?: number;
}

export const DetailsPanel = ({ player, onClose, onReady, combatTime, historyIdx }: Props) => {
  const { getDetails } = useDetails();
  const [details, setDetails] = useState<Details | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const [hasScroll, setHasScroll] = useState(false);
  const { detailHeight, detailWidth, onMouseDownVertical, onMouseDownHorizontal } =
    useResizableDetail();
  const buffColumns = detailWidth >= 1200 ? 4 : detailWidth >= 900 ? 3 : detailWidth >= 700 ? 2 : 1;
  const isCompact = detailWidth < 700;

  useEffect(() => {
    if (!player) return;
    setDetails(null);
    getDetails(player, combatTime, historyIdx).then(setDetails);
  }, [player]);

  useLayoutEffect(() => {
    if (details) {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          onReady?.();
        });
      });
    }
    if (scrollRef.current) {
      setHasScroll(scrollRef.current.scrollHeight > scrollRef.current.clientHeight);
    }
  }, [details?.skills]);

  const FIXED_AREA_HEIGHT = isCompact ? 220 : 264;

  if (!player || !details) return null;

  return (
    <div
      style={{ width: detailWidth }}
      className=" relative text-white font-bold rounded-lg py-4 px-7">
      <div className="flex items-center pb-3 border-b border-white/10">
        <span>{player.name} 상세내역</span>
        <Button
          className="ml-auto"
          variant="ghost"
          onClick={onClose}>
          <X className="scale-125" />
        </Button>
      </div>
      <div className="grid grid-cols-4 gap-2 py-3">
        {[
          { label: "누적 피해량", value: details.totalDmg.toLocaleString() },
          { label: "피해량 기여도", value: `${details.contributionPct.toFixed(1)}%` },
          { label: "치명타 비율", value: `${details.totalCritPct}%` },
          { label: "완벽 비율", value: `${details.totalPerfectPct}%` },
          { label: "강타 비율", value: `${details.totalDoublePct}%` },
          { label: "백어택 비율", value: `${details.totalBackPct}%` },
          { label: "보스 막기비율", value: `${details.totalParryPct}%` },
          { label: "전투시간", value: combatTime },
        ].map(({ label, value }) => (
          <div
            key={label}
            className="bg-white/5 rounded-md px-3 py-3">
            <p className="text-xs text-white/50 mb-1">{label}</p>
            <p className="text-sm font-bold">{value}</p>
          </div>
        ))}
      </div>

      <BuffRateSection
        buffOperatingRate={details.buffOperatingRate}
        columns={buffColumns}
      />
      <div
        ref={scrollRef}
        className={`overflow-y-auto ${hasScroll ? "pr-1" : ""}`}
        style={{ maxHeight: Math.max(100, detailHeight - FIXED_AREA_HEIGHT) }}>
        {isCompact ? (
          <div className="space-y-1.5">
            {details.skills.map((s) => {
              const ratio = s.dmg / (details.totalDmg || 1);
              const stats = [
                { label: "명중", value: s.time },
                { label: "봉혼석", value: s.shardTimes },
                { label: "치명", value: s.critPct === "-" ? "-" : `${s.critPct}%` },
                { label: "패리", value: s.parryPct === "-" ? "-" : `${s.parryPct}%` },
                { label: "완벽", value: s.perfectPct === "-" ? "-" : `${s.perfectPct}%` },
                { label: "강타", value: s.doublePct === "-" ? "-" : `${s.doublePct}%` },
                { label: "백어택", value: s.backPct === "-" ? "-" : `${s.backPct}%` },
              ];

              return (
                <div
                  key={s.code}
                  className="rounded-lg p-3"
                  style={{ borderBottom: "1px solid rgba(255,255,255,0.08)" }}>
                  <div className="flex items-center gap-2 mb-2">
                    <SkillIcon
                      code={s.code}
                      size={26}
                    />
                    <span className="text-sm truncate flex-1 text-row-fill text-shadow-meter">
                      {s.name}
                    </span>
                    <span className=" text-row-fill text-sm text-shadow-meter shrink-0 ">
                      {(ratio * 100).toFixed(1)}%
                    </span>
                  </div>

                  <div className="relative h-6 rounded-md overflow-hidden mb-2.5">
                    <div
                      className="absolute inset-0 origin-left rounded-md"
                      style={{
                        background: "linear-gradient(to right, #55c42a, #3a9e20)",
                        transform: `scaleX(${ratio})`,
                      }}
                    />
                    <div className="absolute inset-0 bg-white/5 rounded-md" />
                    <span className="absolute inset-0 flex items-center justify-end pr-2 text-xs font-bold text-row-fill text-shadow-meter ">
                      {s.dmg.toLocaleString()}
                    </span>
                  </div>

                  <div className="flex flex-wrap gap-1.5">
                    {stats.map(({ label, value }) => (
                      <div
                        key={label}
                        className="bg-gray-600 flex items-center gap-2 rounded-lg px-3 py-1">
                        <span className="text-xs">{label}</span>
                        <span className="text-xs font-bold tabular-nums">{value}</span>
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <Table className="w-full table-fixed text-sm border-collapse">
            <colgroup>
              <col />
              {Array.from({ length: 7 }).map((_, i) => (
                <col
                  key={i}
                  style={{ width: "9%" }}
                />
              ))}
              <col style={{ width: "22%" }} />
            </colgroup>

            {/* ✅ sticky 유지 */}
            <TableHeader className="sticky top-0 z-50 ">
              <TableRow className="text-center border-b border-white/10 ">
                <TableHead className="text-left  py-2 font-bold text-white">스킬명</TableHead>
                <TableHead className="py-2 font-bold text-white">명중 횟수</TableHead>
                <TableHead className="py-2 font-bold text-center text-white">봉혼석</TableHead>
                <TableHead className="py-2 text-center font-bold text-white">치명타</TableHead>
                <TableHead className="py-2 text-center font-bold text-white">패리</TableHead>
                <TableHead className="py-2 text-center font-bold text-white">완벽</TableHead>
                <TableHead className="py-2 text-center font-bold text-white">강타</TableHead>
                <TableHead className="py-2 text-center font-bold text-white">백어택</TableHead>
                <TableHead className="py-2 font-bold text-center text-white">누적 피해량</TableHead>
              </TableRow>
            </TableHeader>

            <TableBody>
              {details.skills.map((s, i) => {
                const ratio = s.dmg / (details.totalDmg || 1);

                return (
                  <TableRow
                    className="cursor-auto"
                    key={s.code}
                    style={{
                      borderBottom:
                        i < details.skills.length - 1 ? "1px solid rgba(255,255,255,0.08)" : "none",
                    }}>
                    <TableCell className="py-1.5">
                      <div className="flex items-center gap-2 overflow-hidden">
                        <SkillIcon
                          code={s.code}
                          size={26}
                        />
                        <span className="truncate text-row-fill text-shadow-meter">{s.name}</span>
                      </div>
                    </TableCell>

                    {[
                      s.time,
                      s.shardTimes,
                      s.critPct === "-" ? "-" : `${s.critPct}%`,
                      s.parryPct === "-" ? "-" : `${s.parryPct}%`,
                      s.perfectPct === "-" ? "-" : `${s.perfectPct}%`,
                      s.doublePct === "-" ? "-" : `${s.doublePct}%`,
                      s.backPct === "-" ? "-" : `${s.backPct}%`,
                    ].map((val, ci) => (
                      <TableCell
                        key={ci}
                        className="py-1.5 text-center">
                        {val}
                      </TableCell>
                    ))}

                    <TableCell className="py-1.5">
                      <div className="relative h-8 rounded-md overflow-hidden">
                        <div
                          className="absolute inset-0 origin-left rounded-md"
                          style={{
                            background: "linear-gradient(to right, #55c42a, #3a9e20)",
                            transform: `scaleX(${ratio})`,
                          }}
                        />
                        <div className="relative z-10 h-full flex items-center justify-end gap-1.5 pr-2 text-row-fill text-shadow-meter">
                          <span>{s.dmg.toLocaleString()}</span>
                          <span className="opacity-70">({(ratio * 100).toFixed(1)}%)</span>
                        </div>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </div>

      <div
        onMouseDown={onMouseDownVertical}
        className="resizeHandle absolute bottom-1 left-1/2 -translate-x-1/2 w-24 h-3 cursor-s-resize flex items-center justify-center opacity-60 hover:opacity-100 transition-opacity group">
        <div className="w-8 h-1 rounded-full bg-white transition-colors" />
      </div>
      <div
        onMouseDown={onMouseDownHorizontal}
        className="resizeHandle absolute top-1/2 -translate-y-1/2 right-2 w-3 h-24 cursor-e-resize flex items-center justify-center opacity-60 hover:opacity-100 transition-opacity group">
        <div className="w-1 h-10 rounded-full bg-white" />
      </div>
    </div>
  );
};
