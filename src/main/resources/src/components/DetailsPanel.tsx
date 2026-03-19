import { useEffect, useState } from "react";
import type { Player } from "../types";
import { useDetails, type Details } from "../hooks/useDetails";

export const DetailsPanel = ({ player }: { player: Player | null }) => {
  const { getDetails } = useDetails();
  const [details, setDetails] = useState<Details | null>(null);

  useEffect(() => {
    if (!player) return;

    getDetails(player).then(setDetails);
  }, [player]);

  if (!player) return null;

  return (
    <div className="p-3 bg-black/90 text-white text-sm">
      <div className="font-bold mb-2">{player.name} 상세내역</div>


      {details && (
        <div className="grid grid-cols-2 gap-1 mb-2">
          <div>총 피해: {details.totalDmg.toLocaleString()}</div>
          <div>기여도: {details.contributionPct.toFixed(1)}%</div>
          <div>치명: {details.totalCritPct}%</div>
          <div>백어택: {details.totalBackPct}%</div>
        </div>
      )}

      <div className="space-y-1">
        {details?.skills.map((s) => {
          const ratio = s.dmg / (details.totalDmg || 1);

          return (
            <div key={s.code} className="relative text-xs">
              <div
                className="absolute left-0 top-0 h-full bg-purple-500/30 origin-left"
                style={{ transform: `scaleX(${ratio})` }}
              />
              <div className="relative flex justify-between">
                <span>{s.name}</span>
                <span>
                  {s.dmg.toLocaleString()} (
                  {(ratio * 100).toFixed(1)}%)
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};