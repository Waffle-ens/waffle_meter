import { memo } from "react";
import bossIcon from "../assets/bossIcon.png";

interface Props {
  targetName: string;
}

export const TargetInfo = memo(({ targetName }: Props) => {
  const displayName = targetName || "타겟 인식 실패";
  const isFailed = !targetName;

  return (
    <div className="relative w-full h-11 px-2 mt-4 my-2 rounded-sm overflow-hidden bg-black/30 ">
      <div
        className="absolute inset-0 origin-left"
        style={{
          background: "linear-gradient(to right, #6b0f1a, #5c1a24)",
          opacity: isFailed ? 0.2 : 0.8,
        }}
      />

      <div className="relative h-full flex items-center gap-3">
        <div className="w-8 h-7 flex items-center justify-center shrink-0">
          <img
            src={bossIcon}
            draggable={false}
            className={`w-full h-full object-contain ${isFailed ? "opacity-40" : ""}`}
          />
        </div>
        <span
          className="text-lg font-bold text-shadow-meter"
          style={{ color: isFailed ? "rgba(255,255,255,0.4)" : "#ffffff" }}>
          {displayName}
        </span>
        {!isFailed && (
          <div className="ml-auto font-bold text-shadow-meter">
            <span style={{ color: "#e63333" }}>76,521,443</span>
          </div>
        )}
      </div>
    </div>
  );
});
