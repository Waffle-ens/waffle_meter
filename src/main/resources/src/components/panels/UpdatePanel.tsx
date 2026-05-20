import { useEffect, useRef, useState } from "react";
import type { UpdateInfo, DownloadState, CheckStatus } from "@/types";
import { Button } from "@/components/ui/button";

interface Props {
  updateInfo: UpdateInfo | null;
  downloadState: DownloadState;
  checkStatus: CheckStatus;
  currentVersion?: string;
  onClose: () => void;
  onUpdate: () => void;
  onRetryDownload: () => void;
  onReady?: () => void;
  onOpenReleasePage: () => void;
}

export const UPDATE_PANEL_DOT_CLS: Record<DownloadState["status"], string> = {
  idle: "bg-cyan-300 shadow-[0_0_6px_rgba(103,232,249,0.55)]",
  downloading: "bg-cyan-300 shadow-[0_0_6px_rgba(103,232,249,0.55)] animate-pulse",
  complete: "bg-green-400 shadow-[0_0_6px_rgba(74,222,128,0.55)]",
  error: "bg-red-400 shadow-[0_0_5px_rgba(248,113,113,0.5)]",
};

export const UPDATE_PANEL_HEADER_TITLE: Record<DownloadState["status"], string> = {
  idle: "업데이트 알림",
  downloading: "다운로드 중...",
  complete: "다운로드 완료",
  error: "설치 실패",
};

const CLS = {
  scrollBody: "flex flex-1 flex-col justify-center  px-4 pt-2",
  footerBar: "flex shrink-0 flex-row gap-2 border-t border-white/[0.07] px-4 pb-2 pt-3",
  footerBarCol: "flex shrink-0 flex-col gap-2 border-t border-white/[0.07] px-4 pb-2 pt-3",
  verRow: "text-sm flex justify-between items-center p-2 mb-1 text-sm",
  errRow: "text-sm flex justify-between items-center p-2 mb-1 text-sm",
  verLabel: "text-white/[0.5]",
  verCur: "text-slate-400 tabular-nums",
  verNew: "text-green-400 tabular-nums",
  dividerLine: "flex-1 h-px bg-white/[0.07]",
  dividerText: "text-[10.5px] font-semibold tracking-[0.07em] uppercase ",
} as const;

const VersionRows = ({ current, latest }: { current: string; latest: string }) => (
  <>
    <div className={CLS.verRow}>
      <span className={CLS.verLabel}>현재</span>
      <span className={CLS.verCur}>v{current}</span>
    </div>
    <div className={CLS.verRow}>
      <span className={CLS.verLabel}>최신</span>
      <span className={CLS.verNew}>v{latest}</span>
    </div>
  </>
);

export const UpdatePanel = ({
  updateInfo,
  downloadState,
  checkStatus,
  currentVersion,
  onClose,
  onUpdate,
  onOpenReleasePage,
  onRetryDownload,
}: Props) => {
  const [visible, setVisible] = useState(true);
  const prevStatus = useRef(downloadState.status);

  useEffect(() => {
    if (prevStatus.current === downloadState.status) return;
    prevStatus.current = downloadState.status;
    setVisible(false);
    const t = setTimeout(() => setVisible(true), 150);
    return () => {
      clearTimeout(t);
      setVisible(true);
    };
  }, [downloadState.status]);

  const { status } = downloadState;

  return (
    <div
      className="flex h-full min-h-0 w-full flex-col overflow-hidden px-2 pb-2 font-semibold transition-opacity duration-150"
      style={{ opacity: visible ? 1 : 0 }}>
      {status === "idle" && checkStatus === "checking" && (
        <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 px-4 py-10">
          <div className="h-4 w-4 animate-spin rounded-full border-2 border-cyan-300 border-t-transparent" />
          <p className="text-sm text-white/40">버전 확인 중...</p>
        </div>
      )}

      {status === "idle" && checkStatus === "upToDate" && (
        <>
          <div className={CLS.scrollBody}>
            <div className={CLS.verRow}>
              <span className={CLS.verLabel}>현재</span>
              <span className={CLS.verCur}>v{currentVersion ?? updateInfo?.currentVersion ?? "알 수 없음"}</span>
            </div>
            <div className={CLS.verRow}>
              <span className={CLS.verLabel}>상태</span>
              <span className="text-green-400">최신 버전입니다</span>
            </div>
          </div>
          <div className={CLS.footerBar}>
            <Button
              onClick={onClose}
              className="flex-1 p-4 w-20">
              닫기
            </Button>
          </div>
        </>
      )}

      {status === "idle" && checkStatus === "error" && (
        <>
          <div className={CLS.scrollBody}>
            <div className={CLS.verRow}>
              <span className={CLS.verLabel}>현재</span>
              <span className={CLS.verCur}>알 수 없음</span>
            </div>
            <div className={CLS.errRow}>
              <span className={CLS.verLabel}>오류</span>
              <span className="text-red-300">네트워크 또는 권한 문제</span>
            </div>
          </div>
          <div className={CLS.footerBar}>
            <Button
              onClick={onClose}
              className="flex-1 p-4 w-20 opacity-60 hover:opacity-100 transition-opacity">
              닫기
            </Button>
            <Button
              onClick={onOpenReleasePage}
              className="flex-1 transition-colors p-4 w-20">
              수동 설치
            </Button>
          </div>
        </>
      )}

      {status === "idle" && checkStatus === "updateAvailable" && updateInfo && (
        <>
          <div className={CLS.scrollBody}>
            <VersionRows
              current={updateInfo.currentVersion}
              latest={updateInfo.latestVersion}
            />
          </div>
          <div className={CLS.footerBar}>
            <Button
              onClick={onClose}
              size="lg"
              className="p-4 w-20 opacity-60 hover:opacity-100 transition-opacity flex-1">
              나중에
            </Button>
            <Button
              onClick={onUpdate}
              className="flex-1 bg-cyan-600 p-4 w-20 transition-colors hover:bg-cyan-500">
              업데이트
            </Button>
          </div>
        </>
      )}

      {status === "downloading" && (
        <>
          <div className={CLS.scrollBody}>
            {updateInfo && (
              <VersionRows
                current={updateInfo.currentVersion}
                latest={updateInfo.latestVersion}
              />
            )}
          </div>
          <div className={CLS.footerBarCol}>
            <div className="flex justify-between items-center text-sm">
              <p className="text-white/40">다운로드 중..</p>
              <span className="tabular-nums text-cyan-300">{downloadState.percent}%</span>
            </div>
            <div className="h-1.5 rounded-full bg-white/[0.07] overflow-hidden">
              <div
                className="h-full rounded-full bg-linear-to-r from-cyan-600 to-emerald-400
                           transition-[width] duration-300 ease-out"
                style={{ width: `${downloadState.percent}%` }}
              />
            </div>
          </div>
        </>
      )}

      {status === "complete" && (
        <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-1.25 px-6 py-10">
          <div
            className="w-9 h-9 rounded-full flex items-center justify-center mb-1
                          bg-green-400/10 border border-green-400/20 text-green-400 text-[15px]">
            ✓
          </div>
          <p className="text-sm text-slate-200">다운로드가 완료되었습니다</p>
          <p className="text-sm text-white/45">미터기를 종료한 뒤 재설치 해주세요.</p>
        </div>
      )}

      {status === "error" && (
        <>
          <div className={CLS.scrollBody}>
            {updateInfo && (
              <div className={CLS.verRow}>
                <span className={CLS.verLabel}>현재</span>
                <span className={CLS.verCur}>v{updateInfo.currentVersion}</span>
              </div>
            )}
            <div className={CLS.errRow}>
              <span className={CLS.verLabel}>오류</span>
              <span className="text-red-300">네트워크 또는 권한 문제</span>
            </div>
          </div>
          <div className={CLS.footerBar}>
            <Button
              onClick={onOpenReleasePage}
              size="lg"
              className="flex-1 p-4 w-20 opacity-60 hover:opacity-100 transition-opacity">
              수동 설치
            </Button>
            <Button
              onClick={onRetryDownload}
              className="flex-1 bg-destructive hover:bg-destructive/70 transition-colors p-4 w-20">
              다시 시도
            </Button>
          </div>
        </>
      )}
    </div>
  );
};
