# 모듈러 총기 텍스처 구조 규칙

모든 모듈러 총기 텍스처는 `Textures/Weapons/Modular` 아래에서 파츠 역할별로 분리한다. 루트 폴더에는 이미지 파일을 직접 두지 않는다.

## 폴더 구조

| 폴더 | 용도 | 현재 예시 |
|---|---|---|
| `Receivers` | 하부·상부 총몸 | M16A2/3 lower, M4A1 upper, M16A4 upper |
| `Barrels` | 총열 | AR-15 10.3인치, 20인치 |
| `GasSystems` | 가스 블록·가스 계통 | MK12 LP, AR-15 전방 가늠쇠 가스 블록 |
| `Handguards` | 핸드가드와 조립식 하부 확장 | HAC RIS, HAC URX upper/lower |
| `MuzzleDevices` | 총구 장치와 소음기 | HAC NT4, BrightStrike suppressor |
| `Stocks` | 버퍼 튜브와 개머리판 | Colt carbine/A2 tube, MOE/A2 stock |
| `Grips` | 권총손잡이와 수직손잡이 | IronFang A2, Dalton/HAC VFG |
| `Magazines` | 탄창과 탄창 확장 파츠 | STANAG, pull grip |
| `Optics` | 조준기·배율기·조준기 마운트 | EXPS, G33, TA51, TA11 |
| `Tactical` | 레이저와 플래시라이트 | AN/PEQ-15, M600 |
| `RailAccessories` | 레일 커버 등 비기능성 레일 파츠 | HAC URX lower/side panel |
| `_Backups` | 원본 편집 백업 | `*.png~`; 게임 설치본에는 배포하지 않음 |

## 파일 규칙

- 기본 텍스처와 외곽선은 항상 같은 폴더에 둔다.
- 외곽선 파일명은 기본 파일명 뒤에 정확히 `_Outline`을 붙인다.
- Def의 `texPath`에는 확장자를 쓰지 않는다.
- 새 파츠는 1024×1024 공통 총기 캔버스를 유지한다.
- 같은 파츠의 기본/Outline 캔버스 크기와 위치는 완전히 같아야 한다.
- 파일을 다른 역할 폴더로 옮길 때 해당 `ThingDef.graphicData.texPath`도 동시에 변경한다.
- `_Backups`는 소스 보존용이며 RimWorld 설치본으로 복사하지 않는다.

## 신규 M16A4 개발 세트

신규 Def는 `Defs/ModernWar/Items/ModularWeapons_M16A4_Development.xml`에 분리했다.

- M16A4 상부 총몸
- 20인치 AR-15 총열
- AR-15 전방 가늠쇠 가스 블록
- HAC URX 상부·하부 핸드가드
- STANAG 탄창과 탄창 당김 손잡이
- Colt A2 버퍼 튜브와 IronFang A2 개머리판
- Lumicon TA51 레일 마운트와 TA11 조준경
- HAC 수직손잡이
- HAC URX 하부·측면 레일 패널

`HD_Gun_ModularM16A4_Test_Weapon`은 위 신규 파츠 14종을 전부 포함하는 좌표 편집용 기본 트리다. 텍스처 캔버스 변경 후 M4와 M16A4 세트의 위치 오프셋은 모두 0으로 초기화했다. 인게임 편집기에서 중심점과 부착점을 정밀 조정한 뒤 새 XML 출력값을 Def에 반영한다.
