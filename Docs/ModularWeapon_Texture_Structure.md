# 모듈러 총기 텍스처 구조 규칙

모든 모듈러 총기 텍스처는 `Textures/Weapons/Modular` 아래에서 파츠 역할별로 분리한다. 루트 폴더에는 이미지 파일을 직접 두지 않는다.

## 폴더 구조

| 폴더 | 용도 | 현재 예시 |
|---|---|---|
| `Receivers` | 하부·상부 총몸 | M16A2/3 lower, M16A4 upper |
| `Barrels` | 총열 | AR-15 10.3인치, 14.5인치, 20인치 |
| `GasSystems` | 가스 블록·가스 계통 | MK12 LP, AR-15 전방 가늠쇠 가스 블록 |
| `Handguards` | 핸드가드와 조립식 하부 확장 | HAC RIS, HAC URX/M4 URX upper/lower |
| `MuzzleDevices` | 총구 장치와 소음기 | HAC NT4, BrightStrike suppressor |
| `Stocks` | 버퍼 튜브와 개머리판 | Colt carbine/A2 tube, MOE/M4/A2 stock |
| `Grips` | 권총손잡이와 수직손잡이 | IronFang A2, Dalton/HAC VFG |
| `Magazines` | 탄창과 탄창 확장 파츠 | STANAG, pull grip |
| `Optics` | 조준기·배율기·조준기 마운트 | EXPS, G33, TA51, TA11 |
| `Tactical` | 레이저와 플래시라이트 | AN/PEQ-15, IZLID Ultra, M600 |
| `RailAccessories` | 레일 커버 등 비기능성 레일 파츠 | HAC URX short/lower/side panel |
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

`HD_Gun_ModularM16A4_Test_Weapon`은 위 신규 파츠 14종을 전부 포함하는 좌표 편집용 기본 트리다. M4A1/M16A4 상부 총몸 Def는 M16A4 상부 텍스처를 공유하며, 최신 M4 출력의 상부 리시버 마운트·총열·핸드가드·상부 레일 좌표를 함께 사용한다. 인게임 편집기에서 중심점과 부착점을 정밀 조정한 뒤 새 XML 출력값을 Def에 반영한다.

추가 M4 파츠인 14.5인치 총열, M4 URX 상·하부 핸드가드, 짧은 레일 패널, M4 개머리판은 공용 개발 Def 파일에 등록되어 M4/M16 계열의 호환 소켓 카탈로그에서 선택할 수 있다.

## M16A1/A2 레거시 개발 세트

`Defs/ModernWar/Items/ModularWeapons_M16Legacy_Development.xml`에 다음 파츠와 편집용 조립체를 분리했다.

- M16A1 하부·상부 총몸과 삼각형 핸드가드
- M16A2 상부 총몸과 상·하 분할 핸드가드
- 20인치 pencil-profile 총열
- 20발 STANAG 탄창
- `HD_Gun_ModularM16A1_Test_Weapon`
- `HD_Gun_ModularM16A2_Test_Weapon`

레거시 파츠의 초깃값은 기존 M16A4 조립체의 하부 총몸 인터페이스와 공용 AR-15 결합 규격을 사용한다. 이후 각 완성 조립체를 소환해 편집기 저장본으로 정밀 좌표를 갱신한다.
