import { useEffect, useRef, useState } from "react";
import type { Version, UpdateInfo, DownloadState } from "@/types";
// import { useDebugStore } from "../stores/debugStore";
const API = "https://api.github.com/repos/TK-open-public/Aion2-Dps-Meter/releases?per_page=10";
const RELEASE_URL = "https://github.com/TK-open-public/Aion2-Dps-Meter/releases";

const RETRY_INTERVAL = 800;
const RETRY_LIMIT = 5;

const parseVersion = (value: string): Version | null => {
  const raw = String(value || "")
    .trim()
    .replace(/^v/i, "");
  const match = raw.match(/^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$/);
  if (!match) return null;
  return {
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    pre: match[4] || null,
    raw,
  };
};

const compareVersion = (a: Version, b: Version): number => {
  if (a.major !== b.major) return a.major > b.major ? 1 : -1;
  if (a.minor !== b.minor) return a.minor > b.minor ? 1 : -1;
  if (a.patch !== b.patch) return a.patch > b.patch ? 1 : -1;
  if (!a.pre && !b.pre) return 0;
  if (!a.pre) return 1;
  if (!b.pre) return -1;
  return a.pre.localeCompare(b.pre);
};

const pickLatestRelease = (
  releases: any[],
  wantPrerelease: boolean
): { version: Version; msiUrl: string } | null => {
  return releases
    .filter((r) => !r.draft && !!r.prerelease === wantPrerelease)
    .reduce<{ version: Version; msiUrl: string } | null>((best, r) => {
      const v = parseVersion(r.tag_name);
      if (!v) return best;
      const msiAsset = r.assets?.find((a: any) => a.name?.endsWith(".msi"));
      if (!msiAsset) return best;
      if (!best || compareVersion(v, best.version) > 0)
        return { version: v, msiUrl: msiAsset.browser_download_url };
      return best;
    }, null);
};

export const useVersionCheck = () => {
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const [downloadState, setDownloadState] = useState<DownloadState>({ status: "idle" });
  const retryCountRef = useRef(0);

  // 버전 체크
  useEffect(() => {
    const check = async () => {
      const version = window.javaBridge?.getVersion?.();
      const current = parseVersion(version ?? "");

      if (!current || !(window as any).javaBridge) {
        if (retryCountRef.current < RETRY_LIMIT) {
          retryCountRef.current++;
          setTimeout(check, RETRY_INTERVAL);
        }
        return;
      }

      try {
        const res = await fetch(API, {
          headers: { Accept: "application/vnd.github+json" },
          cache: "no-store",
        });
        if (!res.ok) return;

        const releases = await res.json();
        const latestStable = pickLatestRelease(releases, false);
        const latestBeta = pickLatestRelease(releases, true);

        let target: { version: Version; msiUrl: string } | null = null;
        let isPrerelease = false;

        if (latestStable && compareVersion(latestStable.version, current) > 0) {
          target = latestStable;
          isPrerelease = false;
        } else if (
          current.pre &&
          latestBeta &&
          compareVersion(latestBeta.version, current) > 0
        ) {
          target = latestBeta;
          isPrerelease = true;
        }

        if (target) {
          setUpdateInfo({
            currentVersion: current.raw,
            latestVersion: target.version.raw,
            isPrerelease,
            msiUrl: target.msiUrl,
          });
        }
      } catch (e) {}
    };

    check();
  }, []);

  useEffect(() => {
    (window as any).onDownloadProgress = (percent: number) => {
      setDownloadState({ status: "downloading", percent });
    };
    (window as any).onDownloadComplete = () => {
      setDownloadState({ status: "complete" });
    };
    (window as any).onDownloadError = () => {
      setDownloadState({ status: "error" });
    };

    return () => {
      delete (window as any).onDownloadProgress;
      delete (window as any).onDownloadComplete;
      delete (window as any).onDownloadError;
    };
  }, []);

  const startUpdate = (msiUrl: string) => {
    setDownloadState({ status: "downloading", percent: 0 });
    (window as any).javaBridge.startUpdate(msiUrl);
  };

  const retryDownload = () => {
    if (!updateInfo) return;
    startUpdate(updateInfo.msiUrl);
  };

  // 기존 수동 설치 fallback
  const openReleasePage = () => {
    (window as any).javaBridge.openBrowser(RELEASE_URL);
    (window as any).javaBridge.exitApp();
  };

  return {
    updateInfo,
    downloadState,
    startUpdate,
    retryDownload,
    openReleasePage,
  };
};
