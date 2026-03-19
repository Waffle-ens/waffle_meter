import { useEffect } from "react";
import { useSettingsStore } from "../stores/useSettingsStore";
import { useHotkeyCapture } from "../hooks/useHotkeyCapture";
import { formatHotkey, parseHotkeyString } from "../utils/hotKey";

export const SettingsPanel = ({ open, onClose }: any) => {
  const { hotkey, setHotkey } = useSettingsStore();
  const { isCapturing, pending, start, stop, reset } = useHotkeyCapture(hotkey);
  useEffect(() => {
    if (!open) return;

    const jb = (window as any).javaBridge;
    const raw = jb?.getHotkey?.() ?? jb?.getHotKey?.();
    const parsed = parseHotkeyString(raw);

    if (parsed) {
      setHotkey(parsed);
      reset(parsed);    
    }
  }, [open]);

  const handleSave = () => {
    setHotkey(pending);

    (window as any).javaBridge?.updateHotkey?.(pending.modifiers, pending.vkCode);

    onClose();
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center">
      <div className="bg-zinc-900 p-4 w-75 rounded">
        <div className="text-sm mb-2">Hotkey</div>

        <input
          readOnly
          onClick={() => {
            if (isCapturing) stop();
            else start();
          }}
          value={formatHotkey(pending.modifiers, pending.vkCode)}
          className={`w-full p-2 border ${isCapturing ? "border-purple-500" : "border-gray-700"}`}
        />

        <div className="flex justify-end gap-2 mt-4">
          <button onClick={onClose}>취소</button>
          <button onClick={handleSave}>저장</button>
        </div>
      </div>
    </div>
  );
};