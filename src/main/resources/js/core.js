class DpsApp {
  static instance;

  constructor() {
    if (DpsApp.instance) return DpsApp.instance;

    this.POLL_MS = 200;
    this.USER_NAME = "-------";

    this.dpsFormatter = new Intl.NumberFormat("ko-KR");
    this.lastJson = null;
    this.isCollapse = false;

    // 빈데이터 덮어쓰기 방지 스냅샷
    this.lastSnapshot = null;
    // reset 직후 서버가 구 데이터 계속 주는 현상 방지
    this.resetPending = false;

    this.BATTLE_TIME_BASIS = "render";
    this.GRACE_MS = 30000;
    this.GRACE_ARM_MS = 1000;

    //자동리셋 시간
    this.AUTO_RESET_AFTER_ENDED_MS = 60000; // ended 이후 1분

    //자동리셋 관련
    this._endedAt = null;
    this._rawLastChangedAt = 0;
    this._hadCombat = false;

    this._autoResetStableStart = null;
    this._autoResetElapsedMs = 0;
    this._autoResetLastAt = 0;

    // battleTime 캐시
    this._battleTimeVisible = false;
    this._lastBattleTimeMs = null;

    this._pollTimer = null;

    DpsApp.instance = this;
  }

  static createInstance() {
    if (!DpsApp.instance) DpsApp.instance = new DpsApp();
    return DpsApp.instance;
  }

  start() {
    this.elList = document.querySelector(".list");
    this.elBossName = document.querySelector(".bossName");
    this.elBossName.textContent = "DPS METER";

    this.resetBtn = document.querySelector(".resetBtn");
    this.collapseBtn = document.querySelector(".collapseBtn");

    this.bindHeaderButtons();
    this.bindDragToMoveWindow();

    this.meterUI = createMeterUI({
      elList: this.elList,
      dpsFormatter: this.dpsFormatter,
      getUserName: () => this.USER_NAME,
      onClickUserRow: (row) => this.detailsUI.open(row),
    });

    this.battleTime = createBattleTimeUI({
      rootEl: document.querySelector(".battleTime"),
      tickSelector: ".tick",
      statusSelector: ".status",
      graceMs: this.GRACE_MS,
      graceArmMs: this.GRACE_ARM_MS,
      visibleClass: "isVisible",
    });
    this.battleTime.setVisible(false);

    this.detailsPanel = document.querySelector(".detailsPanel");
    this.detailsClose = document.querySelector(".detailsClose");
    this.detailsTitle = document.querySelector(".detailsTitle");
    this.detailsStatsEl = document.querySelector(".detailsStats");
    this.skillsListEl = document.querySelector(".skills");

    this.detailsUI = createDetailsUI({
      detailsPanel: this.detailsPanel,
      detailsClose: this.detailsClose,
      detailsTitle: this.detailsTitle,
      detailsStatsEl: this.detailsStatsEl,
      skillsListEl: this.skillsListEl,
      dpsFormatter: this.dpsFormatter,
      getDetails: (row) => this.getDetails(row),
    });
    window.ReleaseChecker?.start?.();

    this.startPolling();
    this.fetchDps();
  }

  nowMs() {
    return typeof performance !== "undefined" ? performance.now() : Date.now();
  }

  safeParseJSON(raw, fallback = {}) {
    if (typeof raw !== "string") {
      return fallback;
    }
    try {
      const value = JSON.parse(raw);
      return value && typeof value === "object" ? value : fallback;
    } catch {
      return fallback;
    }
  }

  startPolling() {
    if (this._pollTimer) return;
    this._pollTimer = setInterval(() => this.fetchDps(), this.POLL_MS);
  }

  stopPolling() {
    if (!this._pollTimer) return;
    clearInterval(this._pollTimer);
    this._pollTimer = null;
  }

  resetAll({ callBackend = true } = {}) {
    this.resetPending = !!callBackend;

    this._resetAutoResetState?.();

    this.lastSnapshot = null;
    this.lastJson = null;

    this._battleTimeVisible = false;
    this._lastBattleTimeMs = null;
    this.battleTime?.reset?.();
    this.battleTime?.setVisible?.(false);

    this.detailsUI?.close?.();
    this.meterUI?.onResetMeterUi?.();

    if (this.elBossName) {
      this.elBossName.textContent = "DPS METER";
    }
    if (callBackend) {
      window.javaBridge?.resetDps?.();
    }
  }

  _resetAutoResetState() {
    this._endedAt = null;
    this._rawLastChangedAt = 0;
    this._hadCombat = false;

    this._autoResetStableStart = null;
    this._autoResetElapsedMs = 0;
    this._autoResetLastAt = 0;
  }

  // 전투 끝난 후 (회색불) AUTO_RESET_AFTER_ENDED_MS 이후 초기화
  // detail 열려있으면 잠시 중단

  _tickAutoReset(now, { rawChanged = false, hasRows = false } = {}) {
    if (this.isCollapse) return;
    if (this.resetPending) return;

    if (rawChanged) {
      this._rawLastChangedAt = now;
    } else if (!this._rawLastChangedAt) {
      this._rawLastChangedAt = now;
    }

    if (hasRows) {
      this._hadCombat = true;
    }

    // 전투 종료는 isEnded(회색불)로 변한 순간
    const endedNow = !!this.battleTime?.isEnded?.();

    // ended가 아니면 카운트다운 초기화
    if (!endedNow) {
      this._endedAt = null;
      this._autoResetStableStart = null;
      this._autoResetElapsedMs = 0;
      this._autoResetLastAt = 0;
      return;
    }

    if (!this._hadCombat) {
      return;
    }

    // 전투 종료 시점 (회색불 들어온 순간)
    if (this._endedAt === null) {
      this._endedAt = now;
    }

    // 전투 종료 후 1분간 raw가 완전히 동일하면 리셋

    const paused = !!this.detailsUI?.isOpen?.();
    const stableStartCandidate = Math.max(this._endedAt, this._rawLastChangedAt || this._endedAt);

    // raw가 바뀌었거나 endedAt이 갱신되면 시간초 초기화
    if (this._autoResetStableStart !== stableStartCandidate) {
      this._autoResetStableStart = stableStartCandidate;
      this._autoResetElapsedMs = 0;
      this._autoResetLastAt = now;
    }

    // details가 열려 있으면 잠깐 멈춤.
    if (paused) {
      this._autoResetLastAt = now; // 멈춘동안 delta가 누적되지 않게 고정
      return;
    }

    const lastAt = this._autoResetLastAt || now;
    this._autoResetElapsedMs += Math.max(0, now - lastAt);
    this._autoResetLastAt = now;

    if (this._autoResetElapsedMs >= this.AUTO_RESET_AFTER_ENDED_MS) {
      this.resetAll({ callBackend: true });
    }
  }

  fetchDps() {
    if (this.isCollapse) return;
    const now = this.nowMs();
    const raw = window.dpsData?.getDpsData?.();
    // globalThis.uiDebug?.log?.("getBattleDetail", raw);

    // 값이 없으면 타이머 숨김
    if (typeof raw !== "string") {
      this._rawLastChangedAt = now;

      this._lastBattleTimeMs = null;
      this._battleTimeVisible = false;
      this.battleTime.setVisible(false);
      return;
    }

    if (raw === this.lastJson) {
      const shouldBeVisible = this._battleTimeVisible && !this.isCollapse;

      this.battleTime.setVisible(shouldBeVisible);
      if (shouldBeVisible) {
        this.battleTime.update(now, this._lastBattleTimeMs);
      }
      this._tickAutoReset(now, this._lastBattleTimeMs, {
        rawChanged: false,
        hasRows: !!(this.lastSnapshot && this.lastSnapshot.length),
      });
      return;
    }

    this.lastJson = raw;

    const { rows, targetName, battleTimeMs } = this.buildRowsFromPayload(raw);
    this._lastBattleTimeMs = battleTimeMs;

    this._tickAutoReset(now, battleTimeMs, { rawChanged: true, hasRows: rows.length > 0 });

    const showByServer = rows.length > 0;
    if (this.resetPending) {
      const resetAck = rows.length === 0;

      this._battleTimeVisible = false;
      this.battleTime.setVisible(false);

      if (!resetAck) {
        return;
      }

      this.resetPending = false;
    }
    // 빈값은 ui 안덮어씀
    let rowsToRender = rows;
    if (rows.length === 0) {
      if (this.lastSnapshot) rowsToRender = this.lastSnapshot;
      else {
        this._battleTimeVisible = false;
        this.battleTime.setVisible(false);
        return;
      }
    } else {
      this.lastSnapshot = rows;
    }

    // 타이머 표시 여부
    const showByRender = rowsToRender.length > 0;
    const showBattleTime = this.BATTLE_TIME_BASIS === "server" ? showByServer : showByRender;

    const eligible = showBattleTime && Number.isFinite(Number(battleTimeMs));

    this._battleTimeVisible = eligible;
    const shouldBeVisible = eligible && !this.isCollapse;

    this.battleTime.setVisible(shouldBeVisible);

    if (shouldBeVisible) {
      this.battleTime.update(now, battleTimeMs);
    }

    // 렌더
    this.elBossName.textContent = targetName ? targetName : "";
    this.meterUI.updateFromRows(rowsToRender);
  }

  buildRowsFromPayload(raw) {
    const payload = this.safeParseJSON(raw, {});
    const targetName = typeof payload?.targetName === "string" ? payload.targetName : "";

    const mapObj = payload?.map && typeof payload.map === "object" ? payload.map : {};
    const rows = this.buildRowsFromMapObject(mapObj);

    const battleTimeMsRaw = payload?.battleTime;
    const battleTimeMs = Number.isFinite(Number(battleTimeMsRaw)) ? Number(battleTimeMsRaw) : null;

    return { rows, targetName, battleTimeMs };
  }

  buildRowsFromMapObject(mapObj) {
    const rows = [];

    for (const [id, value] of Object.entries(mapObj || {})) {
      const isObj = value && typeof value === "object";

      const job = isObj ? (value.job ?? "") : "";
      const nickname = isObj ? (value.nickname ?? "") : "";
      const name = nickname || String(id);

      const dpsRaw = isObj ? value.dps : value;
      const dps = Math.trunc(Number(dpsRaw));

      // 소수점 한자리
      const contribRaw = isObj ? Number(value.damageContribution) : NaN;
      const damageContribution = Number.isFinite(contribRaw)
        ? Math.round(contribRaw * 10) / 10
        : NaN;

      if (!Number.isFinite(dps)) {
        continue;
      }

      rows.push({
        id: String(id),
        name,
        job,
        dps,
        damageContribution,
        isUser: name === this.USER_NAME,
      });
    }

    return rows;
  }

  async getDetails(row) {
    const raw = await window.dpsData?.getBattleDetail?.(row.id);
    let detailObj = raw;
    // globalThis.uiDebug?.log?.("getBattleDetail", detailObj);

    if (typeof raw === "string") detailObj = this.safeParseJSON(raw, {});
    if (!detailObj || typeof detailObj !== "object") detailObj = {};

    const skills = [];
    let totalDmg = 0;

    let totalTimes = 0;
    let totalCrit = 0;
    let totalParry = 0;
    let totalBack = 0;
    let totalPerfect = 0;
    let totalDouble = 0;

    for (const [code, value] of Object.entries(detailObj)) {
      if (!value || typeof value !== "object") continue;

      const nameRaw = typeof value.skillName === "string" ? value.skillName.trim() : "";
      const baseName = nameRaw ? nameRaw : `스킬 ${code}`;

      // 공통 
      const pushSkill = ({
        codeKey,
        name,
        time,
        dmg,
        crit = 0,
        parry = 0,
        back = 0,
        perfect = 0,
        double = 0,
        countForTotals = true,
      }) => {
        const dmgInt = Math.trunc(Number(String(dmg ?? "").replace(/,/g, ""))) || 0;
        if (dmgInt <= 0) {
          return;
        }

        const t = Number(time) || 0;

        totalDmg += dmgInt;
        if (countForTotals) {
          totalTimes += t;
          totalCrit += Number(crit) || 0;
          totalParry += Number(parry) || 0;
          totalBack += Number(back) || 0;
          totalPerfect += Number(perfect) || 0;
          totalDouble += Number(double) || 0;
        }
        skills.push({
          code: String(codeKey),
          name,
          time: t,
          crit: Number(crit) || 0,
          parry: Number(parry) || 0,
          back: Number(back) || 0,
          perfect: Number(perfect) || 0,
          double: Number(double) || 0,
          dmg: dmgInt,
        });
      };

      // 일반 피해
      pushSkill({
        codeKey: code,
        name: baseName,
        time: value.times,
        dmg: value.damageAmount,
        crit: value.critTimes,
        parry: value.parryTimes,
        back: value.backTimes,
        perfect: value.perfectTimes,
        double: value.doubleTimes,
      });

      // 도트피해
      if (Number(String(value.dotDamageAmount ?? "").replace(/,/g, "")) > 0) {
        pushSkill({
          codeKey: `${code}-dot`, // 유니크키
          name: `${baseName} - 지속피해`,
          time: value.dotTimes,
          dmg: value.dotDamageAmount,
          countForTotals: false,
        });
      }
    }

    const pct = (num, den) => {
      if (den <= 0) return 0;
      return Math.round((num / den) * 1000) / 10;
    };
    const contributionPct = Number(row?.damageContribution);
    const combatTime = this.battleTime?.getCombatTimeText?.() ?? "00:00";

    return {
      totalDmg,
      contributionPct,
      totalCritPct: pct(totalCrit, totalTimes),
      totalParryPct: pct(totalParry, totalTimes),
      totalBackPct: pct(totalBack, totalTimes),
      totalPerfectPct: pct(totalPerfect, totalTimes),
      totalDoublePct: pct(totalDouble, totalTimes),
      combatTime,

      skills,
    };
  }

  bindHeaderButtons() {
    this.collapseBtn?.addEventListener("click", () => {
      this.isCollapse = !this.isCollapse;

      // 접히면 polling 멈추고 완전 초기화
      if (this.isCollapse) {
        this.stopPolling();
        this.elList.style.display = "none";
        this.resetAll({ callBackend: true });
      } else {
        // 펼치면 polling 재개하고 즉시 1회 fetch
        this.elList.style.display = "grid";
        this.startPolling();
        this.fetchDps();
      }

      const iconName = this.isCollapse ? "arrow-down-wide-narrow" : "arrow-up-wide-narrow";
      const iconEl =
        this.collapseBtn.querySelector("svg") || this.collapseBtn.querySelector("[data-lucide]");
      if (!iconEl) {
        return;
      }

      iconEl.setAttribute("data-lucide", iconName);
      lucide.createIcons({ root: this.collapseBtn });
    });
    this.resetBtn?.addEventListener("click", () => {
      this.resetAll({ callBackend: true });
    });
  }

  bindDragToMoveWindow() {
    let isDragging = false;
    let startX = 0,
      startY = 0;
    let initialStageX = 0,
      initialStageY = 0;

    document.addEventListener("mousedown", (e) => {
      isDragging = true;
      startX = e.screenX;
      startY = e.screenY;
      initialStageX = window.screenX;
      initialStageY = window.screenY;
    });

    document.addEventListener("mousemove", (e) => {
      if (!isDragging) return;
      if (!window.javaBridge) return;

      const deltaX = e.screenX - startX;
      const deltaY = e.screenY - startY;
      window.javaBridge.moveWindow(initialStageX + deltaX, initialStageY + deltaY);
    });

    document.addEventListener("mouseup", () => {
      isDragging = false;
    });
  }
}

// 디버그콘솔
const setupDebugConsole = () => {
  const g = globalThis;
  if (globalThis.uiDebug?.log) return globalThis.uiDebug;

  const consoleDiv = document.querySelector(".console");
  if (!consoleDiv) {
    globalThis.uiDebug = { log: () => {}, clear: () => {} };
    return globalThis.uiDebug;
  }

  const safeStringify = (value) => {
    if (typeof value === "string") return value;
    if (value instanceof Error) return `${value.name}: ${value.message}`;
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  };

  const appendLine = (line) => {
    consoleDiv.style.display = "block";
    consoleDiv.innerHTML += line + "<br>";
    consoleDiv.scrollTop = consoleDiv.scrollHeight;
  };

  globalThis.uiDebug = {
    clear() {
      consoleDiv.innerHTML = "";
    },
    log(...args) {
      const line = args.map(safeStringify).join(" ");
      appendLine(line);
      console.log(...args);
    },
  };

  return globalThis.uiDebug;
};

setupDebugConsole();
const dpsApp = DpsApp.createInstance();
