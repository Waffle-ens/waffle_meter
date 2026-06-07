import type { DownloadState, UpdateInfo } from "@/types";
import { Button } from "@/components/ui/button";
import { Bell, CheckCircle2, Download, X } from "lucide-react";

interface Props {
  updateInfo: UpdateInfo | null;
  downloadState: DownloadState;
  onUpdate: () => void;
  onOpenPanel: () => void;
  onDismiss: () => void;
}

export const UpdateToast = ({
  updateInfo,
  downloadState,
  onUpdate,
  onOpenPanel,
  onDismiss,
}: Props) => {
  if (!updateInfo && downloadState.status === "idle") return null;

  const isDownloading = downloadState.status === "downloading";
  const isComplete = downloadState.status === "complete";
  const isError = downloadState.status === "error";

  const title = isComplete
    ? "다운로드 완료"
    : isError
      ? "다운로드 실패"
      : updateInfo
        ? `v${updateInfo.latestVersion} 업데이트`
        : "업데이트 알림";
  const description = isComplete
    ? "미터기를 종료한 뒤 재설치 해주세요."
    : isError
      ? "네트워크 또는 권한 문제를 확인해 주세요."
      : updateInfo
        ? `현재 v${updateInfo.currentVersion}`
        : "업데이트 상태를 확인해 주세요.";

  return (
    <div
      className="fixed bottom-4 right-4 z-50 w-[320px] max-w-[calc(100vw-32px)] overflow-hidden rounded-lg border border-[var(--meter-border)] bg-[var(--panel-bg)] text-[var(--meter-fg)] shadow-[0_18px_44px_rgba(0,0,0,0.36)] backdrop-blur-md"
      data-no-drag>
      <div className="flex items-start gap-3 px-3.5 py-3">
        <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-md border border-[var(--meter-control-border)] bg-[var(--meter-control-bg)] text-cyan-300">
          {isComplete ? (
            <CheckCircle2 className="size-4.5 text-emerald-300" />
          ) : (
            <Bell className="size-4.5" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <p className="min-w-0 flex-1 truncate text-sm font-bold">{title}</p>
            <Button
              variant="ghost"
              size="icon-xs"
              title="닫기"
              aria-label="업데이트 알림 닫기"
              className="h-6 w-6 opacity-60 hover:opacity-100"
              onClick={onDismiss}>
              <X className="size-3.5" />
            </Button>
          </div>
          <p className="mt-0.5 truncate text-xs font-semibold text-[var(--meter-muted)]">
            {description}
          </p>
          {isDownloading && (
            <div className="mt-3">
              <div className="mb-1 flex justify-between text-[11px] font-semibold text-[var(--meter-muted)]">
                <span>다운로드 중</span>
                <span className="tabular-nums text-cyan-300">{downloadState.percent}%</span>
              </div>
              <div className="h-1.5 overflow-hidden rounded-full bg-white/10">
                <div
                  className="h-full rounded-full bg-linear-to-r from-cyan-500 to-emerald-400 transition-[width] duration-300"
                  style={{ width: `${downloadState.percent}%` }}
                />
              </div>
            </div>
          )}
          {!isDownloading && !isComplete && (
            <div className="mt-3 flex justify-end gap-2">
              <Button
                variant="ghost"
                size="sm"
                onClick={onOpenPanel}
                className="h-7 px-2 text-xs opacity-70 hover:opacity-100">
                상세
              </Button>
              <Button
                size="sm"
                onClick={onUpdate}
                className="h-7 bg-cyan-600 px-2 text-xs hover:bg-cyan-500">
                <Download className="size-3.5" />
                업데이트
              </Button>
            </div>
          )}
          {isComplete && (
            <div className="mt-3 flex justify-end">
              <Button
                variant="ghost"
                size="sm"
                onClick={onOpenPanel}
                className="h-7 px-2 text-xs opacity-80 hover:opacity-100">
                자세히 보기
              </Button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
