import { Button } from "@/components/ui/button";
import type { PanelType } from "@/types";
import { memo, useRef } from "react";
import {
  Settings,
  //  RefreshCcw,
  Power,
  ClipboardClock,
  Bug,
  UserRoundPlus,
  Grip,
  Moon,
  PanelBottom,
  PanelTop,
  Sun,
} from "lucide-react";
// import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { useJoinRequestStore } from "@/stores/useJoinRequestStore";

interface Props {
  // isCollapse: boolean;
  // reset: () => void;
  setSettings: (value: PanelType) => void;
  // toggleCollapse: () => void;
  className: string;
}
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useMoveWindow } from "@/hooks/drag/useMoveWindow";

export const Header = memo(
  ({
    className,
    //  reset,
    setSettings,
  }: Props) => {
    const isDebugMode = useSettingsStore((s) => s.isDebugMode);
    const overlayTheme = useSettingsStore((s) => s.overlayTheme);
    const overlayLayout = useSettingsStore((s) => s.overlayLayout);
    const toggleOverlayTheme = useSettingsStore((s) => s.toggleOverlayTheme);
    const toggleOverlayLayout = useSettingsStore((s) => s.toggleOverlayLayout);
    const requestCount = useJoinRequestStore((s) => s.requests.length);
    const isOpen = useJoinRequestStore((s) => s.isOpen);
    const setOpen = useJoinRequestStore((s) => s.setOpen);
    const exitApp = () => {
      window.javaBridge?.exitApp?.();
    };
    const dragRef = useRef<HTMLDivElement>(null);
    useMoveWindow(dragRef); // selector 대신 ref로 변경

    const toggleDebugConsole = () => {
      window.dispatchEvent(new CustomEvent("toggle-debug-console"));
    };

    const iconButtonClass =
      "meter-control h-7 w-7 rounded-md border transition-all";
    const isLightMode = overlayTheme === "light";
    const isBottomLayout = overlayLayout === "bottom";

    return (
      <div className="flex items-center justify-between gap-3">
        <div className={`flex min-w-0 items-center gap-2 ${className}`}>
          <div
            ref={dragRef}
            className="meter-control window-drag-handle flex h-7 w-7 cursor-grab items-center justify-center rounded-md border transition-all active:cursor-grabbing">
            <Grip className="size-4" />
          </div>
        </div>

        <div className={`${className} flex shrink-0 items-center gap-1`}>
          {/* <Tooltip>
            <TooltipTrigger asChild> */}
          <Button
            variant="ghost"
            title={isLightMode ? "다크 모드" : "화이트 모드"}
            aria-label={isLightMode ? "다크 모드로 변경" : "화이트 모드로 변경"}
            onClick={toggleOverlayTheme}
            size="icon"
            className={iconButtonClass}>
            {isLightMode ? <Moon className="size-4.5" /> : <Sun className="size-4.5" />}
          </Button>

          <Button
            variant="ghost"
            title={isBottomLayout ? "기본 배치" : "하단 배치"}
            aria-label={isBottomLayout ? "기본 배치로 변경" : "하단 배치로 변경"}
            onClick={toggleOverlayLayout}
            size="icon"
            className={iconButtonClass}>
            {isBottomLayout ? (
              <PanelTop className="size-4.5" />
            ) : (
              <PanelBottom className="size-4.5" />
            )}
          </Button>

          <Button
            variant="ghost"
            onClick={exitApp}
            size="icon"
            className={iconButtonClass}>
            <Power className="size-4.5" />
          </Button>
          {/* </TooltipTrigger> */}
          {/* <TooltipContent>종료</TooltipContent> */}
          {/* </Tooltip> */}

          {/* <Tooltip>
            <TooltipTrigger asChild> */}
          {/* <Button
            variant="ghost"
            onClick={reset}
            className="rounded-full">
            <RefreshCcw className="size-4.5" />
          </Button> */}
          {/* </TooltipTrigger>
            <TooltipContent>새로고침</TooltipContent>
          </Tooltip> */}
          <Button
            variant="ghost"
            onClick={() => setOpen(!isOpen)}
            size="icon"
            className={`${iconButtonClass} relative`}>
            <UserRoundPlus className="size-4.5" />
            {requestCount > 0 && (
              <span
                className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full border border-black/40 bg-red-500 text-[10px] font-bold text-white shadow-sm">
                {requestCount}
              </span>
            )}
          </Button>

          {/* <Tooltip>
            <TooltipTrigger asChild> */}
          <Button
            size="icon"
            variant="ghost"
            onClick={() => setSettings("settings")}
            className={iconButtonClass}>
            <Settings className="size-4.5" />
          </Button>
          {/* </TooltipTrigger>
            <TooltipContent>설정</TooltipContent>
          </Tooltip> */}

          {/* <Tooltip>
            <TooltipTrigger asChild> */}
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setSettings("history")}
            className={iconButtonClass}>
            <ClipboardClock className="size-4.5" />
          </Button>
          {/* </TooltipTrigger>
            <TooltipContent>전투 기록</TooltipContent>
          </Tooltip> */}

          {isDebugMode && (
            // <Tooltip>
            //   <TooltipTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              onClick={toggleDebugConsole}
              className={iconButtonClass}>
              <Bug className="size-4.5" />
            </Button>
            //   </TooltipTrigger>
            //   <TooltipContent>디버그 콘솔</TooltipContent>
            // </Tooltip>
          )}
        </div>
      </div>
    );
  },
);
