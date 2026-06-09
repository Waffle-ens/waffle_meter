import { useSettingsStore } from "@/stores/useSettingsStore";
import { useEffect } from "react";
import { useShallow } from "zustand/react/shallow";

/**
 * small-window 방식 작은 창 오버레이(Phase 1).
 *
 * `smallWindowOverlay` 플래그가 켜지고 패널/토스트 등 확장 콘텐츠가 없을 때(=compact),
 * 네이티브 Stage 를 미터기 bbox 크기로 (uiX,uiY) 위치에 직접 잡는다(setWindowBounds).
 * 그 외(플래그 off, 또는 패널이 열린 expanded 상태)에는 기존 전체화면 경로(syncOverlayBounds)로 돌아간다.
 *
 * compact 일 때 미터기는 창 (0,0) 에 놓이므로(App 에서 left/top=0), 드래그는 네이티브 창 이동으로 처리한다.
 *
 * @param expanded 패널·토스트·다이얼로그 등 미터기 밖 콘텐츠가 보이는 상태(전체화면 폴백 필요)
 * @returns compact 여부 — App 이 미터기 root 위치(0 vs uiX)와 드래그 모드를 결정하는 데 사용
 */
export const useOverlayWindow = (expanded: boolean): boolean => {
  const { smallWindowOverlay, isLoaded } = useSettingsStore(
    useShallow((s) => ({
      smallWindowOverlay: s.smallWindowOverlay,
      isLoaded: s.isLoaded,
    })),
  );

  const compact = isLoaded && smallWindowOverlay && !expanded;

  useEffect(() => {
    if (!isLoaded) return;
    const jb = window.javaBridge;

    if (!compact) {
      // 전체화면 폴백: 플래그 off 또는 패널 열림. 기존 fitOverlayToScreen 경로.
      jb?.syncOverlayBounds?.();
      return;
    }

    const anchor = document.querySelector<HTMLElement>("[data-meter-root-anchor]");
    if (!anchor || !jb?.setWindowBounds) return;

    // 미터기 bbox(논리 px) 만큼 창을 잡는다. 위치는 화면 절대좌표 uiX/uiY.
    const apply = () => {
      const rect = anchor.getBoundingClientRect();
      const { uiX, uiY } = useSettingsStore.getState();
      jb.setWindowBounds!(uiX, uiY, Math.ceil(rect.width), Math.ceil(rect.height));
    };

    apply();
    // 콘텐츠 크기 변화(플레이어 수, 리사이즈, minimal 토글, 레이아웃)에 창 크기 추종.
    const observer = new ResizeObserver(apply);
    observer.observe(anchor);
    return () => observer.disconnect();
  }, [compact, isLoaded]);

  return compact;
};
