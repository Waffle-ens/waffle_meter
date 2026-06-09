// 작은 창(union) 드래그 중 신호.
//
// union 모드에서 콘텐츠를 옮기면 창 origin(--ovx)이 매 프레임 바뀌는데, 네이티브 창 이동과
// CSS origin 변경이 ~1프레임 어긋나 드래그 안 하는 콘텐츠(미터기 등)가 덜덜 떨린다.
// → 드래그 중에는 전체화면 + origin 0 으로 고정(절대 화면좌표 배치, origin 변동 없음)했다가
//   드래그 끝나면 union 작은 창으로 다시 맞춘다. useOverlayWindow 가 이 신호를 구독한다.

type Listener = (dragging: boolean) => void;

let dragging = false;
const listeners = new Set<Listener>();

export const setOverlayDragging = (value: boolean) => {
  if (dragging === value) return;
  dragging = value;
  listeners.forEach((l) => l(value));
};

export const isOverlayDragging = () => dragging;

export const subscribeOverlayDragging = (listener: Listener) => {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
};
