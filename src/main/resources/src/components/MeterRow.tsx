import { memo } from "react";

interface Props {
  id: string;
  name: string;
  dps: number;
  contribution: number;
  isSelected: boolean;
  onSelect: (id: string) => void;
  topDps: number;
}

export const MeterRow = memo(
  ({ id, name, dps, contribution, isSelected, onSelect, topDps }: Props) => {
    const ratio = dps / topDps;

    let contribClass = "";
    if (contribution < 3) contribClass = "bg-red-900";
    else if (contribution < 5) contribClass = "bg-yellow-900";

    return (
      <div
        onClick={() => onSelect(id)}
        className={`relative p-2 cursor-pointer bg-black/70 ${contribClass} ${
          isSelected ? "ring-2 ring-purple-500" : ""
        }`}
      >
        <div
          className="absolute left-0 top-0 h-full bg-purple-500/40 origin-left"
          style={{ transform: `scaleX(${ratio})` }}
        />

        <div className="relative flex justify-between text-sm">
          <span>{name}</span>

          <span>
            {dps.toLocaleString()}/초 ({contribution.toFixed(1)}%)
          </span>
        </div>
      </div>
    );
  }
);
