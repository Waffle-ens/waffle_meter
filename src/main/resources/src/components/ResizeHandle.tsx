interface Props {
  onMouseDown: (e: React.MouseEvent) => void;
}
export const ResizeHandle = ({ onMouseDown }: Props) => {
  return (
    <div
      onMouseDown={onMouseDown}
      className="resizeHandle absolute bottom-2 right-2 w-5 h-5 cursor-se-resize opacity-40 hover:opacity-100 transition-all duration-200 z-10">
      <svg
        width="20"
        height="20"
        viewBox="0 0 20 20"
        fill="none">
        <circle
          cx="17"
          cy="17"
          r="2.5"
          fill="rgba(255,255,255,0.9)"
        />
        <circle
          cx="10"
          cy="17"
          r="2.5"
          fill="rgba(255,255,255,0.5)"
        />
        <circle
          cx="17"
          cy="10"
          r="2.5"
          fill="rgba(255,255,255,0.5)"
        />
      </svg>
    </div>
  );
};
