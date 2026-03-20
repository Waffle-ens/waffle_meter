import { useEffect } from "react";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useHotkeyCapture } from "@/hooks/useHotkeyCapture";
import { formatHotkey, parseHotkeyString } from "@/utils/hotKey";
import { Button } from "@/components/ui/button";
import { X } from "lucide-react";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import type { DisplayMode, NameDisplay } from "@/stores/useSettingsStore";

interface Props {
  onClose: () => void;
  onReady?: () => void;
}

const DISPLAY_MODES: { value: DisplayMode; label: string; description: string }[] = [
  { value: "dps_percent", label: "DPS / 기여도", description: "45,000/초 (35.5%)" },
  {
    value: "amount_dps_percent",
    label: "누적 / DPS / 기여도",
    description: "1.2M 45,000/초 (35.5%)",
  },
  { value: "amount_percent", label: "누적 / 기여도", description: "1.2M (35.5%)" },
];
const NAME_DISPLAY_MODES: { value: NameDisplay; label: string; description: string }[] = [
  { value: "all", label: "모두 표기", description: "전체 표기" },
  { value: "me_only", label: "나만 표기", description: "나만 전체 표기, 나머지는 딜***" },
  { value: "hidden", label: "모두 숨김", description: "바*** 이*** 트***" },
];

export const SettingsPanel = ({ onClose, onReady }: Props) => {
  const { hotkey, setHotkey, displayMode, setDisplayMode, nameDisplay, setNameDisplay } =
    useSettingsStore();
  const { isCapturing, pending, start, stop, reset } = useHotkeyCapture(hotkey);

  useEffect(() => {
    const jb = (window as any).javaBridge;
    const raw = jb?.getHotkey?.() ?? jb?.getHotKey?.();
    const parsed = parseHotkeyString(raw);
    if (parsed) {
      setHotkey(parsed);
      reset(parsed);
    }
    onReady?.();
  }, []);

  const handleSave = () => {
    setHotkey(pending);
    (window as any).javaBridge?.updateHotkey?.(pending.modifiers, pending.vkCode);
    onClose();
  };

  return (
    <div className="font-bold rounded-lg py-4 px-7 w-[360px]">
      <div className="flex items-center pb-3 border-b border-white/10">
        <span>설정</span>
        <Button
          variant="ghost"
          className="ml-auto"
          onClick={onClose}>
          <X className="scale-125" />
        </Button>
      </div>

      {/* 핫키 */}
      <div className="py-4 border-b border-white/10">
        <div className="text-sm mb-2 opacity-80">핫키</div>
        <input
          readOnly
          onClick={() => {
            if (isCapturing) stop();
            else start();
          }}
          value={formatHotkey(pending.modifiers, pending.vkCode)}
          className={`w-full p-2 rounded-md bg-white/5 border text-sm cursor-pointer
          
            hover:border-white/10 transition-colors`}
        />
        {isCapturing && (
          <p className="text-xs text-purple-400 mt-1 opacity-80">
            키 조합을 입력하세요 (Ctrl / Alt + 키)
          </p>
        )}
      </div>

      {/* dps형식 */}
      <div className="py-4 border-b border-white/10 ">
        <div className="text-sm mb-3 opacity-80 ">표시 형식</div>
        <ToggleGroup
          type="single"
          value={displayMode}
          orientation="vertical"
          spacing={1}
          onValueChange={(value) => value && setDisplayMode(value as DisplayMode)}
          className="w-full">
          {DISPLAY_MODES.map(({ value, label, description }) => (
            <ToggleGroupItem
              key={value}
              value={value}
              className="w-full   
               data-[state=on]:border-purple-500
               data-[state=on]:bg-purple-500/40
               hover:bg-purple-300/10
               hover:text-gray-200
          transition-colors flex flex-col items-start gap-1 p-3 h-auto
        
       ">
              <span className=" font-bold">{label}</span>
              <span className="text-sm opacity-50 font-normal">{description}</span>
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </div>

      {/* 아이디 표기 */}
      <div className="py-4 border-b border-white/10">
        <div className="text-sm mb-3 opacity-80">아이디 표기</div>
        <ToggleGroup
          type="single"
          value={nameDisplay}
          spacing={4}
          onValueChange={(value) => value && setNameDisplay(value as NameDisplay)}
          className="w-full">
          {NAME_DISPLAY_MODES.map(({ value, label }) => (
            <ToggleGroupItem
              key={value}
              value={value}
              className="
          data-[state=on]:border-purple-500
          data-[state=on]:bg-purple-500/40
          hover:bg-purple-300/10
          hover:text-gray-200
          transition-colors  px-4">
              <span className="font-bold">{label}</span>
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </div>

      <div className="flex justify-end gap-2 pt-4">
        <Button
          onClick={onClose}
          size="lg"
          className=" p-4 w-20 opacity-60 hover:opacity-100 transition-opacity">
          취소
        </Button>
        <Button
          onClick={handleSave}
          className=" bg-purple-600 hover:bg-purple-700  transition-colors p-4 w-20">
          저장
        </Button>
      </div>
    </div>
  );
};
