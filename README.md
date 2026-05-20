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
