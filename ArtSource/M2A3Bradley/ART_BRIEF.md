# Bradley 아트 작업 기록

2026-10-22 최신 요청: 2차 포탑 재구성을 되돌리고, 기존 형상을 유지한 채 부품 사이 색 차이를 약 3배로 확대한다.
공통 기준은 `ArtSource/VEHICLE_ART_STYLE.md` v1.2다. 현재 결과는 채색 시제품이며 승인된 최종 스타일 표준은 아니다.

## 자료와 목표

- 청사진: M2A3 Bradley, `C:/Users/human/Downloads/il_fullxfull.4547052563_4kxl.webp`.
- 3D 형상 근거: 사용자 제공 War Thunder `m3a3_bradley_0.obj`. M2A3 청사진과 형식이 다르며 상세 대응은 `Audit/Stage1/FORMAT_BASELINE.md`에 기록한다.
- OBJ 좌표 변환: (X,Y,Z) → Blender(-Z,-X,Y+0.012). 실제 단위는 추가 검증 필요.
- 목표 스타일: RimThunder의 차량 외형·디테일 가독성. Legion은 3D 렌더·한 면 한 색의 표현 방식만 참고한다.
- 사용자 설명의 RimThunder 제작 과정은 추정이며 확인된 제작 이력으로 단정하지 않는다.
- TAN, 그림자·AO 없음, 투명 배경. ERA는 보류한다.

## 현재 형상과 채색

- 편집 모델: `RimWorld/bradley_rimworld.blend`, 교환용 `bradley_rimworld.glb`.
- 고정 입력: `ReferenceFit/stage1_rimworld_base.blend`. 2차 재구성 전의 43,344면·83개 메시를 정확히 복원했다.
- 저장 결과와 고정 입력의 꼭짓점·면·부모·월드 변환을 프레임 1에서 비교해 일치를 확인했다. 기존 4개 애니메이션과 분리 피벗도 검증했다.
- 2차의 새 포탑 해치·수납함·연막탄 등 재구성 형상은 현재 출력에 없다. 기존 입력의 빈 메시 15개와 생략 상태도 복원됐다.
- `part_contrast.json`에서 부품과 주변 지붕의 명도 차이를 기본 3배로 확대한다. 이전에 같은 색이던 장비는 역할별 최소 차이를 사용한다.
- 포탑 해치·TOW·관측장비·후방 수납함 등 26개 부품/연결 요소에 보정을 적용했다. 기존 면 방향에 따른 명도와 고무·유리·금속·그릴 재질을 유지한다.
- 지나친 암색/백색을 막는 제한이 있으므로 모든 픽셀의 차이가 정확히 3배인 것은 아니다. 총 2,589개 면에서 제한이 적용됐다.
- 후방 지붕 해치는 주변 지붕보다 밝게 유지한다. 작은 높이·카메라 깊이 보정은 기존대로 면 전체에 적용한다.

## 투영과 출력

- EAST 차체: 64도 고각 직교 투영, 포탑/포신: 90도 상면 직교 투영.
- 포탑 회전·포신 상하·후방 램프·출입문은 독립 계층을 유지한다.
- `RimWorld/VF_East/`에 1024px 마스터, `256/`와 `512/`에 분리 PNG·합성 미리보기·피벗 정보를 저장한다.
- 차량 화면 폭은 알파 >20 기준 약 195px/388px다. `iso_front.png`, `iso_rear.png`는 형상 확인용이다.

## 검증과 남은 범위

- `RimWorld/recolor_validation.json`: 2차 이전 형상/계층 일치, 주요 해치의 지붕 대비 3배 설정, 후방 해치 밝기 확인.
- `RimWorld/validation.json`: 저장 모델·4개 애니메이션·무광원·투명 배경·ERA 미포함 확인.
- `RimWorld/VF_East/render_report.json`: 레이어·여백·카메라와 렌더 조건 확인.
- 256px/512px EAST와 앞/뒤 등각 렌더를 시각 확인했다. 실제 게임 drawSize·줌에서의 검수는 미실시다.
- 이번 변경은 형상 결함을 새로 수리하는 작업이 아니다. 빈 메시/생략 부품은 `Audit/Stage1/PART_ROLES.md`에 남아 있고, 연막탄 등 추가 형상은 별도 요청 시 검토한다.
- 1차의 112개 원본/파생 그룹 역할표와 형식 기준은 유지한다. `bradley_roles.json`은 현재 채색 역할에 사용하며 새 형상 생성은 하지 않는다.
- `Audit/Stage2/REPORT.md`와 이전 단계별 수정 계획은 철회된 실험의 기록이다. 현재 실행 경로는 `simplify_for_rimworld.py` → `recolor_bradley.py`다.
