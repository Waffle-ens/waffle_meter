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

export const getClassColor = (job?: string) => {
  return JOB_COLOR_MAP[job ?? ""] ?? "bg-white/10 border-white/10 text-white/80";
};
