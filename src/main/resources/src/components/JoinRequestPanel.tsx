import { useEffect, useState } from "react";
import { useJoinRequestStore } from "@/stores/useJoinRequestStore";
import { X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { getServerLabel } from "@/utils/parser";
import { getJobIconSrc } from "@/utils/icons";

const TOTAL_SEC = 20;

const TimerBar = ({ arrivedAt }: { arrivedAt: number }) => {
  const calc = () => {
    const elapsed = (Date.now() - arrivedAt) / 1000;
    return Math.max(0, TOTAL_SEC - elapsed);
  };

  const [remaining, setRemaining] = useState(calc);

  useEffect(() => {
    const interval = setInterval(() => {
      const r = calc();
      setRemaining(r);
      if (r <= 0) clearInterval(interval);
    }, 200);
    return () => clearInterval(interval);
  }, [arrivedAt]);

  const pct = (remaining / TOTAL_SEC) * 100;
  const color =
    pct > 50
      ? "linear-gradient(to right, #55c42a, #3a9e20)"
      : pct > 25
        ? "linear-gradient(to right, #e6a817, #c98c0f)"
        : "linear-gradient(to right, #e05252, #b83c3c)";

  return (
    <div className="flex items-center gap-2">
      <div className="relative flex-1 h-1.5 rounded bg-white/10 overflow-hidden">
        <div
          className="absolute inset-y-0 left-0 rounded transition-all duration-200"
          style={{ width: `${pct}%`, background: color }}
        />
      </div>
      <span className="text-xs tabular-nums opacity-60 w-6 text-right">
        {Math.ceil(remaining)}s
      </span>
    </div>
  );
};

export const JoinRequestPanel = () => {
  const { requests, isOpen, setOpen } = useJoinRequestStore();

  if (!isOpen) return null;

  return (
    <div
      className="mt-2 px-1 rounded-lg bg-[rgba(12,22,40,0.4)] border border-white/10 text-white font-bold"
      style={{ width: "100%" }}>
      <div className="flex items-center justify-between px-4 py-2.5 mb-2 border-b border-white/10">
        <span className="text-sm">파티 신청</span>
        <div className="flex items-center gap-3">
          <span className="text-xs opacity-50 ">{requests.length}건</span>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => {
              setOpen(false);
            }}>
            <X className="scale-110" />
          </Button>
        </div>
      </div>
      {requests.length === 0 ? (
        <div className="flex items-center justify-center py-6 opacity-40">
          <span className="text-sm">파티 신청이 없습니다</span>
        </div>
      ) : (
        <div
          className="overflow-y-auto scrollbar-gutter:stable"
          style={{ maxHeight: 260 }}>
          {[...requests].reverse().map((r) => (
            <div
              key={r.requester}
              className="px-4 py-2">
              <div className="flex items-center gap-1  ">
                <div className="flex items-center gap-2 flex-1">
                  <img
                    src={getJobIconSrc(r.job ?? undefined)}
                    className="w-6 h-6 object-contain shrink-0"
                    style={{ filter: "drop-shadow(0 0 3px rgba(20,20,20,0.6))" }}
                  />
                  <div className="flex flex-col">
                    <span className="text-sm text-shadow-meter">
                      {r.nickname}
                      {getServerLabel(r.server) ? `[${getServerLabel(r.server)}]` : ""}
                    </span>
                  </div>
                </div>
                <span className="text-shadow-meter text-sm tabular-nums text-[#41DCD1]">
                  {`${(r.power / 1000).toFixed(1)}k`}
                </span>
              </div>

              <TimerBar arrivedAt={r.arrivedAt} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
