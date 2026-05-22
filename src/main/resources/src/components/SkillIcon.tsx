import { useState } from "react";
import { getSkillIconSrc } from "@/utils/icons";
import { CircleDot, Package, Sparkles } from "lucide-react";

interface Props {
  code?: string | number;
  size?: number;
}

export const SkillIcon = ({ code, size = 24 }: Props) => {
  const [failedSrc, setFailedSrc] = useState<string | undefined>(undefined);
  const src = getSkillIconSrc(code);
  const failed = src !== undefined && failedSrc === src;
  const num = typeof code === "string" ? parseInt(code, 10) : code;
  const FallbackIcon =
    typeof num === "number" && num >= 20_000_000 && num < 30_000_000
      ? Package
      : typeof num === "number" && num >= 110_000_000
        ? CircleDot
        : Sparkles;

  if (!src || failed) {
    return (
      <div
        className="shrink-0 rounded-md border border-[var(--meter-soft-border)] bg-[var(--meter-stat-bg)] flex items-center justify-center text-[var(--meter-muted)]"
        style={{ width: size, height: size }}>
        <FallbackIcon
          style={{
            width: Math.max(12, Math.round(size * 0.58)),
            height: Math.max(12, Math.round(size * 0.58)),
          }}
          strokeWidth={2.2}
        />
      </div>
    );
  }

  return (
    <img
      src={src}
      alt=""
      loading="lazy"
      decoding="async"
      style={{ width: size, height: size }}
      className="shrink-0 rounded-md object-contain"
      onError={() => setFailedSrc(src)}
    />
  );
};
