import { useHistory } from "@/hooks/useHistory";
import bossIcon from "@/assets/bossIcon.png";
import { useSettingsStore } from "@/stores/useSettingsStore";
import { useEffect } from "react";

interface Props {
  formatBattleTime: (ms: number) => string;
  onSelectHistory: (idx: number, report: any) => void;
}

// type UploadStatus = "idle" | "loading" | "success" | "error";

const formatDateTime = (ms: number) => {
  if (!ms) return "";
  const d = new Date(ms);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
};

export const HistoryPanel = ({ formatBattleTime, onSelectHistory }: Props) => {
  const { historyList, loading, fetchHistory } = useHistory();
  const theme = useSettingsStore((s) => s.theme);
  const overlayTheme = useSettingsStore((s) => s.overlayTheme);
  const isLightOverlay = overlayTheme === "light";
  // const [uploadStatus, setUploadStatus] = useState<Record<number, UploadStatus>>({});
  // const [uploadSlugs, setUploadSlugs] = useState<Record<number, string>>({});
  useEffect(() => {
    fetchHistory();
  }, []);
  // const isAnyUploading = Object.values(uploadStatus).some((s) => s === "loading");

  // const handleUpload = async (e: React.MouseEvent, idx: number) => {
  //   e.stopPropagation();
  //   if (isAnyUploading) return;

  //   setUploadStatus((prev) => ({ ...prev, [idx]: "loading" }));
  //   try {
  //     const slug: string | null = await window.javaBridge?.upload?.(idx);
  //     if (slug) {
  //       setUploadSlugs((prev) => ({ ...prev, [idx]: slug }));
  //       setUploadStatus((prev) => ({ ...prev, [idx]: "success" }));
  //     } else {
  //       setUploadStatus((prev) => ({ ...prev, [idx]: "error" }));
  //     }
  //   } catch {
  //     setUploadStatus((prev) => ({ ...prev, [idx]: "error" }));
  //   }
  // };

  return (
    <div className="flex h-full min-h-0 w-full flex-col overflow-hidden ">
      <div className="flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto overflow-x-hidden scrollbar-gutter:stable pt-1 pr-2">
        {loading && <div className="text-center opacity-40 py-8">불러오는 중</div>}
        {!loading && historyList.length === 0 && (
          <div className="flex-1 text-center opacity-40 py-8 flex items-center justify-center">
            전투 기록이 없습니다
          </div>
        )}
        {historyList.map((item) => {
          return (
            <div
              key={item.idx}
              className="flex items-center gap-2">
              <div
                onClick={() => onSelectHistory(item.idx, item.raw)}
                className="relative w-full cursor-pointer overflow-hidden rounded-lg border border-[var(--meter-soft-border)] bg-[var(--meter-row-bg)] px-3 transition-all duration-200 hover:brightness-110"
                style={{ minHeight: 52 }}>
                <div
                  className="absolute inset-0 origin-left"
                  style={{
                    background: `linear-gradient(to right, ${theme.bossBar[0]}, ${theme.bossBar[1]})`,
                    opacity: isLightOverlay ? (item.isBoss ? 0.68 : 0.18) : item.isBoss ? 0.8 : 0.2,
                  }}
                />

                <div
                  className="relative flex items-center gap-3"
                  style={{ minHeight: 52 }}>
                  <div
                    className="flex items-center justify-center shrink-0"
                    style={{ width: 32, height: 32 }}>
                    <img
                      src={bossIcon}
                      draggable={false}
                      className={`w-full h-full object-contain ${!item.isBoss ? "opacity-40" : ""}`}
                    />
                  </div>

                  <div className="flex h-full justify-between items-center gap-0.5 flex-1 min-w-0">
                    <div className="flex flex-col">
                      <span
                        className="font-bold text-shadow-meter truncate"
                        style={{ color: isLightOverlay ? "#111827" : "#ffffff" }}>
                        {item.mobName}
                      </span>
                      <span
                        className="text-xs font-normal"
                        style={{ color: isLightOverlay ? "rgba(30,41,59,0.64)" : "rgba(255,255,255,0.5)" }}>
                        {formatDateTime(item.battleStart)}
                      </span>
                    </div>

                    <div className="flex items-center gap-1.5 shrink-0 ml-2">
                      <span
                        className="font-bold text-shadow-meter"
                        style={{ color: isLightOverlay ? "#111827" : theme.bossRightValue }}>
                        {formatBattleTime(item.battleTime)}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
              {/* <div
                style={{ minHeight: 52 }}
                className=" rounded-lg w-16 flex items-center justify-center bg-black/30 cursor-pointer hover:brightness-125 transition-all duration-200">
                <div className="">
                  {status === "idle" && (
                    <div
                      className="flex flex-col justify-center items-center gap-1 opacity-60 hover:opacity-100 transition-opacity"
                      onClick={(e) => handleUpload(e, item.idx)}>
                      <Upload className="w-3.5 h-3.5" />
                      <p className=" text-xs font-normal ">업로드</p>
                    </div>
                  )}
                  {status === "loading" && (
                    <div className="flex flex-col justify-center items-center gap-1 opacity-50 ">
                      <Loader2 className="w-3.5 h-3.5 animate-spin " />
                      <p className=" text-xs font-normal ">대기중</p>
                    </div>
                  )}
                  {status === "success" && (
                    <div
                      className="flex flex-col justify-center items-center gap-1 opacity-80 hover:opacity-100 transition-opacity"
                      onClick={(e) => {
                        e.stopPropagation();
                        window.open(`http://example.com/${slug}`, "_blank");
                      }}>
                      <House className="text-success w-3.5 h-3.5" />
                      <p className="text-success text-xs font-normal ">완료</p>
                    </div>
                  )}
                  {status === "error" && (
                    <div className="flex flex-col justify-center items-center gap-1 opacity-80 hover:opacity-100 transition-opacity">
                      <TriangleAlert className="text-warning w-3.5 h-3.5" />
                      <p className="text-warning text-xs font-normal ">실패</p>
                    </div>
                  )}
                </div>
              </div> */}
            </div>
          );
        })}
      </div>
    </div>
  );
};
