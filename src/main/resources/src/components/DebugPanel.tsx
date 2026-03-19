import { useDebugStore } from "../stores/debugStore";

export const DebugPanel = () => {
  const logs = useDebugStore((s) => s.logs);

  return (
    <div className="fixed bottom-0 left-0 w-full h-50 bg-black/90 text-green-400 text-xs overflow-auto p-2 ">
      {logs.map((log, i) => (
        <div key={i} className="whitespace-pre-wrap">
          {log}
        </div>
      ))}
    </div>
  );
};