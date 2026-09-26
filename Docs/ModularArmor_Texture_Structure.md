# 방어구 부품 텍스처 폴더

기존 개별 PNG를 원래 의상 폴더 안의 부품별 하위 폴더로 정리합니다. 파일명과 이미지 내용은 유지하며, 이미지 합성이나 시트 분할 로더를 사용하지 않습니다.

IBTV 폴더 `Textures/Helod/Apparel/HD_IOTVGen4Assault/`의 구조:

- `Mag/`: 탄창 파우치
- `WalkieTalkie/`: 무전기
- `MediM/`: 중형 메디팩
- `MediL/`: 대형 메디팩
- `Admin/`: 행정 파우치
- `CamelBak/`: 수낭

파츠 폴더 안에는 부착 위치별 하위 폴더를 둡니다: `FrontHigh/`, `FrontLow/`, `Right/`, `RightBack/`, `Left/`, `LeftBack/`, `Side/`, `SideBack/`, `Groin/`, `Back/`. 해당 위치의 그림이 있는 폴더만 생성합니다.

예: `HD_IOTVGen4Assault/Mag/FrontHigh/HD_IBTV_FrontHigh_Mag_1_east.png`.

하체 보호대, 등 보호대, MOLLE 드롭 레그에 장착하는 파우치도 각 기존 폴더 안에 같은 이름의 하위 폴더로 정리합니다.

예:

- `HD_IOTVGen4Assault/HD_IOTVGen4Assault_Groin/Mag/Groin/`
- `HD_IOTVGen4Assault/HD_IOTVGen4Assault_BackProtector/MediM/Back/`
- `HD_MOLLEBattleBelt/HD_MOLLEBattleBelt_DropLeg/WalkieTalkie/Right/`

무릎 보호대는 `HD_UCPPants/KneePads/`에 보관합니다. 이미 개별 폴더가 있는 어깨·하체·등 보호대 및 드롭 레그의 기본 PNG는 기존 위치를 사용합니다.

XML의 `uiIconPath`와 `graphicData/texPath`는 실제 개별 PNG 경로를 가리킵니다. 장착 위치별 PALS 경로는 기존 패널 폴더에 `authoredPalsTextureKey`와 부착 위치 이름을 차례로 추가하여 계산합니다. 예를 들어 키가 `Mag`이고 패널 접두사가 `HD_IBTV_FrontHigh`이면 해당 패널 폴더의 `Mag/FrontHigh/` 안에서 PNG를 읽습니다. 방향 및 같은 위치의 칸 번호는 기존 파일명으로 구분합니다.

폴더와 XML 및 DLL을 적용한 뒤 게임을 재시작해야 합니다.
