import { type PropsWithChildren } from "react";

interface Props {
  title?: string;
  description?: string;
  className?: string;
}

export const SettingsItem = ({ title, children, className }: PropsWithChildren<Props>) => {
  return (
    <div className={` border-white/10 ${className}`}>
      <div className="text-sm mb-2 opacity-80">{title}</div>

      {children}
    </div>
  );
};
