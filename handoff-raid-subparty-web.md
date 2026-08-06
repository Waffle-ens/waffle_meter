# 핸드오프: 공대 서브파티 판정을 로스터 기준으로 (통계웹)

작성 2026-08-07 · 미터 브랜치 `feature/raid-roster-size` · 웹 레포는 이 문서 작성 시점에 **읽기만** 했습니다.

## 한 줄

미터가 이제 `battle.rosterSize`(공대 정원)를 보냅니다. 웹이 지금 `battle.partySize`(**딜한 사람 수**)로
"공대인가"를 판정하는 두 곳을 이 값으로 바꾸면, 필드 전투를 "N인 공대"로 부르는 오라벨과 공대원 일부만
딜한 전투의 서브파티 유실이 함께 사라집니다.

## 왜 지금인가 — 미터 쪽은 더 할 게 거의 없습니다

패킷 코퍼스 192세션을 실제 파이프라인으로 재생해 측정했습니다(오프라인 고정, 2회 동일 결과).

업로드까지 가는 전투 중 라벨 대상(`partySize >= 6`)은 22건이고, 그중 **10건**이
`N인 공대 시너지 구분 이전 지표`를 답니다. 그 10건의 정체:

| 사유 | 건수 | 미터가 고칠 수 있나 |
|---|---|---|
| **로스터가 아예 없는 전투** (필드/zerg인데 딜러가 6명 이상) | 7 | ✗ 공대가 아님. 웹이 공대라고 부르는 게 문제 |
| **5인 파티인데 딜러가 6명** (소환수/외부인이 딜러 수를 부풀림) | 1 | ✗ 위와 같음 |
| **진짜 공대인데 일부만 그 보스에 딜함** | 2 | ✗ 0딜 참가자를 날조하지 않는 한 불가 |

즉 **남은 라벨의 8/10은 애초에 공대가 아닌 전투**입니다. 나머지 2건도 미터가 만들어낼 수 없는 정보입니다.

진짜 공대 리포트만 보면 이미 대부분 신뢰됩니다 — 10인 공대 16건 중 **12건 신뢰**, 나머지 4건이 위의
"일부만 딜" 유형입니다. 그리고 **그 4건 전부, 참가자 전원이 서로 다른 유효 슬롯을 갖고 있습니다.**
미터는 누가 1파티고 누가 2파티인지 알고 있는데 웹이 완비 조건 때문에 버리는 상태입니다.

> 참고로 미터 쪽 개선안(0x9200 uid 조인, handle 조인, 슬롯 소거법)도 측정했는데 **순증 0건**이었습니다.
> 업로드되는 전투 중 "참가자 수 == 정원인데 바인딩이 모자란" 경우가 존재하지 않기 때문입니다.
> 바인딩은 이미 충분하고, 병목은 웹 규칙입니다.

## 미터가 보내기 시작한 것

`battle.rosterSize` — 0x9702 로스터 스냅샷의 인원 수. 전투 저장 시점에 동결합니다.

```jsonc
"battle": {
  "startedAt": 1754500000000,
  "endedAt":   1754500184000,
  "durationMs": 184000,
  "partySize": 8,      // 딜한 사람 수 (의미 불변)
  "rosterSize": 10     // 공대 정원. 로스터를 못 잡았으면 키 자체가 없음
}
```

- **가법·optional**입니다. 로스터가 없으면 키를 안 보냅니다(0이 아니라 부재).
- 웹 zod 오브젝트는 non-strict라 지금 배포본은 이 키를 그냥 버립니다 → **웹 배포 전에 미터를 먼저 내도 안전**합니다.
- `partySize`의 의미는 **바꾸지 않았습니다**. `battle-group.ts:47,66`의 중복제거 그룹키,
  `rankings/overall.ts:237`의 코호트 축, 전투 상세의 "공대 N명" 표시가 이 값에 물려 있어서입니다.

## 웹에 부탁드리는 변경 (2건, 독립적)

### 변경 1 — 공대 판정을 rosterSize로 (라벨 오분류 해소, 8/10건)

`src/server/reports/ingest-report.ts`

```ts
// 지금
function getRaidPartySize(payloadPartySize: number) {
  return payloadPartySize === 10 ? 10 : payloadPartySize === 8 ? 8 : null;
}

// 제안: 로스터가 있으면 그걸 쓰고, 없으면 기존 동작
function getRaidPartySize(payloadPartySize: number, rosterSize?: number | null) {
  const size = rosterSize ?? payloadPartySize;
  return size === 10 ? 10 : size === 8 ? 8 : null;
}
```

그리고 라벨 쪽(`profile.ts:271`, `recent.ts:220`, `identity-page.ts:216`)과
`recent.ts:435`(`synergyTrusted: ![8,10].includes(row.partySize) || row.subPartyKnown`)의
`row.partySize` 기반 "공대인가" 판정을 rosterSize로 바꿔 주세요.
지금은 딜러가 6명 이상이기만 하면 공대로 부릅니다 — 실측 코퍼스에 **딜러 70명짜리 필드 전투**가
"70인 공대 시너지 구분 이전 지표"로 표시되는 케이스가 있습니다.

⚠️ **스키마 추가가 필요합니다.** 이 라벨들은 payload가 아니라 **DB 행**을 읽습니다
(`battleReports.partySize`, `battleReports.subPartyKnown` — `src/db/schema.ts:157,161`).
`battle_reports`에 `roster_size smallint NULL`을 추가하고 ingest에서 `payload.battle.rosterSize`를 채워 주세요.
과거 행은 NULL이고, 그때는 기존 `partySize` 폴백으로 떨어지므로 회귀가 없습니다.

**반대 방향 오탐도 같이 잡힙니다**: 지금은 필드 전투에서 우연히 딜러가 정확히 8명이나 10명이면
`getRaidPartySize`가 8/10을 돌려줘 **공대가 아닌 전투를 공대로 취급**합니다. rosterSize가 있으면 그 경로가 막힙니다.

### 변경 2 — 참가자 완비 요구 완화 (진짜 공대 잔여분 해소, 2/10건)

`resolveParticipantSynergy`(`:183-190`)의 `payload.participants.length === raidPartySize`가
"공대원 전원이 이 보스에 딜했어야 한다"를 요구합니다. 기믹 분할 전투(심연의 날개 케투 = 항상 slot 5-8만,
이격의 라후 = 항상 slot 1-5만)는 이 조건을 **원리적으로** 만족할 수 없습니다.

서브파티 시너지 계산 자체(`:216-226`)는 참가자를 슬롯으로 두 그룹 나눠 그룹별 마스크를 구할 뿐이라
**전원 출석이 필요하지 않습니다.** 완비 조건은 신뢰 게이트지 계산 조건이 아닙니다.

제안: `hasCompleteRaidSlots`(전원 + 정확한 {1..N} 세트) 대신 아래를 신뢰 조건으로.

```ts
// 참가자 전원이 슬롯을 갖고, 서로 겹치지 않으며, 1..raidPartySize 범위 안이다
function hasCoherentRaidSlots(participants, raidPartySize) {
  const slots = participants.map((p) => p.partySlot);
  return slots.every((s) => typeof s === "number" && s >= 1 && s <= raidPartySize)
    && new Set(slots).size === slots.length;
}
```

**정확도는 오히려 좋아집니다.** 지금 이 전투들이 받는 폴백은 `getWholePartySynergyMask`인데, 그건
`payload.partyComposition.jobs`에서 계산되고 그 값은 미터가 **기여자(=딜한 사람)로만** 채웁니다
(`StatsPayloadBuilder.cs:171-176`). 즉 폴백도 결석자를 포함하지 않습니다 — 같은 5명을 놓고
"한 덩어리 마스크"를 주느냐 "서브파티별 마스크"를 주느냐의 차이일 뿐이고, 후자가 더 정확합니다.
기믹 분할 전투에서는 다른 서브파티가 물리적으로 다른 장소에 있었으므로 시너지를 주지도 않았습니다.

**남는 판단거리**: 같은 서브파티 안에 딜을 안 한 사람이 있으면(전투 내내 사망 등) 그 사람의 직업이
마스크에서 빠져 **과소** 계산됩니다. 과소 마스크는 해당 참가자를 "시너지 덜 받은" 코호트로 분류하므로
백분위가 **후하게** 나올 수 있습니다. 이 케이스가 실측 코퍼스엔 없었지만(기믹 분할은 서브파티 통째 결석),
보수적으로 가려면 "present 서브파티의 인원이 `slotsPerSubParty`와 같을 때만 그 그룹 마스크를 신뢰"로
좁힐 수 있습니다.

⚠️ **미터가 0딜 참가자를 채워 넣는 방식은 하지 않습니다.** 그 사람들은 실제로 다른 보스와 싸우고 있었고,
참가자로 만들면 DPS 분포와 랭킹이 오염됩니다.

## 수용 기준

1. 로스터 10인 공대 리포트에서 참가자가 5~9명이어도 `sub_party_known`이 켜진다(변경 2 채택 시).
2. 로스터가 없는 필드 전투는 딜러가 몇 명이든 "N인 공대 …" 라벨이 뜨지 않는다(변경 1).
3. `rosterSize`가 없는 과거 리포트의 동작·표시가 그대로다.
4. `battle.partySize` 기반 그룹키·코호트·표시가 그대로다.

## 백필

`battle_participants.party_slot`은 과거 8인 리포트에도 저장돼 있으므로, 저장된 슬롯으로
`sub_party_known` / `received_synergy_mask`를 재계산하는 백필이 가능합니다. 다만 과거 리포트에는
`rosterSize`가 없어 변경 1의 혜택은 못 받습니다(신규 리포트부터).

## 롤백

미터 쪽은 필드 추가뿐이라 웹이 안 읽으면 아무 일도 안 일어납니다. 웹 변경 2개도 각각 독립이고
`rosterSize` 부재 시 기존 경로로 떨어지므로 개별 롤백이 가능합니다.
