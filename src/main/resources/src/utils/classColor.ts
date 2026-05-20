export const JOB_COLOR_MAP: Record<string, string> = {
  검성: "bg-slate-950/72 border border-cyan-300/22 text-cyan-50 shadow-[inset_4px_0_0_rgba(34,211,238,0.78)]",
  수호성: "bg-slate-950/72 border border-blue-300/22 text-blue-50 shadow-[inset_4px_0_0_rgba(96,165,250,0.78)]",
  살성: "bg-slate-950/72 border border-lime-300/22 text-lime-50 shadow-[inset_4px_0_0_rgba(132,204,22,0.78)]",
  궁성: "bg-slate-950/72 border border-emerald-300/22 text-emerald-50 shadow-[inset_4px_0_0_rgba(52,211,153,0.78)]",
  마도성: "bg-slate-950/72 border border-violet-300/22 text-violet-50 shadow-[inset_4px_0_0_rgba(167,139,250,0.78)]",
  정령성: "bg-slate-950/72 border border-fuchsia-300/22 text-fuchsia-50 shadow-[inset_4px_0_0_rgba(217,70,239,0.78)]",
  치유성: "bg-slate-950/72 border border-amber-300/24 text-amber-50 shadow-[inset_4px_0_0_rgba(245,158,11,0.78)]",
  호법성: "bg-slate-950/72 border border-orange-300/24 text-orange-50 shadow-[inset_4px_0_0_rgba(249,115,22,0.78)]",
};

export const LIGHT_JOB_COLOR_MAP: Record<string, string> = {
  검성: "bg-white/78 border border-cyan-700/24 text-slate-950 shadow-[inset_4px_0_0_rgba(8,145,178,0.58)]",
  수호성: "bg-white/78 border border-blue-700/24 text-slate-950 shadow-[inset_4px_0_0_rgba(37,99,235,0.58)]",
  살성: "bg-white/78 border border-lime-800/24 text-slate-950 shadow-[inset_4px_0_0_rgba(77,124,15,0.58)]",
  궁성: "bg-white/78 border border-emerald-700/24 text-slate-950 shadow-[inset_4px_0_0_rgba(5,150,105,0.58)]",
  마도성: "bg-white/78 border border-violet-700/24 text-slate-950 shadow-[inset_4px_0_0_rgba(109,40,217,0.58)]",
  정령성: "bg-white/78 border border-fuchsia-700/24 text-slate-950 shadow-[inset_4px_0_0_rgba(162,28,175,0.58)]",
  치유성: "bg-white/78 border border-amber-700/26 text-slate-950 shadow-[inset_4px_0_0_rgba(180,83,9,0.58)]",
  호법성: "bg-white/78 border border-orange-700/26 text-slate-950 shadow-[inset_4px_0_0_rgba(194,65,12,0.58)]",
};

export const getClassColor = (job?: string, tone: "dark" | "light" = "dark") => {
  if (tone === "light") {
    return LIGHT_JOB_COLOR_MAP[job ?? ""] ?? "bg-slate-400/24 border border-slate-600/20 text-slate-950";
  }

  return JOB_COLOR_MAP[job ?? ""] ?? "bg-slate-950/70 border border-white/12 text-white/80";
};
