import { Fragment, useEffect, useMemo, useState } from "react";
import type { Player, Details } from "@/types";
import { useDetails } from "@/hooks/useDetails";
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
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger } from "../ui/accordion";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useShallow } from "zustand/react/shallow";
import { getChainMainCode, normalizeChainCode, SKILL_CHAIN_GROUPS } from "@/constants/skillChains";
import { Minus, Plus } from "lucide-react";

interface Props {
  player: Player | null;
  onReady?: () => void;
  players: Player[];

  combatTime: string;
  historyIdx?: number;
}

interface SkillGroupRow {
  key: string;
  skill: Details["skills"][number];
  detailRows: Details["skills"];
}

const pctInt = (num: number, den: number) => (den > 0 ? Math.round((num / den) * 100) : 0);

const mergeSkills = (base: Details["skills"][number], rows: Details["skills"]) => {
  const time = rows.reduce((sum, row) => sum + row.time, 0);
  const crit = rows.reduce((sum, row) => sum + row.crit, 0);
  const parry = rows.reduce((sum, row) => sum + row.parry, 0);
  const back = rows.reduce((sum, row) => sum + row.back, 0);
  const perfect = rows.reduce((sum, row) => sum + row.perfect, 0);
  const double = rows.reduce((sum, row) => sum + row.double, 0);
  const shardTimes = rows.reduce((sum, row) => sum + row.shardTimes, 0);

  return {
    ...base,
    time,
    crit,
    parry,
    back,
    perfect,
    double,
    shardTimes,
    dmg: rows.reduce((sum, row) => sum + row.dmg, 0),
    critPct: pctInt(crit, time),
    parryPct: pctInt(parry, time),
    perfectPct: pctInt(perfect, time),
    doublePct: pctInt(double, time),
    backPct: pctInt(back, time),
  };
};

const buildSkillGroups = (skills: Details["skills"]): SkillGroupRow[] => {
  const byCode = new Map<string, Details["skills"]>();
  const usedIndexes = new Set<number>();

  skills.forEach((skill) => {
    const code = normalizeChainCode(skill.code);
    byCode.set(code, [...(byCode.get(code) ?? []), skill]);
  });

  const groups: SkillGroupRow[] = [];

  for (const chain of SKILL_CHAIN_GROUPS) {
    const orderedCodes = [chain.mainCode, ...chain.childCodes];
    const rows = orderedCodes.flatMap((code) => byCode.get(code) ?? []);
    if (rows.length < 2) continue;

    const mainRows = byCode.get(chain.mainCode);
    const base = mainRows?.[0] ?? rows[0];
    groups.push({
      key: chain.mainCode,
      skill: mergeSkills(base, rows),
      detailRows: rows,
    });

    skills.forEach((skill, index) => {
      if (getChainMainCode(skill.code) === chain.mainCode) usedIndexes.add(index);
    });
  }

  skills.forEach((skill, index) => {
    if (usedIndexes.has(index)) return;
    groups.push({
      key: `${skill.code}-${index}`,
      skill,
      detailRows: [skill],
    });
  });

  return groups.sort((a, b) => b.skill.dmg - a.skill.dmg);
};

export const DetailsPanel = ({
  player,
  players,
  combatTime,
  historyIdx,
}: Props) => {
  const { getDetails } = useDetails();
  const [details, setDetails] = useState<Details | null>(null);
  const { detailWidth, contributionMode } = useSettingsStore(
    useShallow((s) => ({
      detailWidth: s.detailWidth,
      contributionMode: s.contributionMode,
    })),
  );
  const buffColumns = detailWidth >= 1200 ? 4 : detailWidth >= 900 ? 3 : detailWidth >= 700 ? 2 : 1;
  const isCompact = detailWidth < 700;
  const [openPanel, setOpenPanel] = useState<string>("skills");
  const [expandedSkillGroups, setExpandedSkillGroups] = useState<Set<string>>(() => new Set());

  const playerNameMap = useMemo(() => new Map(players.map((p) => [p.id, p.name])), [players]);

  useEffect(() => {
    if (!player) return;
    let ignore = false;
    setDetails(null);
    setExpandedSkillGroups(new Set());
    getDetails(player, combatTime, historyIdx).then((next) => {
      if (!ignore) setDetails(next);
    });
    return () => {
      ignore = true;
    };
  }, [combatTime, historyIdx, player]);

  if (!player || !details) return null;

  const skillGroups = buildSkillGroups(details.skills);
  const buffCount = (details.buffOperatingRate ?? []).length;
  const debuffCount = (details.debuffOperatingRate ?? []).length;
  const sectionTriggerClass =
    "cursor-pointer rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-section-bg)] px-4 py-2.5 text-sm text-[var(--meter-fg)] hover:bg-[var(--meter-row-hover)] hover:no-underline";
  const tableHeadClass =
    "bg-[var(--meter-table-head-bg)] py-2 font-bold text-[var(--meter-table-head-fg)]";
  const skillNameClass = "truncate text-[var(--meter-fg)] text-shadow-meter";
  const damageTextClass = "text-[#b7791f] text-shadow-meter";
  const toggleSkillGroup = (key: string) => {
    setExpandedSkillGroups((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  };

  return (
    <div className="flex h-full min-h-0 w-full flex-col overflow-hidden">
      <div className="grid grid-cols-4 gap-2 py-3 shrink-0">
        {[
          { label: "누적 피해량", value: details.totalDmg.toLocaleString() },
          {
            label: "피해량 기여도",
            value:
              contributionMode === "entireContribution"
                ? `${player.entireContribution.toFixed(1)}%`
                : `${details.contributionPct.toFixed(1)}%`,
          },
          { label: "치명타 비율", value: `${details.totalCritPct}%` },
          { label: "강타 비율", value: `${details.totalDoublePct}%` },
          // { label: "다단히트 비율", value: `${details.totalMultiHitPct}%` },
          { label: "완벽 비율", value: `${details.totalPerfectPct}%` },
          { label: "백어택 비율", value: `${details.totalBackPct}%` },
          { label: "보스 막기비율", value: `${details.totalParryPct}%` },
          { label: "전투시간", value: combatTime },
        ].map(({ label, value }) => (
          <div
            key={label}
            className="rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-table-row-bg)] px-3 py-3">
            <p className="mb-1 text-xs text-[var(--meter-muted)]">{label}</p>
            <p className="text-sm font-bold">{value}</p>
          </div>
        ))}
      </div>
      <div className="flex-1 min-h-0 overflow-y-auto scrollbar-gutter:stable">
        <Accordion
          type="single"
          className="gap-2"
          collapsible
          value={openPanel}
          onValueChange={(val) => {
            if (val !== "") setOpenPanel(val);
          }}>
          <AccordionItem
            value="buff"
            className="border-none ">
            <AccordionTrigger
              className={sectionTriggerClass}
              disabled={buffCount === 0}>
              <div className="flex w-full items-center justify-between pr-2">
                <span className="font-semibold">버프 가동률</span>
                <span className="text-xs opacity-60">
                  {Object.keys(details.buffOperatingRate).length}개
                </span>
              </div>
            </AccordionTrigger>
            <AccordionContent key={buffColumns}>
              <BuffRateSection
                buffOperatingRate={details.buffOperatingRate}
                columns={buffColumns}
                playerJob={player.job}
                playerId={player.id}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem
            value="debuff"
            className="border-none">
            <AccordionTrigger
              className={sectionTriggerClass}
              disabled={debuffCount === 0}>
              <div className="flex w-full items-center justify-between pr-2">
                <span>디버프 가동률</span>
                <span className="text-xs opacity-60">{debuffCount}개 </span>
              </div>
            </AccordionTrigger>
            <AccordionContent key={buffColumns}>
              <BuffRateSection
                buffOperatingRate={details.debuffOperatingRate}
                columns={buffColumns}
                playerJob={player.job}
                playerId={player.id}
                groupByActor={true}
                playerNameMap={playerNameMap}
              />
            </AccordionContent>
          </AccordionItem>

          <AccordionItem
            value="skills"
            className="border-none">
            <AccordionTrigger className={sectionTriggerClass}>
              <span className="font-semibold">스킬 피해량</span>
            </AccordionTrigger>
            <AccordionContent key={buffColumns}>
              <div className="px-2.5 pt-2">
                {isCompact ? (
                  <div className="space-y-1.5">
                    {skillGroups.map((group) => {
                      const s = group.skill;
                      const hasChildren = group.detailRows.length > 1;
                      const isExpanded = expandedSkillGroups.has(group.key);
                      const ratio = s.dmg / (details.totalDmg || 1);
                      const stats = [
                        { label: "명중", value: s.time },
                        // { label: "봉혼석", value: s.shardTimes },
                        { label: "치명타", value: s.critPct === "-" ? "-" : `${s.critPct}%` },
                        { label: "강타", value: s.doublePct === "-" ? "-" : `${s.doublePct}%` },
                        // {
                        //   label: "다단",
                        //   value: s.multiHitPct === "-" ? "-" : `${s.multiHitPct}%`,
                        // },
                        { label: "완벽", value: s.perfectPct === "-" ? "-" : `${s.perfectPct}%` },
                        { label: "백어택", value: s.backPct === "-" ? "-" : `${s.backPct}%` },
                        { label: "패리", value: s.parryPct === "-" ? "-" : `${s.parryPct}%` },
                      ];

                      return (
                        <div
                          key={group.key}
                          className="rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-table-row-bg)] p-3">
                          <div className="flex items-center gap-2 mb-2">
                            {hasChildren ? (
                              <button
                                type="button"
                                className="flex h-5 w-5 shrink-0 items-center justify-center rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-control-bg)] text-[var(--meter-accent)] transition hover:bg-[var(--meter-control-hover)]"
                                title={isExpanded ? "연계기 접기" : "연계기 펼치기"}
                                onClick={() => toggleSkillGroup(group.key)}>
                                {isExpanded ? <Minus size={13} /> : <Plus size={13} />}
                              </button>
                            ) : (
                              <span className="h-5 w-5 shrink-0" />
                            )}
                            <SkillIcon
                              code={s.code}
                              size={26}
                            />
                            <span className={`flex-1 text-sm ${skillNameClass}`}>
                              {s.name}
                            </span>
                            <span className={`shrink-0 text-sm ${damageTextClass}`}>
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
                            <div className="absolute inset-0 rounded-md bg-[var(--meter-tint)]" />
                            <span className={`absolute inset-0 flex items-center justify-end pr-2 text-xs font-bold ${damageTextClass}`}>
                              {s.dmg.toLocaleString()}
                            </span>
                          </div>

                          <div className="flex flex-wrap gap-1.5">
                            {stats.map(({ label, value }) => (
                              <div
                                key={label}
                                className="flex items-center gap-2 rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-row-hover)] px-3 py-1">
                                <span className="text-xs text-[var(--meter-muted)]">{label}</span>
                                <span className="text-xs font-bold tabular-nums">{value}</span>
                              </div>
                            ))}
                          </div>
                          {hasChildren && isExpanded && (
                            <div className="mt-3 space-y-1.5 border-t border-[var(--meter-soft-border)] pt-2">
                              {group.detailRows.map((child, childIndex) => {
                                const childRatio = child.dmg / (details.totalDmg || 1);
                                return (
                                  <div
                                    key={`${group.key}-${child.code}-${childIndex}`}
                                    className="relative ml-2 flex items-center gap-2 rounded-md bg-[var(--meter-row-hover)] px-2 py-1.5">
                                    <span className="absolute -left-2 top-0 h-1/2 w-2 rounded-bl border-b border-l border-[var(--meter-accent)] opacity-70" />
                                    <SkillIcon
                                      code={child.code}
                                      size={20}
                                    />
                                    <span className={`min-w-0 flex-1 truncate text-xs ${skillNameClass}`}>
                                      {child.name}
                                    </span>
                                    <span className={`text-xs tabular-nums ${damageTextClass}`}>
                                      {child.dmg.toLocaleString()} ({(childRatio * 100).toFixed(1)}%)
                                    </span>
                                  </div>
                                );
                              })}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>
                ) : (
                  <Table className="w-full table-fixed text-sm border-collapse">
                    <colgroup>
                      <col />
                      {Array.from({ length: 6 }).map((_, i) => (
                        <col
                          key={i}
                          style={{ width: "9%" }}
                        />
                      ))}
                      <col style={{ width: "22%" }} />
                    </colgroup>

                    <TableHeader className="sticky top-0 z-50">
                      <TableRow className="border-b border-[var(--meter-soft-border)] text-center hover:bg-transparent">
                        <TableHead className={`${tableHeadClass} text-left`}>
                          스킬명
                        </TableHead>
                        <TableHead className={`${tableHeadClass} text-center`}>
                          명중 횟수
                        </TableHead>
                        {/* <TableHead className="py-2 font-bold text-center text-white">
                          봉혼석
                        </TableHead> */}
                        <TableHead className={`${tableHeadClass} text-center`}>
                          치명타
                        </TableHead>
                        <TableHead className={`${tableHeadClass} text-center`}>
                          강타
                        </TableHead>
                        {/* <TableHead className="py-2 text-center font-bold text-white">
                          다단 히트
                        </TableHead> */}
                        <TableHead className={`${tableHeadClass} text-center`}>
                          완벽
                        </TableHead>
                        <TableHead className={`${tableHeadClass} text-center`}>
                          백어택
                        </TableHead>
                        <TableHead className={`${tableHeadClass} text-center`}>
                          패리
                        </TableHead>
                        <TableHead className={`${tableHeadClass} text-center`}>
                          누적 피해량
                        </TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {skillGroups.map((group) => {
                        const s = group.skill;
                        const hasChildren = group.detailRows.length > 1;
                        const isExpanded = expandedSkillGroups.has(group.key);
                        const ratio = s.dmg / (details.totalDmg || 1);

                        return (
                          <Fragment key={group.key}>
                            <TableRow
                              className="cursor-auto border-b border-[var(--meter-soft-border)] bg-[var(--meter-table-row-bg)] even:bg-[var(--meter-table-row-alt)] hover:bg-[var(--meter-row-hover)]"
                            >
                              <TableCell className="py-1.5">
                                <div className="flex items-center gap-2 overflow-hidden">
                                  {hasChildren ? (
                                    <button
                                      type="button"
                                      className="flex h-5 w-5 shrink-0 items-center justify-center rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-control-bg)] text-[var(--meter-accent)] transition hover:bg-[var(--meter-control-hover)]"
                                      title={isExpanded ? "연계기 접기" : "연계기 펼치기"}
                                      onClick={() => toggleSkillGroup(group.key)}>
                                      {isExpanded ? <Minus size={13} /> : <Plus size={13} />}
                                    </button>
                                  ) : (
                                    <span className="h-5 w-5 shrink-0" />
                                  )}
                                  <SkillIcon
                                    code={s.code}
                                    size={26}
                                  />
                                  <span className={skillNameClass}>
                                    {s.name}
                                  </span>
                                </div>
                              </TableCell>

                              {[
                                s.time,
                                // s.shardTimes,
                                s.critPct === "-" ? "-" : `${s.critPct}%`,
                                s.doublePct === "-" ? "-" : `${s.doublePct}%`,
                                // s.multiHitPct === "-" ? "-" : `${s.multiHitPct}%`,
                                s.perfectPct === "-" ? "-" : `${s.perfectPct}%`,
                                s.backPct === "-" ? "-" : `${s.backPct}%`,
                                s.parryPct === "-" ? "-" : `${s.parryPct}%`,
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
                                  <div className={`relative z-10 flex h-full items-center justify-end gap-1.5 pr-2 ${damageTextClass}`}>
                                    <span>{s.dmg.toLocaleString()}</span>
                                    <span className="opacity-70">({(ratio * 100).toFixed(1)}%)</span>
                                  </div>
                                </div>
                              </TableCell>
                            </TableRow>
                            {hasChildren && isExpanded &&
                              group.detailRows.map((child, childIndex) => {
                                const childRatio = child.dmg / (details.totalDmg || 1);
                                return (
                                  <TableRow
                                    key={`${group.key}-${child.code}-${childIndex}`}
                                    className="border-b border-[var(--meter-soft-border)] bg-[var(--meter-table-row-alt)] text-[var(--meter-fg)] hover:bg-[var(--meter-row-hover)]">
                                    <TableCell className="py-1.5">
                                      <div className="relative flex items-center gap-2 overflow-hidden pl-8">
                                        <span className="absolute left-3 top-0 h-1/2 w-3 rounded-bl border-b border-l border-[var(--meter-accent)] opacity-70" />
                                        <SkillIcon
                                          code={child.code}
                                          size={22}
                                        />
                                        <span className={`${skillNameClass} text-sm opacity-90`}>
                                          {child.name}
                                        </span>
                                      </div>
                                    </TableCell>
                                    {[
                                      child.time,
                                      child.critPct === "-" ? "-" : `${child.critPct}%`,
                                      child.doublePct === "-" ? "-" : `${child.doublePct}%`,
                                      child.perfectPct === "-" ? "-" : `${child.perfectPct}%`,
                                      child.backPct === "-" ? "-" : `${child.backPct}%`,
                                      child.parryPct === "-" ? "-" : `${child.parryPct}%`,
                                    ].map((val, ci) => (
                                      <TableCell
                                        key={ci}
                                        className="py-1.5 text-center text-sm opacity-90">
                                        {val}
                                      </TableCell>
                                    ))}
                                    <TableCell className="py-1.5">
                                      <div className="relative h-7 rounded-md overflow-hidden">
                                        <div
                                          className="absolute inset-0 origin-left rounded-md"
                                          style={{
                                            background: "linear-gradient(to right, #55c42a, #3a9e20)",
                                            transform: `scaleX(${childRatio})`,
                                          }}
                                        />
                                        <div className={`relative z-10 flex h-full items-center justify-end gap-1.5 pr-2 text-sm ${damageTextClass}`}>
                                          <span>{child.dmg.toLocaleString()}</span>
                                          <span className="opacity-70">({(childRatio * 100).toFixed(1)}%)</span>
                                        </div>
                                      </div>
                                    </TableCell>
                                  </TableRow>
                                );
                              })}
                          </Fragment>
                        );
                      })}
                    </TableBody>
                  </Table>
                )}
              </div>
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      </div>
    </div>
  );
};
