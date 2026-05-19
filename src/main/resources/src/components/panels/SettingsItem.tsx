import { type PropsWithChildren, type ReactNode } from "react";
import { cn } from "@/lib/utils";

interface Props {
  title?: string;
  description?: string;
  className?: string;
  titleRight?: ReactNode;
}

export const SettingsItem = ({
  title,
  titleRight,
  children,
  className,
  description,
}: PropsWithChildren<Props>) => {
  return (
    <div className={cn("py-3", className)}>
      {title ? (
        <div className="flex flex-col gap-0.5 mb-2">
          <div className="flex items-center gap-2">
            <div className="text-sm">{title}</div>
            {titleRight ? <div className="text-xs opacity-35">{titleRight}</div> : null}
          </div>
          {description ? <div className="text-xs opacity-40">{description}</div> : null}
        </div>
      ) : null}

      <div className="flex flex-col gap-3">{children}</div>
    </div>
  );
};
