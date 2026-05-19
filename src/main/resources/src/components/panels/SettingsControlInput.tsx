import type { InputHTMLAttributes } from "react";

import { cn } from "@/lib/utils";

type Props = InputHTMLAttributes<HTMLInputElement>;

export function SettingsControlInput({ className, ...props }: Props) {
  return (
    <input
      {...props}
      className={cn(
        "w-full rounded-md  bg-white/5 border border-white/10 px-2.5 py-2 text-xs outline-none transition-colors",
        "focus:border-cyan-400/60 focus:ring-3 focus:ring-cyan-400/20",
        "placeholder:text-white/30",
        className,
      )}
    />
  );
}

