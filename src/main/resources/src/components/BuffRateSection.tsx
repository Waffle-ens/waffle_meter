"use client";

import {
  Accordion,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "@/components/ui/accordion";
import { BuffRateBar } from "@/components/BuffRateBar";
import { useBuffInfo } from "@/hooks/useBuffInfo";

interface Props {
  buffOperatingRate: Record<string, number> | null | undefined;
  columns?: number;
}

export const BuffRateSection = ({ buffOperatingRate, columns = 1 }: Props) => {
  const buffMap = useBuffInfo();

  const entries = buffOperatingRate ? Object.entries(buffOperatingRate) : [];
  if (entries.length === 0) return null;
  const gridClass =
    columns >= 4
      ? "grid-cols-4"
      : columns >= 3
        ? "grid-cols-3"
        : columns >= 2
          ? "grid-cols-2"
          : "grid-cols-1";
  return (
    <div className="mb-3 border border-white/10 rounded-lg overflow-hidden">
      <Accordion
        type="single"
        collapsible>
        <AccordionItem value="buff">
          <AccordionTrigger className="px-4 py-2.5 bg-black/20 cursor-pointer  text-sm">
            <div className="flex w-full items-center justify-between pr-2">
              <span>버프 가동률</span>
              <span className="text-xs opacity-60">{entries.length}개</span>
            </div>
          </AccordionTrigger>

          <AccordionContent key={columns}>
            <div
              className={`px-4 py-2 grid ${gridClass} 
              overflow-y-auto
              gap-x-4 gap-y-0.5 `}>
              {entries
                .sort((a, b) => b[1] - a[1])
                .map(([id, rate]) => (
                  <BuffRateBar
                    key={id}
                    id={id}
                    rate={rate}
                    info={buffMap[id]}
                  />
                ))}
            </div>
          </AccordionContent>
        </AccordionItem>
      </Accordion>
    </div>
  );
};
