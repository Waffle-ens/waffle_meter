import { useEffect, useRef, useState } from "react";
// import { useDebugStore } from "../stores/debugStore";

const API = "https://api.github.com/repos/TK-open-public/Aion2-Dps-Meter/releases?per_page=10";
const RELEASE_URL = "https://github.com/TK-open-public/Aion2-Dps-Meter/releases";

const RETRY_INTERVAL = 800;
const RETRY_LIMIT = 5;

interface Version {
  major: number;
  minor: number;
  patch: number;
  pre: string | null;
  raw: string;
}

export interface UpdateInfo {
  currentVersion: string;
  latestVersion: string;
  isPrerelease: boolean;
}

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

const pickLatest = (releases: any[], wantPrerelease: boolean): Version | null => {
  return releases
    .filter((r) => !r.draft && !!r.prerelease === wantPrerelease)
    .reduce<Version | null>((best, r) => {
      const v = parseVersion(r.tag_name);
      if (!v) return best;
      if (!best || compareVersion(v, best) > 0) return v;
      return best;
    }, null);
};

export const useVersionCheck = () => {
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const retryCountRef = useRef(0);
  // const addLog = useDebugStore((s) => s.addLog);

  useEffect(() => {
    const check = async () => {
      const version = window.dpsData?.getVersion?.();
      // addLog(`version: ${version}`);

      const current = parseVersion(version ?? "");
      // addLog(`current: ${JSON.stringify(current)}`);

      if (!current || !(window as any).javaBridge) {
        // addLog(`재시도: ${retryCountRef.current}`);
        if (retryCountRef.current < RETRY_LIMIT) {
          retryCountRef.current++;
          setTimeout(check, RETRY_INTERVAL);
        }
        return;
      }

      try {
        // addLog("릴리즈 대기");
        const res = await fetch(API, {
          headers: { Accept: "application/vnd.github+json" },
          cache: "no-store",
        });
        if (!res.ok) {
          // addLog(`실패: ${res.status}`);
          return;
        }

        const releases = await res.json();
        // addLog(`릴리즈: ${releases.length}`);

        const latestStable = pickLatest(releases, false);
        const latestBeta = pickLatest(releases, true);
        // addLog(`마지막: ${latestStable?.raw} 마지막베타: ${latestBeta?.raw}`);

        let target: Version | null = null;
        let isPrerelease = false;

        if (latestStable && compareVersion(latestStable, current) > 0) {
          target = latestStable;
          isPrerelease = false;
        } else if (current.pre && latestBeta && compareVersion(latestBeta, current) > 0) {
          target = latestBeta;
          isPrerelease = true;
        }

        // addLog(`타겟: ${target?.raw ?? "없음"}`);

        if (target) {
          setUpdateInfo({
            currentVersion: current.raw,
            latestVersion: target.raw,
            isPrerelease,
          });
        }
      } catch (e) {
        // addLog(`에러: ${e}`);
      }
    };

    check();
  }, []);

  const openReleasePage = () => {
    (window as any).javaBridge.openBrowser(RELEASE_URL);
    (window as any).javaBridge.exitApp();
  };

  return { updateInfo, openReleasePage };
};
