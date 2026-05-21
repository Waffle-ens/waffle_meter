# waffle_meter.v1.4

아이온2 전투분석을 위한 미터기 프로젝트

[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub Issues](https://img.shields.io/github/issues/Waffle-ens/waffle_meter)](https://github.com/Waffle-ens/waffle_meter/issues)
[![GitHub Pull Requests](https://img.shields.io/github/issues-pr/Waffle-ens/waffle_meter)](https://github.com/Waffle-ens/waffle_meter/pulls)

- Spoqa Han Sans Neo, Freesentation, Pretendard (OFL 1.1)
- NEXON Lv2 Gothic, Tmoney Round Wind (Custom License)

해당 프로젝트는 아이온2 운영측의 요청, 패킷암호화등의 조치, 공식적인 사용중단 언급이 있다면 중지 및 비공개상태로 전환됩니다.

## 사용법

1. [Npcap](https://npcap.com/#download)을 설치합니다.
   설치 중 `Install Npcap in WinPcap API-compatible Mode` 옵션을 반드시 체크합니다.

2. [Releases](https://github.com/Waffle-ens/waffle_meter/releases)에서 최신 `waffle_meter.v1.4-x.x.x.msi` 파일을 다운로드해 설치합니다.

3. 아이온2가 실행 중이라면 먼저 캐릭터 선택창으로 이동합니다.

4. 시작 메뉴 또는 설치 폴더의 `waffle_meter.v1.4.exe`를 관리자 권한으로 실행합니다.
   기본 설치 경로는 `C:\Program Files\waffle_meter.v1.4`입니다.

5. 미터기가 보이면 아이온2에 접속해 전투를 시작합니다.
   데미지 기록, 전투 기록, 파티 신청 패널은 인게임 오버레이 형태로 표시됩니다.

6. 미터기가 보이지 않거나 위치가 이상하면 앱을 종료한 뒤 `%APPDATA%\waffle_meter.v1.4\settings.properties`에서 `windowX`, `windowY`, `uiX`, `uiY` 값을 `0`으로 수정한 다음 다시 실행합니다.

7. 업데이트 알림이 표시되면 앱 안에서 업데이트 파일을 다운로드하거나, [Releases](https://github.com/Waffle-ens/waffle_meter/releases)에서 최신 MSI를 직접 설치합니다.

## 업데이트 기록

- v1.4.6
  - 로타르, 롭스티노, 크로메데의 심연 보스 코드를 추가해 해당 던전 전투가 미터기에 잡히지 않던 문제를 수정했습니다.
  - 설정 패널에 패킷 진단 로그 시작/종료 기능을 추가해 특정 던전/패킷 파서 문제를 추적할 수 있도록 했습니다.

- v1.4.5
  - 메인 미터기 목록의 누적 피해량 표시 칸을 전투력 표시로 변경했습니다.
  - DPS 단위를 `숫자/초`에서 `숫자/s` 형식으로 정리했습니다.
  - 파티 신청 카드와 메인 목록의 전투력 표기 포맷을 동일하게 맞췄습니다.

- v1.4.4
  - 트레이 아이콘과 설치 패키지 아이콘에 사용하는 와플 로고 리소스를 새 파일로 교체했습니다.
  - 앱 내부 기본 버전 값을 최신 릴리스 기준으로 정리했습니다.

- v1.4.3
  - 보스 HP 패킷에서 관측된 최고 HP를 최대 HP로 누적 저장하도록 수정했습니다.
  - 같은 몹 인스턴스 정보가 다시 저장될 때 기존 최대 HP가 초기화되지 않도록 보강했습니다.
  - 전투 종료 직전 마지막 HP 정보를 전투 리포트에 반영해 타겟 HP가 `0 / 0%`로 표시되는 문제를 수정했습니다.

- v1.4.2
  - 타겟 인식 실패 상태에서 임시 숫자 유저가 미터 목록에 표시되지 않도록 필터링했습니다.
  - 소환수/정령 계열 피해가 가능한 경우 실제 소유자에게 귀속되도록 보강했습니다.
  - 지속 피해 효과 코드가 실제 스킬명/아이콘으로 표시되도록 스킬 코드 정규화를 개선했습니다.
  - 상세내역 버프 가동률에서 내부 더미 버프를 숨기고, 효과 코드는 실제 스킬명/아이콘으로 보정했습니다.

- v1.4.1
  - 설정에서 다중 모니터 이동 모드를 켜고 끌 수 있도록 추가했습니다.
  - 다중 모니터 이동 모드에서 보조 모니터 영역까지 미터기/패널을 이동할 수 있도록 오버레이 범위를 확장했습니다.
  - 모드 전환 시 기존 위치가 튀지 않도록 좌표 보정을 추가했습니다.

- v1.4.0
  - 미터기 UI를 새 디자인으로 정리했습니다.
  - 트레이/설치 아이콘과 스킬/버프 아이콘 리소스를 보강했습니다.
  - 상세내역 미리보기의 스킬명/스킬 아이콘 표시를 실제 데이터 구조에 맞게 수정했습니다.

- v1.3.2
  - 자동 숨김 상태에서 투명 오버레이가 작업표시줄 위에 남지 않도록 보강했습니다.
  - 아이온2 포커스가 아닐 때 오버레이의 항상 위 상태를 해제하고 뒤로 보내도록 조정했습니다.

- v1.3.1
  - 업데이트 창 헤더의 투명도 조절 UI를 제거하고 닫기 버튼을 고정 배치했습니다.
  - 릴리스 MSI 파일명과 앱 표시명을 `waffle_meter.v1.3` 기준으로 정리했습니다.

- v1.3.0
  - 화이트 모드 상세내역 대비를 개선했습니다.
  - 트레이/설치 아이콘과 스킬 아이콘 리소스를 보강했습니다.
  - 미터 투명도 설정을 하나로 통합했습니다.
