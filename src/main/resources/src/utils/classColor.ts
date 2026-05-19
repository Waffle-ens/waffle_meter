export const JOB_COLOR_MAP: Record<string, string> = {
  검성: "bg-cyan-700/45 border-none text-white",
  수호성: "bg-blue-700/45 border-none text-white",
  살성: "bg-lime-700/45 border-none text-white",
  궁성: "bg-green-700/45 border-none text-white",
  마도성: "bg-violet-700/45 border-none text-white",
  정령성: "bg-pink-700/45 border-none text-white",
  치유성: "bg-yellow-700/45 border-none text-white",
  호법성: "bg-orange-700/45 border-none text-white",
};

export const LIGHT_JOB_COLOR_MAP: Record<string, string> = {
  검성: "bg-cyan-500/32 border border-cyan-700/25 text-slate-950",
  수호성: "bg-blue-500/28 border border-blue-700/25 text-slate-950",
  살성: "bg-lime-600/28 border border-lime-800/25 text-slate-950",
  궁성: "bg-emerald-500/30 border border-emerald-700/25 text-slate-950",
  마도성: "bg-violet-500/28 border border-violet-700/25 text-slate-950",
  정령성: "bg-fuchsia-500/28 border border-fuchsia-700/25 text-slate-950",
  치유성: "bg-amber-500/32 border border-amber-700/25 text-slate-950",
  호법성: "bg-orange-500/30 border border-orange-700/25 text-slate-950",
};

export const getClassColor = (job?: string, tone: "dark" | "light" = "dark") => {
  if (tone === "light") {
    return LIGHT_JOB_COLOR_MAP[job ?? ""] ?? "bg-slate-400/24 border border-slate-600/20 text-slate-950";
  }

  return JOB_COLOR_MAP[job ?? ""] ?? "bg-white/10 border-white/10 text-white/80";
};
