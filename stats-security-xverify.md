# 상호 검증 — 서명 1건 맞추기 (미터기 ↔ 통계웹)

설계도 `design-security-anchor.md` §2.1 서명 규약의 **양 런타임(.NET ↔ Node) 바이트/암호 패리티**를
실측으로 맞춘 결과 + 통계웹 `verify-signature.ts`(W1) 구현 시 그대로 쓸 수 있는 **고정 테스트 벡터**.

## 결과 (양방향 ✅)

같은 키·같은 입력으로:

| 검사 | 결과 |
|---|---|
| `base64(sha256(UTF-8 body))` .NET == Node (한글 닉/직업 포함) | ✅ 일치 |
| canonicalString 재구성 .NET == Node (LF 구분, 바이트 일치) | ✅ 일치 |
| 미터(.NET) 서명을 서버(Node)가 SPKI 공개키로 검증 | ✅ true |
| 서버(Node) 서명을 미터(.NET)가 검증 (역방향 왕복) | ✅ true |

DER 인코딩(.NET `DSASignatureFormat.Rfc3279DerSequence` ↔ Node `dsaEncoding:'der'`), base64,
SPKI 공개키, canonical 바이트가 모두 일치한다. ECDSA 서명 자체는 비결정적(random k)이므로 **서명은
바이트 비교가 아니라 검증으로 맞춘다** — 재서명하면 바이트는 달라지지만 항상 검증된다.

## 고정 테스트 벡터

미터 측 입력(§2.1):

```
METHOD     = POST
PATH       = /api/v1/consent/events        (쿼리스트링 제외)
InstallId  = 11111111-1111-1111-1111-111111111111
Timestamp  = 1700000000000
Nonce      = abcdEFGH1234_-xyz             (base64url, 요청마다 고유)
body       = {"consentState":"accepted","consentVersion":"2026-06-04","character":{"identityHash":"deadbeef","nickname":"와플","server":3,"public":true,"job":"마도성","power":5000}}
```

파생값:

```
base64(sha256(UTF-8 body)) = PBmT1LdJyN4XlU7r19tNhAbEMs80R+E9fKRHOkoMvzs=

canonicalString (UTF-8, '\n' = LF 0x0A):
POST\n/api/v1/consent/events\n11111111-1111-1111-1111-111111111111\n1700000000000\nabcdEFGH1234_-xyz\nPBmT1LdJyN4XlU7r19tNhAbEMs80R+E9fKRHOkoMvzs=

canonical UTF-8 hex:
504F53540A2F6170692F76312F636F6E73656E742F6576656E74730A31313131313131312D313131312D313131312D313131312D3131313131313131313131310A313730303030303030303030300A6162636445464748313233345F2D78797A0A50426D54314C644A794E34586C5537723139744E684162454D733830522B4539664B52484F6B6F4D767A733D
```

키쌍(P-256, 이 벡터 전용 — 운영 키 아님):

```
X-WM-Install-Key (SPKI, base64):
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEZtUcEJeV1dSQKxxuSnvzQExUTjeolkJjOS5WhyJKX2IxSNub3cvc+FVGbEKMM3x0SIO7OoZXB+FuGYK/Bc9m+A==

PKCS#8 private (base64, 벡터 재현용):
MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgwpt1yY0V9XViTtAMY4GJ46COfr6XKsXSWTh9hk2gfe2hRANCAARm1RwQl5XV1JArHG5Ke/NATFRON6iWQmM5LlaHIkpfYjFI25vdy9z4VUZsQowzfHRIg7s6hlcH4W4Zgr8Fz2b4

X-WM-Signature (DER ECDSA-P256-SHA256, base64 — 유효 샘플 1건; 검증으로 확인):
MEYCIQDb4pf6Y39gFEaDL9fRnTMWx+JpC5iSYHQj4iIVeo4z0QIhAIJKqjkZGe811+aGo89MUwqe7qjwyEVmAMFtTfwOm3ct
```

## 통계웹 검증 스니펫 (Node, raw crypto)

`verify-signature.ts`가 해야 할 핵심:

```js
const crypto = require('crypto');
// rawBody = 수신한 요청 본문의 "바이트 그대로" (재직렬화/parse-then-stringify 금지!)
const bodyHash = crypto.createHash('sha256').update(rawBody).digest('base64');
const canonical = [method, path, installId, timestamp, nonce, bodyHash].join('\n'); // path = 쿼리 제외
const pub = crypto.createPublicKey({ key: Buffer.from(installKeyB64, 'base64'), format: 'der', type: 'spki' });
const ok = crypto.verify('sha256', Buffer.from(canonical, 'utf8'),
  { key: pub, dsaEncoding: 'der' }, Buffer.from(signatureB64, 'base64'));
```

위 벡터를 넣으면 `ok === true`.

> ⚠️ **서버 함정:** `base64(sha256(rawBody))`는 **수신 원문 바이트**로 계산해야 한다. JSON을
> parse→stringify 하면 키 순서/공백/이스케이프가 달라져 해시가 어긋나고 서명이 전부 실패한다.
> Next.js route에서 `await request.text()`(또는 raw body)로 받아 그 문자열의 UTF-8 바이트를 해시할 것.

## 통계웹과 확정 필요한 2개 (계약서 §2엔 미고정 — 미터는 상위호환으로 무해)

1. **grant 수신 필드명 = `granted`** (boolean). 미터는 `POST /reports` 응답과
   `POST /consent/events` 응답에서 `granted`를 읽어 공개 토글을 잠금해제한다. 서버가 안 보내면
   기본 false(=상위호환, 토글은 잠긴 채). **보안 게이트가 아니라 UX 신호** — 공개 허용 판정은
   서버 grant가 최종이다. 통계웹이 이 필드명으로 내려주면 맞는다(다른 이름이면 알려달라).

2. **`public_requires_ownership` 거부 봉투.** 미터는 응답 본문에 `public_requires_ownership`
   문자열이 있으면 거부로 인식(현 통계웹 에러 형식 `{ok:false,error:{code,message}}` + 시퀀스
   다이어그램의 `400`과 정합). HTTP 코드(400)는 무관하게 본문 코드로 감지하므로 견고. 코드 문자열만
   `public_requires_ownership`로 유지하면 된다.

위 둘은 SHARED CONTRACT §2 본문을 바꾸지 않았다(가드레일 준수) — §3.3 수신 필드 + §2.4 거부 코드
범위의 구현 선택이다. 변경 필요 시 양 레포 §2 동기화.
