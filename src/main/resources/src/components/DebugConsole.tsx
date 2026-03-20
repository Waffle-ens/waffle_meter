import { useDebugStore } from "../stores/debugStore";

export const DebugPanel = () => {
  const { logs } = useDebugStore();

  return (
    <div style={{
      position: "fixed",
      bottom: 0,
      left: 0,
      right: 0,
      background: "rgba(0,0,0,0.9)",
      color: "#00ff00",
      fontSize: "11px",
      fontFamily: "monospace",
      padding: "8px",
      maxHeight: "200px",
      overflowY: "auto",
      zIndex: 9999,
    }}>
      {logs.map((log, i) => (
        <div key={i}>{log}</div>
      ))}
    </div>
  );
};