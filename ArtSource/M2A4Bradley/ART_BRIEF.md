# 차량 아트 작업 기록 — M2A4 Bradley (1차: TAN 채색)

공통 기준: `ArtSource/VEHICLE_ART_STYLE.md` v2.1. M3A3 Bradley·M1A2 SEPv3와 같은 공통 `VehiclePipeline`과 명도 사다리를 쓴다. 2026-11-13 사용자 요청: `.texture/ref`의 브래들리로 기갑차량 모델을 만들되 일단 채색만 TAN으로. 승인된 표준이 아니다.

## 자료와 목표

- 원본 3D: `C:\Users\human\Documents\GitHub\.texture\ref\m2a4-bradley-ifv-us-army-warno-mod\source\US_M2A4.glb` / SHA256 `404eeb55793843e127e8fef1b08a48cc2e3d298557793752375ead0a6ce45dbc`. WARNO 모드 M2A4, 40,845면 저폴리 게임 모델.
- 파이프라인 입력: `convert_glb.py`(Blender 5.1)로 만든 `..\converted\US_M2A4.obj` / SHA256 `e6f69ee64506e5ee5221e2da02c1cf7b37b79c8d0a46f6ac3a5079467a5eb3f5`. 노드마다 `o` 하나, 월드 변환을 굽고 Blender Z-up을 OBJ Y-up으로 씀. 앞 +X → 축 `forward x, up y`, canonical `(-Z,-X,Y)`, 스케일 1(GLB 미터). 렌더로 앞·좌우 확인(TOW가 좌측).
- 청사진: 미제공. 비율 검수 미실시.
- 원본 텍스처: MERDC 위장 무늬와 풍화가 그려진 PBR 텍스처. 규칙(고정 Emission, 면별 TAN 사다리)에 따라 **쓰지 않는다**. 해치·볼트·그릴 등 원본 디테일 대부분이 텍스처·노멀맵에만 있어 형상으로는 남지 않는다.
- RimThunder 레퍼런스: Desert Sabre `M2A3`(형식 차이: M2A3 ↔ M2A4).
- 기본 색: TAN 고정 Emission. 스타일은 `M3A3Bradley/style.json` 사본(tint 225/213/183, 사다리 216/192/168/144/120/96).

## 부품 계획

| 원본 노드 | 처리 | 레이어 | 색 |
|---|---|---|---|
| `Chassis` (23,389면, 한 메시) | 보존 | hull | armor, 지붕 216·정면 경사 192·측면 경사 168·벽 144·하부 96. `Lights` 재질 면 → 렌즈 192 |
| `Tourelle_01` (6,752면, 한 메시) | 보존 | turret | armor, 지붕 192·앞 경사 144·옆 경사 120·벽 96 (M3A3과 같음). 렌즈 192 |
| `Axe_Canon_02` TOW 발사기 | 보존 | turret | 216 / 측면 144, 포탑 위 `up` |
| `Fist` 포탑 부가 장비(조준경·상자 묶음) | 보존 | turret | 216 / 측면 144, 포탑 위 `up` |
| `Axe_Canon_01` 포방패·포 덮개 | 보존 | barrel | 144 / 120 |
| `Canon_01` 25 mm M242 | 보존 | barrel | 금속 72–144 매끈한 보간, 끝면 72 |
| `Axe_Canon_04`·`Canon_04` 동축 M240 | 보존 | barrel | 금속 96 |
| `Roue_*` 16개 | 보존 | hull | 고무 40, 원판 signed 80–176 |
| `Chenille_*` | 보존 | hull | 궤도 40 |
| 안테나 채찍 5개 (차체 3, 포탑 2) | 삭제 788면 | | 0.54–2.08 m, 굵기 4–6 cm. 저폴리라 3 cm 기준에 안 걸려 연결 성분(높이 > 0.5 m, 폭 < 6 cm)으로 선택 |
| 1px 미만 막대·배선 | 삭제 180면 | | §3 기준(중간 치수 < 3 cm, 길이 > 12 cm) |

원본 40,845면 중 39,877면 보존, 968면은 위 표의 명시적 생략.

## 피벗 (GLB 노드 원점 → canonical)

| rig | 근거 | 피벗 |
|---|---|---|
| `TURRET_YAW` | `Tourelle_01` 원점 | (-0.187, 0.2863, 2.0646) |
| `GUN_ELEVATION` | `Axe_Canon_01` 원점, 동축 M240은 원본에서도 그 자식 | (-0.1851, -0.1588, 2.4667) |
| `TOW_ELEVATION` | `Axe_Canon_02` 원점 | (0.6918, -0.0391, 2.3528) |

원본 게임 모델의 실제 회전 노드라 바운딩 박스 추정이 아니다. 애니메이션 검수는 포탑 35°만.

## 반복 채색 기록

| 회차 | 문제·근거 | 변경 | 결과 |
|---|---|---|---|
| 1 | 초안 | 위 표 기본값 | 빌드 통과. 안테나 채찍이 남음 |
| 2 | 안테나 굵기 4–6 cm라 1px 정리에 안 걸림 | 채찍 5개를 연결 성분으로 삭제 | 등각·EAST에서 제거 확인. 직접 겹침 `up` 2개 간격 24. 하부 측벽 148, 지붕 227로 M3A3과 같은 값 |

## 검수 기록

| 항목 | 상태 | 근거·남은 문제 |
|---|---|---|
| 청사진 비율 | 미실시 | 청사진 미제공 |
| RimThunder 비교 | 미실시 | M3A3 결과와 같은 1024 배율로만 나란히 비교 |
| 256/512 가독성 | 확인 | `output/OutlinedReview/native_sizes.png`. 차체 윗면이 넓은 단색이라 M3A3보다 밋밋함(원본 해치·그릴이 텍스처에만 있음) |
| 직접 겹침 | 통과 | `report.json` `contact_checks` |
| 원본 좌표·조명 불변·GLB unlit | 통과 | `report.json` |
| 실제 게임 표시 | 미실시 | 아스널 패키징·VF 정의 없음 |

## 다음 작업 (요청 시)

- 차체 한 메시 안의 해치·조종수 잠망경·전조등·그릴·견인 고리를 면 집합으로 분리하고 contact/accent 지정.
- 관측장비(CIV·ISU)·전조등 파괴 상태 레이어. 탄약고 뚜껑은 분리하지 않음(§6).
- 동축 M240 / `Fist` 장비를 별도 무기 스프라이트로 나눌지 결정.
- 앞 ERA 타일(`Chassis`의 `Bradley2` 재질 일부)을 기본형에서 뺄지 결정. 지금은 원본대로 포함.
- 게임 패키징(`package_game.py`)과 VF 정의.
