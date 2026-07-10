# HANDOFF — 버프/디버프 업타임 표시 결함 (담당: 통계웹 세션)

> 레포: `waffle_meter_statistics`. 짝 레포: `../waffle_meter` (미터기, .NET/WPF).
> 작성: 미터기 세션(Claude). 양쪽 소스 + 데이터마인 원본(`buff.json`)으로 원인 확정. **코드 변경 없음 — 조사만 수행했다.**
> 이 문서는 `STATS_PAYLOAD_BUFF_ATTRIBUTION.md`(v4 계약)의 **§10 미해결 항목을 대체**한다.

---

## 0. 증상 (전투 상세 페이지 · 버프/디버프 업타임 탭)

1. **버프 누락** — 각 직업이 분명히 사용한 버프가 목록에 없다. 특히 다른 파티원이 걸어준 버프(호법성 진언, 치유성 축복 등)는 **단 한 건도 뜨지 않는다**.
2. **같은 버프/디버프가 여러 행으로 분리** — `살기 파열` 2행(99.8% / 81.2%), `격노 폭발` 3행(44.3% / 44.2% / 6.1%), `정령 강림` 2행(89.2% / 2.9%).
3. **디버프가 버프 탭에 섞여 있다** — 정령성·마도성의 `피해 내성 감소`가 "내 버프"로 표시된다. 데이터마인 원본상 이 스킬의 **5개 코드는 전부 `DeBuff`** 다.
4. **소모품(주문서) 행 아이콘이 시계 placeholder**.

> ⚠️ **사용자 인식 정정**: "미터기는 정상, 웹만 깨졌다"는 절반만 맞다.
> 미터기의 **전투 상세 패널**도 2·3번 증상을 똑같이 갖고 있다(`DetailModel.cs:262-291` — dedup 없음, category 없음).
> base 단위 dedup은 **전투보조 버프 오버레이에만** 존재한다(`DataManager.BuffBaseCode`의 사용처는 `:218 :226 :859 :880 :936 :1007` = 오버레이/피커 전용).
> 순수하게 **웹에서만** 깨진 것은 1번(누락)과 4번(아이콘)이다.

---

## 1. 결론 — 무엇을 누가 고치는가

| ID | 증상 | 원인 위치 | 수정 주체 |
|---|---|---|---|
| **W-1** | 버프 누락 (가장 크고 즉시 고칠 수 있음) | 웹 `isVisibleSelfBuff` 렌더 필터 | **웹, 지금** |
| **W-2** | 중복 행 + 디버프가 버프 탭에 | 웹이 `type`/`baseCode`를 모름 → 데이터마인 메타 동봉으로 해결 가능 | **웹, 지금** |
| **W-3** | 주문서 아이콘 시계 | 웹 아이콘 맵에 소모품 코드 없음 | **웹, 지금** |
| **W-4** | 시전자 미해결 보스 디버프 완전 소실 | 웹 `activeMemberDebuffs` 필터 | **웹, 지금** |
| **M-1** | payload가 실제 buff type을 안 실음 | 미터 `Buff` 레코드가 `type` 드롭 | 미터 — **✅ 수정 완료** |
| **M-2** | 업타임이 코드별로 쪼개짐 (정확한 union은 미터에서만 가능) | 미터 `GetBuffOperatingRate` 그룹 키 | 미터 — **✅ 수정 완료** |
| **M-3** | 자가버프가 `source:"other"`로 오분류 | 미터 `BuffSource`의 자릿수 가정 | 미터 — **✅ 수정 완료** |

**핵심**: W-1·W-3·W-4는 payload를 안 바꾸고 지금 고친다. W-2는 지금 payload(구버전 미터가 올린 **과거 행**)에 정보가 없지만, 웹이 **데이터마인 메타 테이블을 동봉**하면 (기존 `icon-maps.json` / `buff-values.ts`와 같은 방식) **과거 데이터까지 소급 적용**되어 지금 고칠 수 있다.

> ⚠️ **2026-07-10 갱신 — 미터 측 M-1~M-3은 이미 구현·테스트 완료(미커밋)**. 자세한 건 §4.5.
> **`schemaVersion`은 4에 그대로 있고, `baseCode`·`type` 필드만 추가된다.** 웹은 **버전이 아니라 필드 존재 여부로 분기**하라. (§4 W-5가 원래 `schemaVersion 5`를 전제로 쓰여 있었으나, 5로 올리면 현재 배포된 웹의 `z.union([2,3,4])`가 **모든 업로드를 400**으로 떨군다. 그래서 올리지 않았다.)

---

## 2. 데이터 흐름 (행 개수는 payload와 1:1)

```
[미터] UseBuff 구간들
   → DpsCalculator.GetBuffOperatingRate(uid)   ← 여기서 (코드, 시전자)별로 그룹 + 구간 union
   → OperatingData(Code, Name, Summary, Effect, OperatingRate, ActorId)
   → StatsPayloadBuilder.ToBuffPayload()       ← 여기서 source/category 부여
   → StatsBuffPayload { buffCode, buffName, operatingRate, scope, category, source, actorIdentityHash, … }
[웹] payload-schema.ts → ingest-report.ts → buff_results 테이블 (행 1:1 insert)
   → server/battles/detail.ts → battle-detail-client.tsx (렌더)
```

웹은 어떤 행도 합치지 않는다. **payload에 온 행 수 = DB 행 수 = 화면 행 수**(단, 아래 W-1 필터에 걸린 것 제외).

---

## 3. 근본 원인 (증거 포함)

### W-1 — 웹이 `party`/`other` 버프를 렌더 단계에서 전량 폐기 ⭐ 누락의 직접 원인

`src/app/characters/battles/[battleId]/battle-detail-client.tsx:236-248`

```ts
function isVisibleSelfBuff(buff: BattleBuffDetail) {
  if (buff.source === "self") return true;      // 통과
  if (isScrollBuff(buff)) return true;          // 이름에 "주문서" 포함 시 통과
  return buff.source === undefined && !buff.actorIdentityHash;  // v4 payload는 여기서 전부 false
}
```

`:278-284`의 `activeMemberBuffs`가 이 필터를 적용한다. 결과:

- `source: "party"` (다른 플레이어가 걸어준 버프) → **전부 사라진다**. `buffSourceLabel(:196-207)`의 `"파티원 버프"` 케이스는 **도달 불가능한 죽은 코드**다.
- `source: "other"` 중 이름에 `"주문서"`가 없는 것(물약·음식·타직업 자가버프, 그리고 **M-3으로 오분류된 자가버프**) → **전부 사라진다**.

DB에는 전부 들어 있다 — `ingest-report.ts:461-501`은 `participants[].buffs`를 필터 없이 insert한다. **표시 단계에서만 잘린다.**

> ✅ **rDPS/nDPS에는 영향이 없다.** `dps-metrics.ts:36-45`의 `getBuffGain`은 ingest 시점에 payload 원본으로 계산되어 컬럼에 저장된다. 이 필터는 순수 표시용이므로, 걷어내도 숫자는 1도 안 변한다. **가장 안전한 P0 수정.**

---

### W-2 — 웹이 실제 buff `type`과 스킬 base를 모른다 → 중복 행 + 탭 오분류

payload의 `category`는 **효과의 성질이 아니라 대상(target) 기준 고정값**이다.
`StatsPayloadBuilder.cs:198-210` — `scope="participant"`면 무조건 `"buff"`, `scope="boss"`면 무조건 `"debuff"`.

원본 `dotnet/Assets/json/buff.json`(5,481 항목)은 항목마다 실제 타입을 갖고 있다:

```jsonc
"118000071": { "name": "살기 파열", "type": "Buff",   "icon": "...", "icon_view": true,  … }
"118000081": { "name": "살기 파열", "type": "DeBuff", "icon": "...", "icon_view": true,  … }
```

그러나 미터의 `Buff` 레코드는 `DpsModel.cs:166`에서

```csharp
public sealed record Buff(int Code, string Name, string Summary, string Effect);
//  ↑ type / icon / icon_view 를 로딩하지 않음
```

→ **`type`은 payload에 실릴 수 없다.** (M-1)

그리고 `DpsCalculator.cs:609-632`의 `GetBuffOperatingRate`는 **raw 런타임 코드**로 그룹핑한다(`ResolveBuffDisplay(:591-606)`가 `buff.json`에 코드가 있으면 9자리 원본을 그대로 `Display.Code`로 쓴다). → 같은 스킬의 랭크/양상 변형이 각각 별도 행이 된다. (M-2)

**실측 검증 (buff.json 전수 조사):**

| 스크린샷 행 | 실제 코드 | base | type |
|---|---|---|---|
| `살기 파열` ×2 | `118000071`, `118000081`(또는 `…091`) | 모두 `11800000` | **Buff 1 + DeBuff 1** ← 버프 탭에 디버프가 섞임 |
| `격노 폭발` ×3 | `113900071`, `113900072`, + 8자리 폴백 `11390000` | 모두 `11390000` | DeBuff (동일) ← 순수 랭크 중복 |
| `정령 강림` ×2 | `167300011`, `167300012` | 모두 `16730000` | Buff (동일) ← 순수 랭크 중복 |
| `피해 내성 감소` (내 버프로 표시) | `163000007`(정령성) / `152800221`(마도성) | `16300000` / `15280000` | **DeBuff** ← 버프 탭에 있으면 안 됨 |

전수 통계: 9자리 직업 버프코드 1,156개 → `(base, name, type)` 그룹 **344개**. 그중 **111개 그룹이 코드 2개 이상**(= 랭크 중복). 전체 그룹의 약 1/3이 중복 행으로 새어 나온다.

#### 🚨 그룹 키를 잘못 잡으면 더 나빠진다 — 두 개의 함정

**함정 A: 이름으로 묶지 말 것.** `지연 피해`는 코드 9개 / **base 5개**(`14170000` 궁성, `15050000`·`15320000` 마도성, `16300000`·`16330000` 정령성)로, **서로 다른 스킬이 같은 이름을 공유**한다. 스크린샷 11의 정령성 `지연 피해` 2행(56.9% / 47.3%)은 **정상**이다(base가 다름). 이름 기준 dedup은 이걸 잘못 합친다.

**함정 B: base만으로도 묶지 말 것.** 하나의 스킬 base가 **서로 다른 효과들**을 담는다:

```
base 16300000 (정령성) 안에 들어있는 코드들
  163000002  Buff    4원소
  163000003  Buff    원소
  163000004  Buff    원소 [물]
  163000005  Buff    원소 [바람]
  163000006  Buff    원소 [땅]
  163000007  DeBuff  피해 내성 감소     ← base만으로 묶으면 4원소와 합쳐진다
  163000008  DeBuff  지연 피해
  163000001  Passive None (미터가 이미 폐기)
```

또한 `(base, name)`만으로 묶어도 안 된다 — **44개 쌍이 같은 (base, name)에 type이 혼재**한다(`11800000|살기 파열 → Passive/Buff/DeBuff`, `11190000|도약 찍기 → Buff/DeBuff`, `12090000|징벌 → DeBuff/Buff` …).

> ✅ **올바른 그룹 키 = `(baseCode, buffName, type)`**
> `baseCode = code >= 110000000 && code <= 199999999 ? Math.floor(code / 100000) * 10000 : code`
> (미터 `DataManager.BuffBaseCode(:131)`과 동일한 식)
> **`type`이 없으면 이 그룹핑은 성립하지 않는다.** 지금 payload에는 없다 → 아래 W-2 작업으로 웹이 직접 확보한다.

---

### W-3 — 아이콘 맵에 소모품 코드가 없다

- 주문서 버프 코드는 `2210xxxx` 대역: 용기 `22101051`, 가호 `22104021`, 질주 `22101031`, 상급 용기 `22101061`, 충격 완화 `22120011`, 치명타 향상 `22101071`.
- `src/shared/generated/icon-maps.json`의 `status`/`statusFallback`은 각 1,115키 — **`22*` 항목이 0개**.
- → `getStatusEffectIconAsset(icon-maps.ts:51-58)`가 `null` → `detail.ts:111` `fallbackIconAsset` → `"22101051_용기의 주문서.png"`
- → `public/skill-icons/`(721개)에 **주문서 png 0개** → 404 → 시계 placeholder. 스크린샷의 "그 외" 행 전부가 이 경로다.

부수: 8자리 base 폴백 코드(`11390000` 등)도 맵에는 없지만, `public/skill-icons/11390000_격노 폭발.png`가 우연히 존재해서 이름 폴백으로 살아남는다. 운에 기대는 상태다.

---

### W-4 — 시전자를 못 맞춘 보스 디버프는 통째로 사라진다

`battle-detail-client.tsx:285-301`

```ts
detail.bossDebuffs.filter((buff) => {
  if (buff.actorParticipantId && activeMember?.id) return buff.actorParticipantId === activeMember.id;
  if (buff.actorIdentityHash && activeMember?.identityHash) return buff.actorIdentityHash === activeMember.identityHash;
  return false;   // ← 둘 다 못 맞추면 어느 탭에도 안 뜬다
})
```

`detail.ts:349-353`에서 `actorIdentityHash`는 **비공개 참가자면 `undefined`로 마스킹**된다. 그리고 `BattleBuffDetail`에 실려 있는 `actorParticipantIndex`(`:357`)는 이 필터가 쓰지 않는다. → 비공개 캐릭터가 건 디버프가 조용히 증발할 수 있다.

---

### M-1 / M-2 / M-3 — 미터 측 payload 결함 (참고 · 별도 세션)

| ID | 내용 | 앵커 |
|---|---|---|
| **M-1** | `Buff` 레코드가 `buff.json`의 `type`/`icon`/`icon_view`를 로딩하지 않음 → `category`가 대상 기준 고정값 | `DpsModel.cs:166`, `StatsPayloadBuilder.cs:198-210` |
| **M-2** | `GetBuffOperatingRate`가 raw 코드로 그룹 → 랭크 변형이 별도 행. **정확한 업타임 union은 원본 `UseBuff` 구간을 가진 미터에서만 계산 가능** | `DpsCalculator.cs:609-632` |
| **M-3** | `BuffSource`가 `buffCode / 10_000_000`으로 직업 prefix를 비교 → **9자리 코드만 가정**. 8자리 코드는 몫이 `0`/`1`이라 **절대 `self`가 될 수 없다** → 자가버프인데 `source:"other"` | `StatsPayloadBuilder.cs:449-459` |

**M-3 상세** — 이것이 "미터엔 보이는데 웹엔 없다"의 정확한 정체다:

1. `ResolveBuffDisplay`가 `buff.json`에서 코드를 못 찾으면 `NormalizeBuffSkillCode(:558-590)`로 `skills.json`을 조회해 **8자리 base 코드**를 `Display.Code`로 내보낸다(`skills.json`에 `11390000`, `11800000`, `11190000` 등 8자리 스킬코드가 실재한다 — 확인함).
2. 또한 `buff.json` 키 자체가 **8자리 3,125개 / 9자리 1,156개**다. 8자리 코드는 그냥 그대로 나간다.
3. 그 8자리 `buffCode`가 `BuffSource`에 들어가면 `11390000 / 10_000_000 = 1` → 직업 prefix(11~19)와 영원히 불일치 → **`source: "other"`**.
4. 웹의 W-1 필터가 `"other"` + 이름에 "주문서" 없음 → **폐기**.
5. 미터 상세 패널은 같은 오분류를 하지만(`DetailModel.cs:276`) "그 외" 섹션에 **넣어서 보여준다**.

부수 위험: 참가자의 `job`이 `null`이면 `ownerPrefix`가 `null`이 되어 그 사람의 **자가버프 전부**가 `"other"`가 되고, 웹에서 전량 소실된다.

---

## 4. 웹이 할 일

### W-1 (P0) — 3분류 복원 · payload 변경 없음 · rDPS 불변

- `isVisibleSelfBuff` **삭제**. `activeMemberBuffs`는 전량 통과.
- `source`로 3개 섹션 렌더: `self` → **내 버프**, `party` → **파티원 버프**, `other` → **그 외**. (`buffSourceLabel`은 이미 있다.)
- `party` 행은 시전자를 함께 표시 — `actorLabel(:209-219)` 이미 있음.
- 정렬: 섹션 순서 self → party → other, 섹션 내부는 가동률 desc. (기존 `sortBuffsForDisplay`의 "주문서 뒤로" 규칙은 `other` 섹션 내부 정렬로 흡수)
- `source`가 없는 legacy 행(v2/v3)은 "그 외"로.

**수락 기준**: 호법성이 포함된 전투에서 다른 참가자 탭에 `질주의 진언`이 **파티원 버프 / 시전자 <호법성 닉>** 으로 뜬다. 같은 전투의 `rdps`/`ndps`/`givenBuffDps`/`takenBuffDps` 숫자가 **변하지 않는다**.

---

### W-2 (P0) — 데이터마인 메타를 동봉해 `type` + `baseCode` 확보 → 중복 병합 + 탭 정정

payload를 기다리지 않고 웹이 직접 진실을 갖는다. 기존 `icon-maps.json` / `buff-values.ts`와 **동일한 생성물 패턴**이다.

**(a) 메타 생성** — 소스: `../waffle_meter/dotnet/Assets/json/buff.json` (+ `buff_custom.json` 오버레이)

`_handoff_patch_2026-07-10/buff_meta.json` → `src/shared/generated/buff-meta.json` 로 커밋:

```jsonc
{ "118000071": { "b": 11800000, "t": "buff",   "n": "살기 파열" },
  "118000081": { "b": 11800000, "t": "debuff", "n": "살기 파열" },
  "113900071": { "b": 11390000, "t": "debuff", "n": "격노 폭발" } }
```

- `b` = `code >= 110000000 && code <= 199999999 ? floor(code/100000)*10000 : code`
- `t` = `Buff→"buff"` / `DeBuff→"debuff"` / `Passive→"passive"`
- `name`이 `"None"`인 항목은 제외(미터도 `IsPlaceholderBuff`로 폐기한다).
- 8자리 base 코드(폴백 경로가 내보내는 값)도 키로 넣어라 — `buff.json`에 없으면 같은 base의 9자리 항목에서 `t`/`n`을 승계.

**(b) `category`를 메타 기준으로 정정** — `detail.ts:119-125` `getStatusCategory`

```
meta.t 가 있으면 그것을 쓴다 ("passive"는 표시에서 제외 검토)
없으면 현행 scope 폴백 (row.scope === "boss" ? "debuff" : "buff")
```

→ 플레이어에게 걸린 `DeBuff`(예: `피해 내성 감소`, `살기 파열`의 디버프 파트)는 버프 탭에서 빠지고, **"받은 디버프"** 섹션으로 간다. (탭을 새로 팔지, 버프 탭 하단 섹션으로 둘지는 재량)

**(c) 중복 병합** — 그룹 키 `(baseCode, buffName, type, source, actorIdentityHash ?? actorParticipantIndex)`

- 대표 행 = 그룹 내 `operatingRate` 최대 행. 표시값 = `max(operatingRate)`.
- 변형이 2개 이상이면 뱃지/툴팁으로 `n개 변형` 노출(코드 목록 포함) — 디버깅에 유용하다.
- ⚠️ `max`는 **근사**다. 진짜 값은 구간 union이며 미터만 계산할 수 있다(M-2). 동시 적용형(격노 폭발 44.3/44.2 → 44.3)은 오차가 거의 없고, 순차형(정령 강림 89.2/2.9 → 89.2, 실제 union은 ~92%)은 과소평가된다. **합산(`+`) 절대 금지** — 구간이 겹친다.
- ⚠️ 함정 A/B를 반드시 지켜라. 이름만/ base만으로 묶으면 안 된다(§3 W-2 참조).
- M-2 배포 후에는 payload가 이미 1행으로 오므로 이 그룹핑은 **자연히 no-op**이 된다. 코드를 되돌릴 필요 없다.

**(d) `source` 오분류 보정 (M-3 소급 대응)** — 메타가 있으면 웹이 스스로 고칠 수 있다:

```
row.source === "other" && meta.b 의 직업 prefix(floor(b / 1_000_000)) === 해당 참가자 job의 prefix
  → self 로 승격
```

직업 prefix: 검성 11 · 수호성 12 · 살성 13 · 궁성 14 · 마도성 15 · 정령성 16 · 치유성 17 · 호법성 18 · 권성 19.
(미터가 M-3을 고치면 이 보정은 신규 업로드에서 no-op이 되고, 과거 행에만 계속 작동한다.)

**수락 기준**: 검성 탭의 `살기 파열`이 버프 1행(디버프 파트는 "받은 디버프"로 이동). 검성 보스 디버프 탭의 `격노 폭발`이 3행 → **1행**. 정령성 `정령 강림` 2행 → **1행**. 정령성 보스 디버프의 `지연 피해` 2행은 **2행 그대로**(base 상이). 정령성 버프 탭에서 `피해 내성 감소`가 빠진다.

---

### W-3 (P1) — 아이콘

- `public/skill-icons/`에 소모품(주문서) 아이콘 추가. 원본 경로는 `buff.json`의 `icon` 필드(`"Abnormal/ICON_..."`)에 있다. 추출이 어려우면 **버프/디버프 카테고리별 기본 아이콘**으로 폴백하라 — 시계 placeholder는 쓰지 말 것.
- `getStatusEffectIconAsset`(`icon-maps.ts:51-58`)에 **baseCode 폴백** 추가: `status[cat:code]` → `statusFallback[code]` → `statusFallback[baseCode]` → `"{baseCode}.png"`.
- 이름 기반 `fallbackIconAsset`은 최후 수단으로만.

---

### W-4 (P1) — 보스 디버프 시전자 매칭

- `activeMemberDebuffs` 필터에 `actorParticipantIndex` 매칭을 추가(이미 `BattleBuffDetail`에 실려 있다).
- 그래도 시전자가 안 잡히는 행은 **버리지 말고** "시전자 미상" 섹션으로 노출.

---

### W-5 (P2) — 신규 payload 필드 수용

미터가 **schemaVersion 4를 유지한 채** 아래 두 필드를 추가로 보낸다. 현재 웹의 zod는 non-strict라 지금도 조용히 strip하고 있으니, **수용만 해두면 된다**. 값이 오면 `buff-meta.json`보다 우선한다.

```jsonc
{ "schemaVersion": 4,      // 그대로 (5로 올리면 z.union([2,3,4])가 400을 뱉는다)
  "buffCode": 113900071,   // 대표 런타임 코드 — 카탈로그에 있는 코드를 우선 선택 (rDPS/아이콘 호환)
  "baseCode": 11390000,    // 신규
  "type": "debuff",        // 신규: "buff" | "debuff" | "passive" (불명확하면 키 생략)
  "category": "buff",      // ⚠️ 여전히 대상 기준 고정값. participant면 무조건 "buff"
  "operatingRate": 50.4,   // 구간 union으로 재계산된 정확값 (더 이상 코드별로 쪼개지지 않음)
  … }
```

- `buffResultBaseSchema(:69)` — `baseCode`, `type` optional 수용. **`schemaVersion` union은 건드리지 말 것.**
- `db/schema.ts:419-445` `buff_results` — `base_code` bigint nullable, `effect_type` text nullable + drizzle migration.
- `detail.ts` — `type`이 오면 그것 우선, 없으면 `buff-meta.json`, 그것도 없으면 `scope` 폴백. **분기 기준은 `schemaVersion`이 아니라 필드 존재 여부.**
- `category`는 앞으로도 **영원히** 대상 기준이다. 미터가 참가자 행에 `category:"debuff"`를 보내면 `participantBuffResultSchema`의 `z.literal("buff")`가 payload 전체를 거절하므로, 진실은 `type`에만 실린다.

> 🚨 **rDPS 영향 (이미 반영된 사실)**
> `dps-metrics.ts:45`의 `getBuffValues(buff.buffCode)`는 raw 코드로 `buff-values.ts`를 조회한다(키: 9자리 423개 / 8자리 187개).
> 미터는 병합 후에도 `buffCode`에 **카탈로그에 존재하는 대표 런타임 코드**를 싣는다(폴백 행은 기존처럼 8자리 base). 조회는 깨지지 않고, 오히려 예전에 8자리 base로 나가던 행이 이제 9자리 코드로 나가 **적중률이 올라간다**.
> 다만 업타임이 코드별 값 → **구간 union**으로 바뀌므로 신규 업로드의 rDPS/nDPS가 소폭 상승해 과거 행과 불연속이 생긴다. 릴리스 노트에 명시 필요.

---

## 4.5. 미터 측 조치 (2026-07-10 — 구현·테스트 완료, 미커밋)

M-1~M-3을 모두 고쳤다. **`schemaVersion`과 `category`는 의도적으로 손대지 않았다** (§4 W-5의 두 제약 때문).

| 변경 | 파일 |
|---|---|
| `buff.json`의 `type` 파싱 → `Buff.Type` (`Buff`/`DeBuff`/`Passive`/`ItemPassive`→passive). `buff_custom.json`이 덮어써도 타입은 보존 | `ReferenceJson.cs`, `DpsModel.cs`, `DataManager.cs` |
| 업타임 그룹 키 = **`(baseCode, name, type, actorId)`**, 업타임은 전 멤버 구간의 union | `DpsCalculator.cs` |
| 카탈로그에 없는 랭크는 같은 `(base, name)`의 형제들이 **만장일치일 때만** 타입을 상속. 타입이 섞인 base(살기 파열)는 추측하지 않고 `Unknown` | `DataManager.BuffTypeFor` |
| `OperatingData`에 `BaseCode` / `Type` / `JobPrefix` 추가. `JobPrefix`는 **raw 코드가 9자리 job band일 때만** 채움 | `DpsModel.cs` |
| `BuffSource`가 `buffCode / 10_000_000` 대신 `JobPrefix` 사용 | `StatsPayloadBuilder.cs` |
| payload에 `baseCode` / `type` 추가 (null이면 키 생략) | `StatsPayload.cs` |
| 미터 상세 패널: 참가자에게 걸린 `debuff`를 **"받은 디버프"** 섹션으로 분리 | `DetailModel.cs` |

**웹에 미치는 영향**

1. **신규 업로드**는 이미 병합된 1행으로 온다 → W-2(c)의 그룹핑은 자연히 no-op. 코드를 되돌릴 필요 없다.
2. **과거 행**은 그대로 중복이므로 W-2의 `buff-meta.json` 소급 경로는 **여전히 필요**하다.
3. M-3 수정으로 신규 업로드에선 자가버프가 `source:"self"`로 온다 → W-2(d)의 보정도 과거 행 전용이 된다.
4. `type`이 오는 행은 `buff-meta.json` 조회 없이 바로 탭 분류 가능.

검증: 배포되는 `Assets/json/buff.json`을 그대로 로드해 스크린샷 케이스를 재현하는 테스트 추가 —
격노 폭발 3행 → 1행(50.4%), 살기 파열 = Buff 1 + DeBuff 1, 정령성 지연 피해는 2행 유지(base 상이),
마도성 지연 피해 5랭크 → 1행. 전체 511개 테스트 그린.

---

## 5. 검증

**증거 수준 주의**: 스크린샷의 개별 행 ↔ 코드 매핑은 `buff.json` 전수 조사 + 코드 경로로 **연역**한 것이지, 해당 전투의 DB 행을 직접 조회한 것은 아니다. 착수 전에 실측으로 확정하라:

```sql
-- 스크린샷 전투의 battleId로
SELECT participant_id, buff_code, buff_name, operating_rate, scope, category, source, actor_identity_hash
FROM buff_results WHERE report_id = '<battleId>'
ORDER BY participant_id, operating_rate DESC;
```

기대: (a) `source='party'` 행이 **DB에는 존재**한다(화면엔 없다) → W-1 확정. (b) `살기 파열`이 `118000071` + `1180000{81,91}` 2행 → W-2 확정. (c) `격노 폭발` 3행 중 하나가 `11390000`(8자리) → M-3 확정.

**테스트**:
- `tests/fixtures/`에 v4 샘플 추가: `self`/`party`/`other` 각 1건 + `(base,name,type)` 랭크 중복 2건 + 8자리 폴백 코드 1건 + 이름 충돌(`지연 피해` base 2종) 2건.
- `tests/unit/`에 그룹핑 단위 테스트 — **함정 A/B가 회귀하지 않는지**를 못박아라.
- `npm run verify` (lint + typecheck + vitest + build).

---

## 6. 하지 말 것

- ❌ `buffName`으로 dedup — `지연 피해`가 서로 다른 스킬 5개다.
- ❌ `baseCode`만으로 dedup — `4원소`와 `피해 내성 감소`가 같은 base다.
- ❌ `(baseCode, buffName)`으로 dedup — 44쌍에서 Buff와 DeBuff가 합쳐진다.
- ❌ `operatingRate` 합산 — 구간이 겹친다. `max`(근사) 또는 미터의 union(정확)만.
- ❌ `icon_view === false`인 항목 필터링 — `격노 폭발`(`icon_view:false`)이 통째로 사라진다. `icon_view`는 아이콘 선택에만 쓸 것.
- ❌ payload `category`를 신뢰 — 대상 기준 고정값이지 효과의 성질이 아니다.

---

## 7. 앵커 (파일:라인)

**웹 (`waffle_meter_statistics`)**
- `src/app/characters/battles/[battleId]/battle-detail-client.tsx` — `:118` buffIconSrc · `:196` buffSourceLabel · `:209` actorLabel · `:227` isScrollBuff · **`:236` isVisibleSelfBuff** · `:250` sortBuffsForDisplay · `:278` activeMemberBuffs · **`:285` activeMemberDebuffs**
- `src/server/battles/detail.ts` — `:111` fallbackIconAsset · **`:119` getStatusCategory** · `:127` resolveStatusIconAsset · `:131` getBuffSource · `:333-372` 버프 조립
- `src/shared/icon-maps.ts:51` getStatusEffectIconAsset
- `src/shared/generated/icon-maps.json` (status 1,115키 · `22*` 없음)
- `src/shared/payload-schema.ts:69,84,89,96`
- `src/shared/dps-metrics.ts:36-45` getBuffGain ← **rDPS 의존점**
- `src/server/reports/ingest-report.ts:461-501` · `src/server/reports/drizzle-store.ts:331-352` · `src/db/schema.ts:419-445`
- `public/skill-icons/` (721개, 주문서 0개)

**미터 (`../waffle_meter`, 읽기 전용 참조)**
- `dotnet/Assets/json/buff.json` ← **진실의 원천** (`type`, `icon`, `icon_view`)
- `dotnet/src/WaffleMeter.Data/DpsModel.cs:166` `record Buff` (type 드롭)
- `dotnet/src/WaffleMeter.Data/DpsCalculator.cs:555` IsPlaceholderBuff · `:558` NormalizeBuffSkillCode · `:591` ResolveBuffDisplay · `:609` GetBuffOperatingRate
- `dotnet/src/WaffleMeter.Data/DataManager.cs:131` BuffBaseCode
- `dotnet/src/WaffleMeter.Stats/StatsPayloadBuilder.cs:420` ToBuffPayload · `:449` BuffSource
- `dotnet/src/WaffleMeter.App.Core/DetailModel.cs:262` BuildOwnBuffs · `:293` BuildDebuffs
- 계약 문서: `STATS_PAYLOAD_BUFF_ATTRIBUTION.md` (양 레포 동일본 — §10이 이 문서로 대체됨)

---

# 부록 A — 웹 배포(`e155056`) 이후 후속 작업 (2026-07-10)

> 웹이 W-1~W-5를 배포한 뒤, 미터기 세션이 **실 패킷 로그 2건을 리플레이**하며 교차 검증하다가 버그 2건과
> **설계 결함 1건(내 잘못)** 을 발견했다. 미터기 측은 이미 고쳐서 재검증까지 마쳤다.
>
> 리플레이 규모: 2026-07-08 캡처(5인 파티, 네임드 3종 × 22전투) + 2026-07-09 캡처(최대 10인 공대, 네임드 8종 × 30전투).
> 도구: `dotnet/tools/WaffleMeter.BuffReplayCli` (신규). 참가자 버프 행 2,949개를 실물 검사했다.

**먼저, 웹이 잘한 것**: `schemaVersion`을 4로 유지, `baseCode`/`type`을 optional로 수용,
`isVisibleSelfBuff` 제거, 버전이 아니라 필드 존재 여부로 분기 — 전부 정확하다.

---

## A-0. 【설계 정정】 `buff.json`의 `type`은 신뢰할 수 없다 — 표시에서 완전히 빼라

**§3/§4에서 내가 제안한 `type` 기반 탭 분리는 틀렸다.** 미터기는 이미 되돌렸다.

**근거 1 — 게임 툴팁 + 실 패킷.** 검성 `격노 폭발`: *"적중 시 **대상에게** 10초 동안 공격력을 10% 감소시키고,
10초 동안 **자신의** PVE 피해 증폭이 10% 증가합니다."* 한 스킬이 대상엔 디버프, 자신에겐 버프를 건다.

| 코드 | `buff.json` type | 실제 수신자 | 건수 |
|---|---|---|---|
| `113900071` | DeBuff | **대상**(몹 51종) | 352 |
| `113900072` | DeBuff | **대상** | 1,148 |
| `113900481` | *(카탈로그 없음)* | **본인**(플레이어 69명) | 1,016 |

같은 패턴을 도약 찍기·징벌·나포·쇠약의 맹타·그리폰 화살(홍염)·소환: 땅의 정령 툴팁에서 모두 확인했다.
나포의 `…411/421/431/441` 4단계는 특화 *"적중 대상 많을수록 PVE 피해 내성 최대 20% 증가"* 와 정확히 대응한다.

**근거 2 — `type`이 그냥 틀렸다.** 권성 `폭주`(base `19130000`)는 26개 코드 중 절반이 `DeBuff`인데
설명은 전부 `"전투 속도, PVE 피해 증폭, PVP 피해 증폭 증가"` — 순수 버프다.

**근거 3 — `DeBuff`가 시전자 본인에게.** 검성 `살기 파열`의 `118000081`(502건) / `118000091`(1,255건)은
`buff.json`상 DeBuff지만 **100% 시전자 본인**에게 적용된다.

**게임 규칙 (프로젝트 오너 확인):**
> 플레이어 스킬의 디버프는 전부 대상에게 적용된다. 시전자 본인에게 걸리는 디버프는 플레이어 스킬 중엔 없다.
> 디버프 업타임은 "전투시간 동안 보스에게 얼마나 걸려 있었나"로 책정한다.

→ **버프냐 디버프냐는 `type`이 아니라 "누구에게 걸렸나"(`scope`)가 정한다.** 그게 `category`가 이미 하던 일이다.

### 웹이 해야 할 것

1. `src/server/battles/detail.ts` — `bossDebuffs`의 `.filter(b => b.effectType === "debuff")` **제거**
   (또는 `scope === "boss"`로 대체). 지금 이 필터는 카탈로그가 `Buff`로 타이핑한 보스 대상 행을 조용히 삭제한다.
2. `src/shared/buff-display.ts` — `groupStatusEffects`의 그룹 키에서 `effectType`을 **뺀다**
   → `(baseCode, buffName, source, actor)`.
   `살기 파열`의 세 코드가 한 행으로 합쳐진다. 이게 원래 신고된 "같은 버프 2행"의 정답이다.
   - ⚠️ **이름만/`baseCode`만으로 묶는 함정은 그대로 유효하다**(§3 함정 A/B). 둘 다 써야 한다.
3. 참가자 행은 전부 버프 섹션(`self`/`party`/`other`)에 넣는다. "받은 디버프" 개념은 필요 없다.
4. `buff-meta.json`의 `t`는 **아이콘 선택에만** 남긴다(`getStatusEffectIconAsset`).

### 미터기 측 (완료)

`type`을 payload에서 **제거**했다. 그룹 키는 `(baseCode, 이름, 시전자)`이고, 엔티티가 버프/디버프를 가른다.
**웹은 `type`이 앞으로 오지 않는다고 가정해도 된다.** DB의 `effect_type` 컬럼은 남겨둬도 무방(항상 null).
`baseCode`는 계속 온다.

---

## A-1. 【버그】 `passive`로 타이핑된 행을 통째로 버려서 실제 버프가 사라진다

`src/shared/buff-display.ts` — `groupStatusEffects()`

```ts
const effectType = getEffectType(row);
if (effectType === "passive") {
  continue;               // ← 행을 버린다
}
```

`getEffectType`은 payload의 `type`이 없으면 `getBuffMetadata(row.buffCode)?.t`로 폴백한다.
그리고 `src/shared/generated/buff-meta.json`에는:

```json
"13720000": { "b": 13720000, "t": "passive", "n": "빈틈 노리기" }
"13770000": { "b": 13770000, "t": "passive", "n": "기습 자세" }
"12770000": { "b": 12770000, "t": "passive", "n": "모욕의 포효" }
```

**결과**: 살성의 `빈틈 노리기`(가동률 82.8%), `기습 자세`(60.7%), 수호성의 `모욕의 포효`(70.7%)가
**미터기에는 보이는데 웹에서는 사라진다.**

`passive`는 데이터마인의 분류일 뿐이다. 그 행이 payload에 있다는 건 **전투 중 실제로 적용됐다**는 뜻이고,
가동률이 100%가 아니라 82.8%라는 것 자체가 시간에 따라 켜지고 꺼졌다는 증거다.

**수정**: `continue` 제거. `passive` 행도 버프 섹션에 그대로 렌더.
(A-0을 하면 `effectType`이 표시 경로에서 아예 빠지므로 자연히 해결된다.)

---

## A-2. 【버그】 `getSource`의 self 승격이 몹 디버프를 "내 버프"로 둔갑시킨다

`src/shared/buff-display.ts` — `getSource()`

```ts
if (
  source === "other" &&
  ownerJob && isAionJob(ownerJob) &&
  Math.floor(baseCode / 1_000_000) === JOB_SKILL_PREFIXES[ownerJob]   // ← 게이트가 없다
) {
  return "self";
}
```

`baseCode`의 앞 두 자리만 보므로, **직업 스킬이 아닌 8자리 코드도 통과**한다.

| 코드 | 정체 | `floor(baseCode/1e6)` | 결과 |
|---|---|---|---|
| `11800000` | 검성 `살기 파열` base (9자리 버프코드에서 유도) | 11 | self ✅ 의도대로 |
| `15003201` | **몹이 거는 `중독`** (`buff-meta`: `{t:"debuff", n:"중독"}`) | 15 | self ❌ **마도성의 "내 버프"로 표시** |
| `12000101` | **몹이 거는 `중독`** | 12 | self ❌ **수호성의 "내 버프"로 표시** |

**왜 구분되는가**: 9자리 직업 버프코드에서 유도한 base는 `floor(c/100000)*10000`이라 **항상 `0000`으로 끝난다**.
몹/소모품의 8자리 코드는 그렇지 않다.

**수정**: 승격 조건에 `baseCode % 10_000 === 0`을 추가.

```ts
if (
  source === "other" &&
  baseCode % 10_000 === 0 &&        // 9자리 직업 버프코드에서 유도된 base만
  ownerJob && isAionJob(ownerJob) &&
  Math.floor(baseCode / 1_000_000) === JOB_SKILL_PREFIXES[ownerJob]
) {
  return "self";
}
```

부수 효과 확인: 주문서(`22101051` → `%10000 = 1051`), 음식(`21610051` → `51`), 물약(`20111021` → `1021`)은
모두 승격되지 않는다(= 지금처럼 "그 외"). 정상.

> 참고: 미터기는 이미 `source`를 정확히 채워 보낸다(raw 9자리 job band 게이트). 이 승격 로직은 **과거 행 보정용**이다.

---

## A-3. 미터기 측 최종 상태 (완료 · 재검증됨)

| 항목 | 상태 |
|---|---|
| 업타임 그룹 키 = `(baseCode, 이름, 시전자)` · 구간 합집합 | ✅ |
| `BuffSource` = raw 9자리 job band 기준 (8자리 몹/폴백 코드 오분류 제거) | ✅ |
| payload에 `baseCode` 추가, `type`은 **보내지 않음**, `schemaVersion` 4 유지, `category` 대상 기준 유지 | ✅ |
| 미터 상세 패널: `내 버프 / 파티원 버프 / 그 외` (받은 디버프 섹션 없음) | ✅ |
| 테스트 504개 그린 + 실캡처 2건 리플레이 | ✅ |

**리플레이 결과**

| 캡처 | 결과 |
|---|---|
| 07-08 (5인 × 22전투) | 그룹 키 중복 0, source 오분류 0. `격앙` 2행 → **1행**(45.2%) |
| 07-09 (10인 공대 × 30전투, 버프행 2,949) | 그룹 키 중복 0, source 오분류 0. `살기 파열` 88행 → **31행**(전투당 1행). `격노 폭발` = 참가자 18행(자기 버프) / 보스 17행(대상 디버프)으로 엔티티 분리. `지연 피해` base 5종 유지 |

---

## A-4. 검수용 실측 자료

| 확인 항목 | 기대 |
|---|---|
| `빈틈 노리기` / `기습 자세` (살성) | 버프 탭에 **보여야** 함 (현재 A-1로 사라짐) |
| `모욕의 포효` (수호성) | 버프 탭에 **보여야** 함 |
| `중독` (몹이 건 것) | "그 외"에 뜨고 **"내 버프"가 아니어야** 함 (현재 A-2로 둔갑) |
| `살기 파열` (검성) | 버프 탭에 **1행** (신규 업로드는 미터기가 이미 병합해서 보냄) |
| `격노 폭발` (검성) | 버프 탭 1행(자기 버프) + 보스 디버프 탭 1행(대상 디버프) |
| `지연 피해` (정령성) | 보스 디버프 탭에 **2행 유지** (base `16300000` / `16330000` — 다른 스킬) |
| `4원소` vs `피해 내성 감소` | 같은 base `16300000`이지만 **분리 유지** (이름이 다름) |
