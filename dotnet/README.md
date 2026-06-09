# waffle_meter — .NET/WPF 마이그레이션 (dotnet/)

기존 Kotlin 앱(`../`)과 **병행**하는 신규 .NET/WPF 코드. 마이그레이션 전략·단계·리스크는
[`../docs/wpf-migration-plan.md`](../docs/wpf-migration-plan.md), 패리티 하니스 설계는
[`../docs/phase-0-parity-harness.md`](../docs/phase-0-parity-harness.md) 참조.

> ⚠️ **현재 이 머신에 .NET SDK가 설치돼 있지 않습니다.** 아래 파일들은 작성됐으나
> **아직 빌드/테스트가 검증되지 않았습니다.** SDK 설치 후 `dotnet test`로 확인하세요.

## 사전 요구
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
  - 프롬프트에서 바로 설치하려면: `! winget install Microsoft.DotNet.SDK.8`

## 현재까지 작성된 것 (Phase 0 / 초기 Phase 2)
가장 정확성-핵심이고 라이브 코퍼스 없이 검증 가능한 조각부터:

```
dotnet/
  Directory.Build.props                         net8.0 / nullable / implicit usings 공통
  src/WaffleMeter.Capture/                      순수 캡처/재조립 로직 (플랫폼 의존성 0)
    CapturedSegment.cs                          모든 캡처 소스(WinDivert/Npcap/코퍼스)의 공통 계약
    PacketAlignmenter.cs                        ★ Kotlin PacketAlignmenter verbatim 포팅 (seq 정렬/wrap)
    Corpus/CaptureCorpusReader.cs               dev 로거 .jsonl → CapturedSegment 스트림
  tests/WaffleMeter.Capture.Tests/
    PacketAlignmenterTests.cs                   합성 패리티 케이스(wrap/reorder/retransmit/gap/reset)
```

## 빌드 / 테스트 (SDK 설치 후)
```powershell
cd dotnet
dotnet test tests/WaffleMeter.Capture.Tests   # ProjectReference로 Capture까지 함께 빌드
# 선택: 솔루션 파일 생성
dotnet new sln -n WaffleMeter
dotnet sln add src/WaffleMeter.Capture tests/WaffleMeter.Capture.Tests
```

## 패리티 코퍼스 캡처 (Kotlin은 손대지 않음, dev 빌드 사용)
1. `dev/packet-logging` 빌드 실행(이미 PacketDebugLogger 계측 포함). 설정 패널의 패킷 로깅 토글로 세션 시작.
2. AION2에서 대표 전투(보스/멀티히트/버프/소환 포함) 플레이 후 로깅 정지.
3. 산출물: `%APPDATA%\waffle_meter.v1.4\packet-debug-logs\<timestamp>-packet-debug.jsonl`
4. 이 파일을 `dotnet/corpus/<session>/capture.jsonl`로 복사(이 디렉토리는 .gitignore — 게임 트래픽 비커밋).
5. `CaptureCorpusReader.ReadCaptures(path)` → `PacketAlignmenter.Feed(...)`로 Layer 1(정렬) 재생.

> 결정성 주의: `arrivedAt`(wall-clock)이 DPS/battleEnd로 전파되므로 .NET은 **코퍼스의 arrivedAt을 그대로 소비**하고 라이브-버프 `now()`엔 고정 클럭을 주입한다([`../docs/phase-0-parity-harness.md`](../docs/phase-0-parity-harness.md) §7). 또한 현 dev 로거 `capture()`는 캡처 스레드에서 동기 write라 타이밍 교란 가능 — 오프스레드 라이터 리팩터 또는 계측-vs-비계측 battleEnd 자기일관성 측정 필요.

## 다음 (계획 순서)
- Phase 2 계속: `PacketAccumulator`/`StreamAssembler`(프레이밍, 공유 `FrameLength`), `readVarInt`(`-1` sentinel), `StreamProcessor` 디스패치 + K4os BLOCK LZ4, 휴리스틱 스캐너 — 각 레이어 코퍼스 diff.
- 캡처 백엔드: `IPacketCaptureBackend`(WinDivert 기본 + Npcap 옵션) + 권한상승 헬퍼 + named pipe (게이팅 스파이크 GO 후).
- Phase 1 Domain(엔티티 + DpsCalculator) + 결정적 골든 생성기로 `getDpsData`/`getBattleDetail`/`getLiveBuffOperatingRate` JSON 바이트 대조.
- 전체 솔루션 레이아웃(Domain/Capture/Data/Services/Addon.Contracts/App/Tests)은 계획서 §1.1.
