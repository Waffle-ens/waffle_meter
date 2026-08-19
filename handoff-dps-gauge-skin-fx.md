# Handoff: WPF DPS 게이지 스킨 5종 장식 이펙트

마지막 확인: 2026-08-20, branch `dev`, HEAD `33d93fd`

## 목표

현재 dev에 등록된 DPS 게이지 스킨 5종을 이름에 맞는 서로 다른 모션으로 보이게 한다.

1. `berryglaze` / 베리 글레이즈 / 후원자
2. `matcha` / 말차 크림 / 후원자
3. `prism` / 프리즘 / 랭커
4. `ember` / 잔불 / 랭커
5. `frost` / 서리 / 랭커

완성 화면은 현재 설정창의 실제 미터 행 모형을 그대로 유지해야 한다. 기존 색 채움 위에만 투명 장식층을 얹고, 직업 아이콘·티어 링·순위 칩·수치·3px accent rail은 바꾸지 않는다.

Codex 모션 시안:

- `C:\Users\Waffle\.codex\visualizations\2026\08\19\01a01b44-2c6f-7bd3-8508-a046257d9442\dps-gauge-five-skin-study.html`
- 기준 이미지: `C:\Users\Waffle\AppData\Local\Temp\codex-clipboard-0b855273-e299-4824-a63b-6365b95c4c88.png`

## 반드시 지킬 불변 조건

### 1. 기존 `GaugeBrush`를 교체하지 않는다

장식 순서는 반드시 다음과 같다.

```text
기존 GaugeBrush 색 채움 (현재 opacity 0.58)
→ 투명 스킨별 장식 레이어
→ 닉네임·직업 아이콘·DPS·퍼센트 등 기존 foreground
```

`23ca728`의 `NameFxGaugeArt.cs`를 그대로 되살리거나 cherry-pick하지 않는다. 그 구현은 색 채움 전체를 도형 타일로 교체했고, DPS 숫자의 바탕이 지나치게 복잡해져 `4b9dae4`에서 되돌려졌다. 참고할 것은 geometry 제작법, 타일 래핑, 캐싱, UiPreview 검증 아이디어뿐이다.

### 2. 직업 아이콘은 데이터와 픽셀 모두 스킨과 독립이어야 한다

다음 코드는 수정하지 않는다.

- `dotnet/src/WaffleMeter.App.Wpf/OverlayWindow.xaml`의 직업 아이콘 Border/Image(현재 약 293~318)
- `Image Source="{Binding IconSource}"`
- `Skin.StatBg`, `Skin.IconRing`, `Tier.IconRing`, `Tier.InnerRing`
- `dotnet/src/WaffleMeter.App.Wpf/OverlayViewModel.cs`의 `IconSource: JoinIcons.Job(jobName)`
- `dotnet/src/WaffleMeter.App.Wpf/SettingsWindow.xaml`의 샘플 직업 아이콘 Border/Image(현재 약 859~862)
- `SettingsViewModel.cs`의 `JoinIcons.Job("마도성")`

스킨 ID를 `IconSource`, 아이콘 `Background`, `BorderBrush`, `Opacity`, `Effect`, `RenderTransform`에 연결하지 않는다.

새 장식 레이어는 foreground Grid 아래에 둔다. 여기에 더해 아이콘 박스가 반투명 테마에서도 장식에 물들지 않도록, 실제 행과 설정 샘플 모두 장식의 왼쪽 약 70~74 DIP(rail + rank + icon 구간)를 그리지 않는 보호 구간으로 둔다. 가장 단순한 방법은 `GaugeFxLayer`에 `ContentExclusionLeft`를 두고 해당 영역을 clip에서 빼는 것이다. 기존 GaugeBrush는 그대로 뒤에 남으므로 행 색은 끊기지 않는다.

### 3. accent rail과 얇은 bar는 그대로 둔다

- 왼쪽 3px rail은 계속 `FillBrush`/직업색을 사용한다. 게이지 스킨이나 장식 ID를 연결하지 않는다.
- `BarStyle=bar`의 3px bottom bar에는 입자를 넣지 않는다. 현재 `GaugeBrush`만 유지한다.
- `BarStyle=none`이면 장식도 없어야 한다.

### 4. 행 기하를 바꾸지 않는다

`OverlayWindow`는 `SizeToContent=Height` 회귀 이력이 있다. 새 레이어는 measure에 영향을 주지 않는 단일 `FrameworkElement`여야 한다. 기존 Height, Padding, Margin, CornerRadius, 열 정의를 바꾸지 않는다.

## 스킨별 시각 사양

현재 `NameFxPalette.cs`의 dark/light 그라디언트와 밝기 슬라이더 동작은 그대로 보존한다.

### `berryglaze` — 베리 글레이즈

- 인상: 젖은 베리 래커, 점성이 있는 광택과 천천히 맺히는 방울.
- 큰 형태: 높이 5~8 DIP, 폭 70~110 DIP의 굽은 Bézier 광택 리본 1개. 상단 1/3에서 좌→우, 약 6.4초.
- 작은 형태: 눈물방울 5~6개. 폭 3~5 DIP, 길이 6~12 DIP. 잠시 길어졌다가 아래로 8~16 DIP 흘러내리며 사라진다. 생애 3.4~5.8초.
- 색: body `#F0529A`, rim `#FF9BCB`, gloss `#FFF0F7`, shadow `#641035`의 저알파 파생색.
- 금지: 별가루, 빠른 스파크, 날카로운 파편. 프리즘이나 잔불처럼 보이면 실패다.

### `matcha` — 말차 크림

- 인상: 매트한 녹차 위에서 크림이 천천히 접히는 라테아트.
- 큰 형태: 폭 28~48 DIP, 높이 7~13 DIP의 둥근 크림 마블 3~4덩어리. 오른쪽→왼쪽으로 서로 다른 9.6초/13.2초 층 두 개.
- 작은 형태: 외곽선 위주의 폼 링 3~4개. 지름 3~6 DIP, 4.8~7.6초 동안 6~12 DIP 떠오르고 3~6 DIP 옆으로 흐른다.
- 색: tea `#78BE79`, foam `#DCEFCB`, warm cream `#FFF3D2`, deep `#173F29`의 저알파 파생색.
- 금지: 각진 흰 판, 파란 glow, 회전 결정. 서리와 색 없이도 구분되어야 한다.

### `prism` — 프리즘

- 인상: 정돈된 굴절광, 유리면, 스펙트럼 리본.
- 대각 굴절띠 1~2개가 약 3.8초에 통과한다.
- 삼각형/마름모 유리 파편 4~7개가 천천히 회전한다.
- 무작위 원형 입자는 최소화한다. 서리보다 빠르고 구조적이어야 한다.
- 전체 막대가 반복 점멸하면 안 된다.

### `ember` — 잔불

- 인상: 하단의 잉걸에서 작은 불씨가 위로 흩날린다.
- 밝은 불씨 6~7개 + 흐린 재 3~4개. 생애 1.4~2.8초.
- 아래→위로 이동하며 좌우로 2~6 DIP 흔들리고 작아지며 사라진다.
- 둥근 core, 작은 마름모, 짧은 주황 궤적을 섞는다.
- 막대 전체를 불꽃 그림으로 채우거나 전체 opacity를 맥동시키지 않는다.

### `frost` — 서리

- 인상: 얼음 결정과 각진 파편이 비스듬히 회전하며 휘날린다.
- 결정/파편 5~8개, 생애 3.6~6.8초. 잔불보다 크고 느리다.
- 큰 6갈래 결정은 동시에 1~2개만 보이게 한다. 나머지는 4각/비대칭 마름모 파편.
- 상단에 얇고 불규칙한 서릿발 능선을 둘 수 있다.
- 큰 육각형은 보석처럼, 가는 바늘 반복은 빗살처럼 읽혔던 이전 실패를 반복하지 않는다.

## 권장 구현 구조

### A. ID를 view model까지 명시적으로 전달

표시명이나 Brush 참조로 스킨을 추론하지 않는다.

1. `GaugeSkinSampleViewModel`에 `string Id` 추가.
2. `SettingsViewModel.RebuildNameFxSamples()`에서 `Id: e.Id` 전달.
3. `RowViewModel` 끝에 `string? GaugeSkinId` 추가.
4. `OverlayViewModel.Update()`에서 `GaugeBrush(gid, ...)`가 실제로 성공했을 때만 `GaugeSkinId: gid`, 그 외에는 null.
5. `GaugeFxEnabled`(또는 동등한 bool)를 별도로 두고 `NameFxMode=animated`, low-spec 아님, `BarStyle=fill`일 때만 true로 한다.

현재 `nameFxOn`, 표시 범위, `NameFxGauge`, unknown ID 필터를 모두 통과한 경우에만 ID가 행까지 가야 한다. `off`, 미부여, unknown에서는 장식이 0이어야 한다. `static`은 유효한 ID와 기존 색 채움은 유지하지만 `GaugeFxEnabled=false`여야 한다.

### B. 단일 WPF 렌더 표면

새 파일 예시:

- `dotnet/src/WaffleMeter.App.Wpf/GaugeFxLayer.cs`
- 필요하면 clock을 같은 파일의 내부 static class로 둔다.

`GaugeFxLayer : FrameworkElement` 하나가 `OnRender(DrawingContext)`에서 그린다.

- DependencyProperty: `SkinId`, `IsFxEnabled`, `ContentExclusionLeft` 정도면 충분하다.
- `IsHitTestVisible=false`.
- 자체 `OnRender`에서 `PushClip`으로 현재 fill 사각형과 둥근 모서리를 자른다.
- `ContentExclusionLeft`보다 왼쪽에는 장식 geometry를 그리지 않는다.
- 입자마다 `Ellipse`, `Path`, `Canvas`, Storyboard를 만들지 않는다.
- Brush, Pen, StreamGeometry는 스킨/테마/밝기별로 캐시하고 `Freeze()`한다.
- glow는 Blur/DropShadow가 아니라 큰 저알파 도형 + 작은 밝은 core 2~3겹으로 흉내 낸다.
- 프레임마다 컬렉션, Brush, Pen, Geometry를 할당하지 않는다.

### C. XAML 레이어 위치

실제 행 `OverlayWindow.xaml`의 비례 fill Grid에서 현재 단일 Border를 아래처럼 감싼다. 아래는 형태 예시이며 기존 수치/바인딩은 그대로 옮긴다.

```xml
<Grid Grid.Column="0" ClipToBounds="True">
    <Border Background="{Binding GaugeBrush}"
            Opacity="{Binding GaugeOpacity}"
            CornerRadius="4"/>
    <local:GaugeFxLayer SkinId="{Binding GaugeSkinId}"
                        IsFxEnabled="{Binding GaugeFxEnabled}"
                        ContentExclusionLeft="72"
                        IsHitTestVisible="False"/>
</Grid>
```

이 nested Grid는 기존 foreground content Grid보다 먼저 선언되어야 한다. 직업 아이콘 쪽 XAML은 한 줄도 옮기거나 수정하지 않는다.

`SettingsWindow.xaml`의 샘플 fill도 동일한 `GaugeFxLayer`를 사용해야 한다. 시안과 실제 미터가 다른 렌더러를 쓰면 다시 어긋난다.

### D. 전역 clock과 모드

행 DataTemplate 안 Storyboard는 금지한다. `OverlayViewModel.Update()`가 행을 계속 교체하므로 매 tick 재시작되어 끊긴다.

- process-wide clock 1개.
- `CompositionTarget.Rendering`을 쓰되 20~24fps로 throttle한다. 기존 30fps 상한을 재사용해도 되지만 먼저 24fps를 측정한다.
- 위치는 절대 `Stopwatch` 시간 + 스킨 ID + 안정적 행 seed로 계산한다. 행 재생성 시 phase가 0으로 돌아가면 안 된다.
- 화면에 active layer가 없으면 unsubscribe/정지한다.
- `OverlayController`의 parked 상태, `LowSpecMode`, 설정 미리보기 demand, `NameFxMode`, `NameFxSpeedPercent`를 기존 `NameFxSheen`과 같은 gate로 연결한다.
- `animated`: 기존 색 채움 위에서 새 장식이 움직인다.
- `static`: UI 문구 그대로 “움직임 없이 색만” 남긴다. 기존 GaugeBrush만 보이고 새 장식 geometry는 그리지 않는다.
- `off`/unknown/게이지 토글 off: 장식 없음.
- `LowSpecMode`: 현재 설정과 무관하게 새 장식을 숨기고 clock을 정지한다. 기존 GaugeBrush의 정지 색은 유지한다.
- 설정 미리보기에서만 실제 행이 없어도 clock demand가 살아야 한다.

기존 `NameFxSheen`의 base gradient는 첫 구현에서 그대로 둔다. 장식층이 안정된 뒤에만 스킨별 base 속도를 미세 조정한다. 두 시스템을 한 번에 다시 짜지 않는다.

## 설정 미리보기 계약

현재 dev의 `NameFxPalette.GaugeSkins` 선언 순서를 그대로 열거한다. 5행을 별도 하드코딩하지 않는다.

```text
berryglaze / 베리 글레이즈 / 후원자
matcha      / 말차 크림     / 후원자
prism       / 프리즘        / 랭커
ember       / 잔불          / 랭커
frost       / 서리          / 랭커
```

각 행의 비교 변수는 스킨 하나뿐이어야 한다.

- Rank `1`
- 직업 아이콘 `JoinIcons.Job("마도성")`
- 닉네임 `와플장인`
- 서버 `[시엘]`
- 전투력 `656.0k`
- DPS `408,239/s`
- 비율 `35.1%`
- `BarRatio=.55`, `BarRest=.45`
- `GaugeOpacity=.58`
- rail은 현재 user bar gradient

## UiPreview와 테스트

기존 명령:

```powershell
dotnet build dotnet\tools\UiPreview\UiPreview.csproj -c Release --no-restore
dotnet run --project dotnet\tools\UiPreview\UiPreview.csproj -c Release --no-build
```

현재 기준선은 설정 검증 189/189, 탭 계약 20/20 통과다.

추가할 검증:

1. `GaugeSkinSamples.Select(Id)`가 `NameFxPalette.GaugeSkins.Select(Id)`와 순서까지 동일.
2. 정확히 5종, 후원자 2종, 랭커 3종.
3. 실제 행: 유효한 gauge ID일 때만 `GaugeSkinId` 존재.
4. `NameFxGauge=false`, `NameFxMode=off`, unknown ID, 적용 대상 제외 시 ID/장식 없음.
5. `static`/low-spec에서 새 장식 픽셀이 0이고 clock이 정지함. 기존 GaugeBrush 색은 유지됨.
6. `animated`에서 phase 0.00/0.33/0.66 렌더가 실제 픽셀로 다름.
7. 루프 끝과 시작의 평균 채널 차 < 1/255. 타일을 쓰지 않는 절대시간 입자라면 생성/소멸 경계에서 개체 수가 튀지 않는지 별도 검사.
8. 16%/55%/100% bar 길이에서 오른쪽 경계 밖으로 장식이 새지 않음.
9. row height/scale 75%/100%/130%, dark/light 모두 확인.
10. icon `Image.Source`와 아이콘 foreground XAML에 skin binding이 전혀 추가되지 않았음을 검증.
11. 아이콘 보호 구간 안의 새 장식 layer 알파가 항상 0임을 렌더 테스트.
12. 3px bottom bar와 accent rail은 도입 전 결과 유지.
13. 효과 없음/있음 overlay frame cost를 기존 `MeasureOverlayFrame`으로 비교하고 결과를 기록.

전용 캡처를 추가한다.

- `gauge_sheet_Dark.png`
- `gauge_sheet_Light.png`
- 5종을 같은 bar 길이, 같은 phase로 배치.
- 가능하면 phase 0.00/0.33/0.66 세 열을 한 장에 보여 준다.
- 설정창 실제 5행 미리보기와 실제 `OverlayWindow` 캡처도 dark/light 각각 남긴다.

주의: `%TEMP%\waffle_ui_preview`는 실행 전에 비우지 않으므로 오래된 `gauge_sheet_*`가 남아 있을 수 있다. 파일 timestamp와 이번 실행 로그로 새 캡처인지 확인한다.

## 완료 조건

- 다섯 스킨을 색을 보지 않고도 형태와 움직임으로 구분할 수 있다.
- 기존 GaugeBrush 색 채움과 DPS 가독성이 유지된다.
- 직업 아이콘과 ring은 스킨에 관계없이 완전히 동일하다.
- 설정 미리보기와 실제 미터가 동일한 renderer를 사용한다.
- 숨김/park/idle/off/low-spec에서 불필요한 clock이 돌지 않는다.
- Dark/Light 캡처, 세 phase 시트, 빌드, 기존 전체 UiPreview 검증, 새 검증이 모두 통과한다.

## 하지 말 것

- WebView/WebView2 도입
- `23ca728` 전체 복원 또는 cherry-pick
- GaugeBrush를 DrawingBrush 도형으로 교체
- 직업 아이콘/티어 링/rail에 skin ID 연결
- 입자별 UIElement/Storyboard
- BlurEffect/DropShadowEffect/OpacityMask 남발
- 60fps 상시 렌더
- 행 재생성 때마다 random seed/phase 초기화
- 설정 샘플과 실제 미터에 서로 다른 효과 구현
