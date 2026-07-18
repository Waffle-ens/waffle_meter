# CLAUDE.md — waffle_meter 유지·보수 가이드

이 파일은 세션/에이전트가 이 저장소에서 작업할 때 따라야 하는 규칙이다. 아래 지침은 기본 동작보다 우선한다.

## 프로젝트 개요
- **waffle_meter**: AION2용 네이티브 **.NET 10 / WPF** DPS 미터. 배포는 **Velopack + GitHub Actions**.
- 소스: `dotnet/` (App/Capture/Services/Data + `dotnet/tools/` 진단 CLI들). 공개 릴리스 노트: 루트 `RELEASE_NOTES.md`(인앱 패치노트로 임베드됨).
- 최신 배포: **v2.7.6**.
- 사용자 대상 보고/요약/설명은 **한국어**로 작성한다.

---

## 브랜치 & 릴리스 모델 (⚠️ 핵심)

- **`main` = 유일한 릴리스 소스.** 앞으로 모든 배포는 main 커밋에 태그를 찍어 낸다.
- **`dev` = 단일 로컬 통합 브랜치.** 모든 기능/수정 결과물은 dev로 모은다.
- **작업 흐름**
  1. 새 작업은 `feature/<주제>` 또는 `fix/<주제>` 브랜치에서 진행.
  2. 완료 후 **로컬 `dev`에 병합**.
  3. 릴리스할 때 **`dev` → `main` 병합/푸시** 후 main 커밋에 태그.
- **릴리스 트리거 = 태그(브랜치 아님).** `.github/workflows/release-dotnet.yml`이 `*.*.*` 태그 푸시에 반응해 그 커밋을 빌드한다.
  - 따라서 **반드시 `origin/main`에서 도달 가능한 커밋에만 버전 태그를 찍는다.** 다른 브랜치 커밋에 태그하면 그 트리로 배포된다.
- 원격은 `origin` 하나만 쓴다. 임시/백업 브랜치는 만들지 않는다(작업 끝나면 dev 병합 후 삭제, 보존할 히스토리는 `archive/<name>` 태그로 강등).

### ⚠️ 미완료 선행작업: main 화해 (1회성, 사용자 승인 후 실행)
현재 `main`(ba5655f)은 실제 릴리스 브랜치 `wpf/combat-replay-test`(c05f255 = v2.7.6)와 **분기**되어 있다(83 vs 2, fast-forward 불가). main에 앞선 2커밋은 RELEASE_NOTES 백필 docs로 이미 릴리스에 흡수된 **잉여**다. 위 모델이 성립하려면 아래 1회성 작업이 선행돼야 한다:
```bash
git tag archive/main-pre-reset main              # 구 main 안전망
git branch -f main wpf/combat-replay-test         # main을 v2.7.6 tip으로
git push --force-with-lease origin main           # ← 공개 히스토리 재작성(최대 위험 단계)
git branch dev main                               # dev 생성
# 이후: 병합 완료 로컬 브랜치 삭제, origin/wpf/* prune, 워크트리 해제
```
**이 작업 전까지는 릴리스 기준 브랜치가 여전히 `wpf/combat-replay-test`다.** 화해 시 워킹트리의 진행중 WIP(아래)는 먼저 dev로 안전하게 옮긴다.

---

## 릴리스 절차 (SOP)
1. 버전 bump **4곳**: `dotnet/**/*.csproj`, `VersionConfig`, `RELEASE_NOTES.md`, `README.md`.
2. `RELEASE_NOTES.md` 최상단에 새 버전 섹션(한국어 user-facing) 추가 — 인앱 패치노트 팝업이 이 파일을 파싱한다.
3. `docs/progress-log.md`에 이번 릴리스의 완료 항목을 **원인+결과** 형식으로 append.
4. main 반영 후 `git tag <x.y.z>` (bare semver, `v` 접두사 없음) → `git push origin <x.y.z>`.
5. **`SHIP_REPLAY`**: GitHub repo variable. `true`일 때만 비공개 리플레이 엔진 DLL을 publish에 동봉(현재 **OFF**). 리플레이 정식 출시 시점에 별도 결정으로 켠다.

---

## ⚠️ 절대 하지 말 것
- **`waffle_meter.v1.4` (AppData 폴더명)을 리네임하지 마라.** 이건 "옛 버전 경로 잔재"가 아니라 현재 **모든 v2.x가 읽고 쓰는 사용자 데이터 네임스페이스**(설정·저장 전투·폰트·리플레이·패킷 로그)다. `waffle_meter`로 바꾸면 기존 사용자 데이터가 전부 고아가 되고 MSI 제품이 달라져 자동 업데이트가 끊긴다. 정 바꾸려면 `PropertyHandler.LegacyAppNames` 복사-포워드를 쓰는 **별도 마이그레이션 기능**으로 다뤄라. (`AppName` 상수 위치: `PropertyHandler.cs`, `PacketDebugLogger.cs`, `BuffDiag.cs`, `CombatDiag.cs`, `OverlayController.cs`, `Converters.cs`, `DevPacketLogReplay.cs`.)
- **`CombatDiag.cs` 및 진행중 진단 계측을 무심코 커밋하지 마라.** 현재 워킹트리의 `MeterEngine.cs`/`StreamAssembler.cs`/`PacketAccumulator.cs`/`StreamProcessor.cs`/`MeterServices.cs` 수정본은 미해결 버그(나야트만 첫 보스 미포착) 조사용 **WIP**다. `CombatDiag.cs`는 `.gitignore`에 등록되어 있다.
- **저장소 산출물에 경쟁 미터명(INGMeter, A2Power 등)을 넣지 마라.** 경쟁 분석은 로컬(memory)에서만.
- **`docs/`를 un-ignore하지 마라.** RE·경쟁분석·"커밋금지" 문서가 들어있다(특히 `docs/replay/`, `docs/security/`, `docs/packet-re/`, `docs/datamine/`).

---

## 지식 관리 (docs + memory)
- **`docs/`는 비공개 로컬 지식베이스** (`.gitignore`로 공개 repo에서 제외). `memory/`와 평행하게 운영. 전체 구조는 **`docs/README.md`** 참조.
- **완료 작업**은 **`docs/progress-log.md`**에 `원인 + 결과(커밋/버전)` 1엔트리로 append. 심층 근본원인 writeup은 `memory/`에 두고, progress-log는 요약 + `[[memory-slug]]` 링크만 (중복 금지).
- **데이터마인**: **`docs/datamine/README.md`** — 추출 대상·방법(CUE4Parse)·현행 데이터 위치·패치별 최신도·다음 패스 체크리스트.
- **리플레이 모듈**: **`docs/replay/README.md`** — 2-repo 구조·SHIP_REPLAY 게이트·RE 사실 인덱스.
- 4개 기록면의 역할 경계: `RELEASE_NOTES.md`(공개·사용자용 "무엇") / `docs/progress-log.md`(내부·엔지니어용 "왜+결과") / `memory/`(근본원인 심층) / Notion 일지(기간 발행물).

---

## 유지보수 백로그 (사용자 승인 하에 진행)
- **로컬 정리**: gitignore된 `build/`(구 Kotlin 잔재 1.4GB)·`dist/`·`publish/`·`Releases/`·`bridge/*.msi` 등 ~2.4GB 로컬 삭제 가능. 병합 완료 로컬 브랜치 삭제, 워크트리 2개(`.codex`, `.claude`) 해제, 고아 ref `tk-original/main` 정리.
- **오배포 방지 CI 가드(권장)**: 태그 커밋이 `origin/main`에서 도달 가능한지 검증하는 워크플로 job 추가.
- **버전 bump 자동화(권장)**: 4곳 수기 bump를 단일 소스 + 스크립트로.
- **루트 tracked 핸드오프 3종** (`handoff-buff-uptime-web.md`, `handoff-replay-web.md`, `stats-security-xverify.md`): 공개 repo에서 docs/(비공개)로 옮길지 = 웹팀 참조 여부 확인 후 결정.
