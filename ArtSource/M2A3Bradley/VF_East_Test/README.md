# Vehicle Framework east 시험 렌더

- `preview_east.png`: 차체·포탑·포신을 모두 표시한 결과 확인용.
- `vehicle_east.png`: 포탑과 포신을 제외한 차체 텍스처.
- `turret_east_test.png`: 포탑만 표시한 시험 레이어.
- `barrel_east_test.png`: 포신 및 포방패 시험 레이어.

1024×1024 RGBA, 투명 배경, 그림자 없음, 하향 64° 직교 투영입니다.
게임의 east에 맞춰 차량 전방이 화면 오른쪽을 향합니다. 기존 Blender 카메라
이름은 카메라 위치를 뜻하므로 원본의 `Camera_west`(-X)를 사용했습니다.

네 파일은 같은 중심과 스케일을 사용합니다. 포탑·포신은 분리 상태 확인을 위한
동일 캔버스 레이어이며, 회전축 중심으로 정렬한 최종 VF 포탑 텍스처는 아닙니다.
차체 drawSize, 포탑 피벗 및 방향별 offset 연결과 게임 내 검증은 아직 하지 않았습니다.
TAN 색상이 이미 들어 있으므로 시험 적용 시 추가 색상 곱으로 어두워지지 않게 주의합니다.
다른 방향의 렌더 및 원본 모델은 이 스크립트로 변경되지 않습니다.

재생성: Blender의 `--background --python ArtSource/M2A3Bradley/render_vf_east.py`.
`render_report.json`에 픽셀 경계와 포탑 피벗 좌표가 기록됩니다.
