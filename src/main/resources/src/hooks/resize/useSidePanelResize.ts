import { useMemo } from "react";
import { useShallow } from "zustand/react/shallow";
import type { PanelType } from "@/types";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useResizableBase } from "./useResizableBase";

const jb = () => (window as any).javaBridge;

const FALLBACK_DIMS = { width: 400, height: 400 };

type ResizablePanelType = Exclude<PanelType, "update" | null>;
const AXES: Record<
  ResizablePanelType,
  {
    key:
      | "detailWidth"
      | "detailHeight"
      | "settingsPanelWidth"
      | "settingsPanelHeight"
      | "historyPanelWidth"
      | "historyPanelHeight"
      | "updatePanelWidth"
      | "updatePanelHeight";
    dir: "x" | "y";
    min: number;
    max: number;
  }[]
> = {
  details: [
    { key: "detailHeight", dir: "y", min: 300, max: 1070 },
    { key: "detailWidth", dir: "x", min: 480, max: 1600 },
  ],
  settings: [
    { key: "settingsPanelHeight", dir: "y", min: 300, max: 920 },
    { key: "settingsPanelWidth", dir: "x", min: 280, max: 720 },
  ],
  history: [
    { key: "historyPanelHeight", dir: "y", min: 130, max: 860 },
    { key: "historyPanelWidth", dir: "x", min: 360, max: 560 },
  ],
};

function persistSidePanelSize(
  panelType: PanelType,
  state: ReturnType<typeof useSettingsStore.getState>,
) {
  const j = jb();
  if (!j?.saveProps || !panelType) return;
  switch (panelType) {
    case "details":
      j.saveProps("detailHeight", String(state.detailHeight));
      j.saveProps("detailWidth", String(state.detailWidth));
      break;
    case "settings":
      j.saveProps("settingsPanelHeight", String(state.settingsPanelHeight));
      j.saveProps("settingsPanelWidth", String(state.settingsPanelWidth));
      break;
    case "history":
      j.saveProps("historyPanelHeight", String(state.historyPanelHeight));
      j.saveProps("historyPanelWidth", String(state.historyPanelWidth));
      break;

    default:
      break;
  }
}

export function useSidePanelResize(panelType: PanelType) {
  const dims = useSettingsStore(
    useShallow((s) => ({
      detailWidth: s.detailWidth,
      detailHeight: s.detailHeight,
      settingsPanelWidth: s.settingsPanelWidth,
      settingsPanelHeight: s.settingsPanelHeight,
      historyPanelWidth: s.historyPanelWidth,
      historyPanelHeight: s.historyPanelHeight,
      updatePanelWidth: s.updatePanelWidth,
      updatePanelHeight: s.updatePanelHeight,
    })),
  );
  const isResizable = panelType && panelType !== "update";

  const axes = useMemo(
    () => (isResizable ? AXES[panelType as ResizablePanelType] : []),
    [panelType],
  );
  const { onMouseDown: onMouseDownCorner } = useResizableBase({
    axes,
    onSave: (state) => persistSidePanelSize(panelType, state),
  });

  const { panelWidth, panelHeight } = useMemo(() => {
    switch (panelType) {
      case "details":
        return { panelWidth: dims.detailWidth, panelHeight: dims.detailHeight };
      case "settings":
        return { panelWidth: dims.settingsPanelWidth, panelHeight: dims.settingsPanelHeight };
      case "history":
        return { panelWidth: dims.historyPanelWidth, panelHeight: dims.historyPanelHeight };
      case "update":
        return { panelWidth: 300, panelHeight: 160 };
      default:
        return { panelWidth: FALLBACK_DIMS.width, panelHeight: FALLBACK_DIMS.height };
    }
  }, [panelType, dims]);

  return { panelWidth, panelHeight, onMouseDownCorner };
}
