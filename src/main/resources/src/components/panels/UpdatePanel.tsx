import { useEffect, useRef, useState } from "react";
import type { UpdateInfo, DownloadState } from "@/types";
import { X } from "lucide-react";

interface Props {
  updateInfo: UpdateInfo;
  downloadState: DownloadState;
  onClose: () => void;
  onUpdate: () => void;
  onRetryDownload: () => void;
  onReady?: () => void;
}

// ── 상태별 상수 ──────────────────────────────────────────────────────────────

const DOT_CLS: Record<DownloadState["status"], string> = {
  idle: "bg-purple-400 shadow-[0_0_6px_rgba(167,139,250,0.55)]",
  downloading: "bg-purple-400 shadow-[0_0_6px_rgba(167,139,250,0.55)] animate-pulse",
  complete: "bg-green-400 shadow-[0_0_6px_rgba(74,222,128,0.55)]",
  error: "bg-red-400  shadow-[0_0_5px_rgba(248,113,113,0.5)]",
};

const HEADER_TITLE: Record<DownloadState["status"], string> = {
  idle: "업데이트 알림",
  downloading: "업데이트 중...",
  complete: "업데이트 완료",
  error: "설치 실패",
};

// ── 공통 className 조각 ───────────────────────────────────────────────────────

const CLS = {
  // 레이아웃
  body: "flex-1 px-4 pt-[14px] pb-0 flex flex-col",
  footer: "px-4 pt-[10px] pb-[14px] flex-shrink-0",

  // 버전 row
  verRow:
    "flex justify-between items-center px-[10px] py-[7px] mb-[5px]" +
    " rounded-[7px] bg-white/[0.03] text-[12px]",
  errRow:
    "flex justify-between items-center px-[10px] py-[7px] mb-[5px]" +
    " rounded-[7px] bg-red-400/[0.05] border border-red-400/[0.1] text-[12px]",
  verLabel: "text-white/[0.38]",
  verCur: "text-slate-400 font-medium tabular-nums",
  verNew: "text-green-400 font-semibold tabular-nums",

  // 구분선
  dividerLine: "flex-1 h-px bg-white/[0.07]",
  dividerText: "text-[10.5px] font-medium tracking-[0.07em] uppercase whitespace-nowrap",

  // 버튼
  btnLater:
    "flex-1 py-2 rounded-lg text-[12.5px] font-semibold cursor-pointer" +
    " bg-white/5 border border-white/[0.07] text-white/40" +
    " hover:text-white/70 hover:bg-white/[0.09] transition-all",
  btnUpdate:
    "flex-1 py-2 rounded-lg text-[12.5px] font-semibold cursor-pointer border-0" +
    " text-white bg-gradient-to-br from-violet-600 to-violet-700" +
    " shadow-[0_2px_10px_rgba(124,58,237,0.28)]" +
    " hover:shadow-[0_4px_16px_rgba(124,58,237,0.45)] hover:-translate-y-px transition-all",
  btnErr:
    "flex-1 py-2 rounded-lg text-[12.5px] font-semibold cursor-pointer" +
    " bg-red-400/[0.12] border border-red-400/25 text-red-300" +
    " hover:bg-red-400/[0.2] transition-all",
} as const;


const Divider = ({ label, labelCls }: { label: string; labelCls: string }) => (
  <div className="flex items-center gap-2 mb-3">
    <div className={CLS.dividerLine} />
    <span className={`${CLS.dividerText} ${labelCls}`}>{label}</span>
    <div className={CLS.dividerLine} />
  </div>
);

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
  onClose,
  onUpdate,
  onRetryDownload,
  onReady,
}: Props) => {
  const [visible, setVisible] = useState(true);
  const prevStatus = useRef(downloadState.status);

  useEffect(() => {
    onReady?.();
  }, []);

  useEffect(() => {
    if (prevStatus.current === downloadState.status) return;
    setVisible(false);
    const t = setTimeout(() => {
      prevStatus.current = downloadState.status;
      setVisible(true);
    }, 150);
    return () => clearTimeout(t);
  }, [downloadState.status]);

  const { status } = downloadState;
  const showClose = status !== "downloading" && status !== "complete";

  return (
    <div
      className="w-[300px] h-[240px] rounded-[14px] overflow-hidden
                 bg-[rgba(15,28,50,0.92)] border border-white/[0.08]
                 text-[#d7d7d7] flex flex-col transition-opacity duration-150"
      style={{ opacity: visible ? 1 : 0 }}>
      <div
        className="flex items-center gap-2.5 px-4 py-[13px]
                      border-b border-white/[0.07] flex-shrink-0">
        <div className={`w-[7px] h-[7px] rounded-full flex-shrink-0 ${DOT_CLS[status]}`} />
        <span className="flex-1 text-[12.5px] font-semibold tracking-wide text-slate-200">
          {HEADER_TITLE[status]}
        </span>
        {showClose && (
          <button
            onClick={onClose}
            className="w-[22px] h-[22px] rounded-md flex items-center justify-center
                       bg-white/5 hover:bg-white/10 text-[#777] hover:text-[#ccc]
                       border-0 cursor-pointer transition-all">
            <X size={12} />
          </button>
        )}
      </div>

      {status === "idle" && (
        <>
          <div className={CLS.body}>
            <Divider
              label={updateInfo.isPrerelease ? "베타 버전" : "정식 릴리즈"}
              labelCls={updateInfo.isPrerelease ? "text-yellow-400/45" : "text-white/[0.28]"}
            />
            <VersionRows
              current={updateInfo.currentVersion}
              latest={updateInfo.latestVersion}
            />
          </div>
          <div className={`${CLS.footer} flex gap-[7px]`}>
            <button
              onClick={onClose}
              className={CLS.btnLater}>
              나중에
            </button>
            <button
              onClick={onUpdate}
              className={CLS.btnUpdate}>
              업데이트
            </button>
          </div>
        </>
      )}

      {status === "downloading" && (
        <>
          <div className={CLS.body}>
            <Divider
              label="다운로드 중"
              labelCls="text-purple-400/60"
            />
            <VersionRows
              current={updateInfo.currentVersion}
              latest={updateInfo.latestVersion}
            />
          </div>
          <div className={`${CLS.footer} flex flex-col gap-[7px]`}>
            <div className="flex justify-between text-[11.5px]">
              <span className="text-white/[0.28]">설치 중에는 앱을 종료하지 마세요</span>
              <span className="text-purple-400 font-semibold tabular-nums">
                {downloadState.percent}%
              </span>
            </div>
            <div className="h-[5px] rounded-full bg-white/[0.07] overflow-hidden">
              <div
                className="h-full rounded-full bg-gradient-to-r from-violet-600 to-purple-400
                           transition-[width] duration-300 ease-out"
                style={{ width: `${downloadState.percent}%` }}
              />
            </div>
          </div>
        </>
      )}

      {/* ── complete ── */}
      {status === "complete" && (
        <div className="flex-1 flex flex-col items-center justify-center gap-[5px]">
          <div
            className="w-9 h-9 rounded-full flex items-center justify-center mb-[3px]
                          bg-green-400/10 border border-green-400/20 text-green-400 text-[15px]">
            ✓
          </div>
          <p className="text-[13px] font-semibold text-slate-200">다운로드가 완료되었습니다</p>
          <p className="text-[11px] text-white/[0.35]">잠시 후 앱이 자동으로 재실행됩니다</p>
        </div>
      )}

      {status === "error" && (
        <>
          <div className={CLS.body}>
            <Divider
              label="설치 오류"
              labelCls="text-red-400/50"
            />
            <div className={CLS.verRow}>
              <span className={CLS.verLabel}>현재</span>
              <span className={CLS.verCur}>v{updateInfo.currentVersion}</span>
            </div>
            <div className={CLS.errRow}>
              <span className={CLS.verLabel}>오류</span>
              <span className="text-red-300">네트워크 또는 권한 문제</span>
            </div>
          </div>
          <div className={`${CLS.footer} flex gap-[7px]`}>
            <button
              onClick={onClose}
              className={CLS.btnLater}>
              수동 설치
            </button>
            <button
              onClick={onRetryDownload}
              className={CLS.btnErr}>
              다시 시도
            </button>
          </div>
        </>
      )}
    </div>
  );
};
