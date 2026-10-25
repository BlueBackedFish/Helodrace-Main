# 차량 1024 출력·무장 분리

M3A3는 기존 무윤곽 1024 마스터에서 다시 가져오며, M8은 원본 `M8AGS/output/vehicle.blend`를 보존한 채 포탑 본체·M35 포신·M2HB를 2048 마스터로 분리 렌더한 뒤 1024로 축소한다. 512 이미지를 확대하지 않는다. 두 차량의 월드 drawSize, 서쪽 포탑 보정, ERA 위치와 상태별 가림을 유지한다.

M8 `output/WeaponLayers/vehicle.blend`에는 기존 포탑 아래 `M2HB_YAW`가 추가되어 있다. 원본 g0043 장착대와 g0044 기관총 및 gunmetal 면을 함께 분리한다. M2HB는 차장이 조작하며 M35/M240의 조준 선택과 분리된다. 100발 .50 BMG 탄약통과 파괴 가능한 본체·탄약통을 사용한다. M35는 21발, M240은 800발을 유지한다.

M242와 M35의 `VehicleTurretDef.recoil`을 사용한다. VF가 실제 발사 직후 recoilTracker를 시작하고 tick마다 복귀시키며, 커스텀 렌더러는 해당 추적기의 후퇴량만 포신 레이어에 적용한다. M242는 0.09타일, M35는 0.32타일 후퇴한다. 차체·포탑 본판·M2HB에는 주포 후퇴량을 적용하지 않는다. M35의 실제 발사 위치도 포신 후퇴량을 따른다.

2026-10-25 접합부 수정: 포탑과 포신의 검은 윤곽선을 모두 아래에 그리고, 포신·포탑의 무윤곽 색상 면을 그 위에 그린다. 독립 리코일은 유지하면서 포탑의 테두리가 포신을 가로질러 덮는 문제를 없앤다. Bradley는 ERA/전조등 상태별 합성에서 원본 색상 면과 윤곽선 재질을 함께 캐시한다. M8은 `*_fill_*` 무윤곽 레이어를 별도로 출력한다.

M2HB의 회전축은 기존 장착대 바운딩 박스 중심에서 실제 차장 큐폴라 링 중심으로 수정했다. 원본 `hatch_04`의 Z=2.4845 평면에 있는 28개 꼭짓점으로 원을 맞추었으며, 중심은 모델 좌표 `(-0.437326, 0.632416)`, 최대 반경 잔차는 0.000904다. 텍스처를 이 축에 맞춰 등록하므로 기관총이 큐폴라 둘레를 따라 회전한다. `m2hb_mount`는 고정 장착대, `m2hb`는 움직이는 기관총·탄통이다. M2HB 자체의 0.055타일 리코일과 발사점 후퇴를 연결했고, 큐폴라 중심과 장착대는 후퇴하지 않는다. 탄통의 연결된 도색 면 398개는 고정 올리브 녹색 팔레트로 바꾸고 원본 메시·UV를 유지한다.

## Cutout과 테두리 조사

- `ArtSource/IndustrialMachines/sprite_common.py:59`는 PNG 알파를 원형으로 확장한 검정 테두리를 원본 아래에 합성한다. 색상 마스크에서는 테두리를 비선택 상태로 둔다. 512px 공작기계 출력은 6px 선이다.
- `Defs/ColdWar/Buildings/Industry_ColdWar.xml` 등 일반 아이템은 `shaderType=Cutout`으로 그린다. Cutout은 알파가 낮은 픽셀을 버리는 셰이더다. 검은 선을 자동 생성하거나 이미지에 없는 선을 복원하지 않는다.
- 기존 M3A3는 합성 후 `Transparent` + 밉맵 + Trilinear였다. 반투명 경계와 저해상도 밉 단계가 선을 부드럽게 만들 수 있었다. M8은 이미 Cutout이었지만 출력이 512였다.
- 현재 런타임은 두 차량 모두 1024, Cutout, Bilinear, 밉맵 없음이다. PNG/동적 합성에 검은 선을 먼저 만들고 최종 출력에 알파 테스트를 적용한다. 가까운 거리의 선명도를 우선한 선택이며, 먼 줌에서의 앨리어싱은 실제 플레이 화면에서 추가 확인할 항목이다.
- 선의 **월드 두께는 그대로**다. M3A3는 512의 4px → 1024의 8px, M8은 512의 2px → 1024의 4px다. 두 차량의 투명 여백과 drawSize가 다르므로 같은 픽셀 선폭을 강제하지 않는다.

## 재생성

1. Blender에서 `ArtSource/M8AGS/render_weapon_layers.py` 실행.
2. `python ArtSource/VehicleRuntime/package_1024.py --mod-root <아스널 경로>`
3. `python ArtSource/VehicleRuntime/configure_weapons.py --mod-root <아스널 경로>`
4. `python ArtSource/VehicleRuntime/review.py --mod-root <아스널 경로>`

패키징과 정의 변경은 위 순서로 순차 실행한다. `review.py`는 방향별 차량을 같은 64px/타일로 비교한다. 부품 캔버스 자체를 같은 폭으로 축소한 표는 실제 차량 크기 비교가 아니다.

## 검증

실제 RimWorld/VF를 로드한 `VehicleRevisionSmoke`가 정의, 48개 Bradley 상태의 독립 포신, 실제 1024 Cutout 재질/밉맵 없음, M242·M35 후퇴/복귀, M35 발사점 후퇴, M2HB의 별도 차장 역할·조준·탄약을 확인했다. Bradley 규칙 회귀 55개도 통과했다. 이번 검증은 전체 공수 비행·착륙·저장/월드맵 통합 검증의 완료를 뜻하지 않는다.

접합부 수정 후 실제 Unity/VF 렌더 21장을 추가 검수했다. 두 차량의 네 방향 주포 대기/후퇴 8쌍, M2HB의 네 방향 회전과 후퇴를 확인했다. `output/joint_render_review.png`, `output/cupola_rotation_review.png`, `output/joint_validation.json`에 근거가 있다. 큐폴라 검수판의 빨간 십자는 고정된 실제 큐폴라 축이며 게임 텍스처에는 포함하지 않는다. 검수용 실행은 메인 메뉴에 머문 채 임시 차량만 생성하고 오프스크린 카메라로 그린다.
