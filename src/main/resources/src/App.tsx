import { useState, useCallback, useEffect } from "react";
import { useCombatController } from "./hooks/useCombatController";

import { MeterList } from "./components/MeterList";
import { DetailsPanel } from "./components/DetailsPanel";
import { useDragWindow } from "./hooks/useDragWindow";

import type { Player } from "./types";
import { SettingsPanel } from "./components/SettingsPanel.tsx";
import { DebugPanel } from "./components/DebugPanel.tsx";

export default function App() {
  const { players, targetName, reset, toggleCollapse } = useCombatController();
  const [settings, setSettings] = useState(false);

  const [selected, setSelected] = useState<Player | null>(null);
  useDragWindow(".drag-area");
  const handleSelect = useCallback(
    (id: string) => {
      const player = players.find((p) => p.id === id);
      if (player) setSelected(player);
    },
    [players],
  );
  useEffect(() => {
    (window as any).resetDpsUI = () => {
      reset();
    };
  }, []);

  return (
    <div className="w-full h-full bg-transparent flex items-start">
      <div className="w-100 h-75 bg-black text-white">
        <div className="drag-area cursor-move select-none flex justify-between items-center p-2 border-b border-gray-700 text-sm">
          <div>{targetName || "DPS METER"}</div>

          <div className="flex gap-2">
            <button onClick={reset}>Reset</button>
            <button onClick={() => setSettings(true)}>Settings</button>
            <button onClick={toggleCollapse}>Toggle</button>
          </div>
        </div>

        <div className="flex">
          <div className="w-1/2 p-2">
            <MeterList
              players={players}
              selectedId={selected?.id}
              onSelect={handleSelect}
            />
          </div>
          <SettingsPanel
            open={settings}
            onClose={() => setSettings(false)}
          />
          <div className="w-1/2 p-2">{selected && <DetailsPanel player={selected} />}</div>

          <DebugPanel></DebugPanel>
        </div>
      </div>
    </div>
  );
}
