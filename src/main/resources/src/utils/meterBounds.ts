const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max);

interface ClampMeterRootPositionOptions {
  rootEl?: HTMLElement | null;
  anchorEl?: HTMLElement | null;
  fallbackWidth?: number;
  fallbackHeight?: number;
  viewportWidth?: number;
  viewportHeight?: number;
}

export const clampMeterRootPosition = (
  x: number,
  y: number,
  {
    rootEl,
    anchorEl,
    fallbackWidth = 0,
    fallbackHeight = 0,
    viewportWidth = window.innerWidth,
    viewportHeight = window.innerHeight,
  }: ClampMeterRootPositionOptions,
) => {
  const rootRect = rootEl?.getBoundingClientRect();
  const anchorRect = anchorEl?.getBoundingClientRect();
  const offsetLeft = rootRect && anchorRect ? anchorRect.left - rootRect.left : 0;
  const offsetTop = rootRect && anchorRect ? anchorRect.top - rootRect.top : 0;
  const width = anchorRect?.width || fallbackWidth;
  const height = anchorRect?.height || fallbackHeight;

  const minX = -offsetLeft;
  const minY = -offsetTop;
  const maxX = Math.max(minX, viewportWidth - offsetLeft - width);
  const maxY = Math.max(minY, viewportHeight - offsetTop - height);

  return {
    x: clamp(x, minX, maxX),
    y: clamp(y, minY, maxY),
  };
};
