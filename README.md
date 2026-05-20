# waffle_meter.v1.3

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

2. [Releases](https://github.com/Waffle-ens/waffle_meter/releases)에서 최신 `waffle_meter.v1.3-x.x.x.msi` 파일을 다운로드해 설치합니다.

3. 아이온2가 실행 중이라면 먼저 캐릭터 선택창으로 이동합니다.

4. 시작 메뉴 또는 설치 폴더의 `waffle_meter.v1.3.exe`를 관리자 권한으로 실행합니다.
   기본 설치 경로는 `C:\Program Files\waffle_meter.v1.3`입니다.

5. 미터기가 보이면 아이온2에 접속해 전투를 시작합니다.
   데미지 기록, 전투 기록, 파티 신청 패널은 인게임 오버레이 형태로 표시됩니다.

6. 미터기가 보이지 않거나 위치가 이상하면 앱을 종료한 뒤 `%APPDATA%\waffle_meter.v1.3\settings.properties`에서 `windowX`, `windowY`, `uiX`, `uiY` 값을 `0`으로 수정한 다음 다시 실행합니다.

7. 업데이트 알림이 표시되면 앱 안에서 업데이트 파일을 다운로드하거나, [Releases](https://github.com/Waffle-ens/waffle_meter/releases)에서 최신 MSI를 직접 설치합니다.

## 업데이트 기록

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
