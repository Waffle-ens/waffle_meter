import { useSettingsStore } from "@/stores/useSettingsStore";
import { isOverlayDragging, subscribeOverlayDragging } from "@/hooks/overlay/overlayDrag";
import { useEffect } from "react";

/**
 * 오버레이 창 모드.
 * - `fullscreen`: 전체화면 투명창(기존 fitOverlayToScreen). 플래그 off / 모달 / (Phase 2a) join·toast 열림.
 * - `meterOnly`: 미터기 bbox 작은 창(Phase 1). 드래그=네이티브 창 이동. 미터 root는 창 (0,0).
 * - `union`: 미터기+패널의 union bbox 작은 창(Phase 2). 콘텐츠는 화면좌표를 `--ovx/--ovy` 만큼 당겨 배치.
 */
export type OverlayMode = "fullscreen" | "meterOnly" | "union";

const setOriginVars = (x: number, y: number) => {
  const root = document.documentElement;
  root.style.setProperty("--ovx", `${x}px`);
  root.style.setProperty("--ovy", `${y}px`);
};

/**
 * 작은 창 오버레이의 네이티브 창 bounds + 콘텐츠 origin(`--ovx/--ovy`) 단일 소유자.
 *
 * `union` 모드는 **측정 기반**: 보이는 `[data-overlay-content]` 요소의 `getBoundingClientRect()`(창-상대)에
 * 현재 적용 origin 을 더해 화면좌표를 복원하고, 그 union 으로 새 origin/크기를 구해 `--ovx` 와
 * `setWindowBounds` 를 갱신한다. 화면좌표가 불변이면 결과도 불변(±1px 가드)이라 루프가 없다.
 */
export const useOverlayWindow = (mode: OverlayMode): void => {
  useEffect(() => {
    const jb = window.javaBridge;

    if (mode === "fullscreen") {
      setOriginVars(0, 0);
      jb?.syncOverlayBounds?.();
      return;
    }

    if (mode === "meterOnly") {
      // Phase 1 경로: 미터기 bbox = 창. 위치 = 화면좌표 uiX/uiY. 미터 root 는 창 (0,0)(App 에서 left:0).
      const anchor = document.querySelector<HTMLElement>("[data-meter-root-anchor]");
      if (!anchor || !jb?.setWindowBounds) return;
      const apply = () => {
        const rect = anchor.getBoundingClientRect();
        const { uiX, uiY } = useSettingsStore.getState();
        setOriginVars(uiX, uiY);
        jb.setWindowBounds!(uiX, uiY, Math.ceil(rect.width), Math.ceil(rect.height));
      };
      apply();
      const observer = new ResizeObserver(apply);
      observer.observe(anchor);
      const unsub = useSettingsStore.subscribe((s, prev) => {
        if (s.uiX !== prev.uiX || s.uiY !== prev.uiY) apply();
      });
      return () => {
        observer.disconnect();
        unsub();
      };
    }

    // union 모드.
    if (!jb?.setWindowBounds) return;
    // 진입 시점의 적용 origin = 현재 --ovx/--ovy (meterOnly 에서 왔으면 uiX/uiY, fullscreen 에서 왔으면 0).
    const cs = getComputedStyle(document.documentElement);
    const applied = {
      x: parseFloat(cs.getPropertyValue("--ovx")) || 0,
      y: parseFloat(cs.getPropertyValue("--ovy")) || 0,
      w: 0,
      h: 0,
    };

    let rafId: number | null = null;
    let shrinkTimer: ReturnType<typeof setTimeout> | null = null;

    const measure = () => {
      rafId = null;
      // 드래그 중에는 union 재적합을 멈춘다(전체화면 + origin 0 유지 → 비드래그 콘텐츠 떨림 방지).
      if (isOverlayDragging()) return;
      const els = Array.from(
        document.querySelectorAll<HTMLElement>("[data-overlay-content]"),
      );
      if (!els.length) return;

      let minX = Infinity;
      let minY = Infinity;
      let maxX = -Infinity;
      let maxY = -Infinity;
      for (const el of els) {
        if (el.getClientRects().length === 0) continue; // display:none 등 미렌더
        const r = el.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        // 창-상대 rect + 현재 적용 origin → 화면좌표 복원.
        minX = Math.min(minX, r.left + applied.x);
        minY = Math.min(minY, r.top + applied.y);
        maxX = Math.max(maxX, r.right + applied.x);
        maxY = Math.max(maxY, r.bottom + applied.y);
      }
      if (!Number.isFinite(minX)) return;

      const nx = Math.round(minX);
      const ny = Math.round(minY);
      const w = Math.ceil(maxX - minX);
      const h = Math.ceil(maxY - minY);
      // 멱등 가드(±1px): 화면좌표 불변이면 재적용 안 함 → MutationObserver 자기루프 방지.
      if (
        Math.abs(nx - applied.x) <= 1 &&
        Math.abs(ny - applied.y) <= 1 &&
        Math.abs(w - applied.w) <= 1 &&
        Math.abs(h - applied.h) <= 1
      ) {
        return;
      }
      applied.x = nx;
      applied.y = ny;
      applied.w = w;
      applied.h = h;
      setOriginVars(nx, ny);
      jb.setWindowBounds!(nx, ny, w, h);
    };

    const schedule = () => {
      // 새로 나타난 콘텐츠도 크기 추종하도록 매번 observe(중복 observe 는 무시됨).
      document
        .querySelectorAll<HTMLElement>("[data-overlay-content]")
        .forEach((el) => resizeObserver.observe(el));
      if (rafId === null) rafId = requestAnimationFrame(measure);
    };

    const resizeObserver = new ResizeObserver(schedule);
    const mutationObserver = new MutationObserver(schedule);
    const dragArea = document.querySelector(".drag-area");
    if (dragArea) {
      mutationObserver.observe(dragArea, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ["style", "class"],
      });
    }

    // origin 변경(전체화면↔union)은 네이티브 창 이동과 WebView --ovx 리페인트가 ~1프레임 어긋나 깜빡인다.
    // → 전체화면 전환은 rAF 타이밍(클린한 축소와 동일)으로, union 축소는 콘텐츠가 정착한 뒤 수행한다.
    const goFullscreen = () => {
      setOriginVars(0, 0);
      applied.x = 0;
      applied.y = 0;
      applied.w = 0;
      applied.h = 0;
      jb.syncOverlayBounds?.();
    };

    // 드래그 시작 → 전체화면(rAF). 드래그 끝 → union 즉시 축소(클린한 방향).
    const onDraggingChange = (d: boolean) => {
      if (shrinkTimer !== null) {
        clearTimeout(shrinkTimer);
        shrinkTimer = null;
      }
      if (d) {
        if (rafId !== null) cancelAnimationFrame(rafId);
        rafId = requestAnimationFrame(() => {
          rafId = null;
          if (isOverlayDragging()) goFullscreen();
        });
      } else {
        schedule();
      }
    };
    const unsubscribeDrag = subscribeOverlayDragging(onDraggingChange);

    if (isOverlayDragging()) {
      onDraggingChange(true);
    } else {
      // 진입: 먼저 전체화면(rAF)으로 펴서 패널이 미터기 좌/상단에 있어도 클립·origin 점프 깜빡임을 막고,
      // 패널이 페이드인되어 정착하면 union 작은 창으로 축소(축소는 콘텐츠가 안 움직여 깔끔).
      if (rafId !== null) cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(() => {
        rafId = null;
        goFullscreen();
        shrinkTimer = setTimeout(schedule, 220);
      });
    }

    return () => {
      if (rafId !== null) cancelAnimationFrame(rafId);
      if (shrinkTimer !== null) clearTimeout(shrinkTimer);
      resizeObserver.disconnect();
      mutationObserver.disconnect();
      unsubscribeDrag();
    };
  }, [mode]);
};
