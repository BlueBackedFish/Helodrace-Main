# 림월드용 단순화 기본형

현재 권장 작업본. 사용자 제공 War Thunder M3A3 추출 모델을 보존한
`../ReferenceFit/m3a3_bradley_refit.blend`에서 파생했습니다. ERA는 보류합니다.

- `bradley_rimworld.blend`: 편집 원본. 포탑·포신·후방 램프·출입문 독립 피벗과 시연 애니메이션 유지.
- `bradley_rimworld.glb`: 분리 계층/애니메이션 교환용 모델.
- `VF_East/preview_east.png`: EAST 합성 시험 렌더.
- `VF_East/vehicle_east.png`: 차체 64도 투영.
- `VF_East/turret_top_east.png`, `barrel_top_east.png`: 포탑/포신 90도 투영, 중앙 피벗.
- `VF_East/render_report.json`: 피벗 위치와 투영 정보. 게임 정의에 연결할 때 사용.
- `iso_front.png`, `iso_rear.png`: 전체 형상 확인.

223,389면 → 28,227면(87.4% 감소). 작은 연결 요소 3,709개와 포탑 내부 메시 제거,
평면 정리와 곡면 감량. 주요 장갑 형상, 해치, 관측장비, TOW, 연막탄 발사기,
궤도와 바퀴의 실루엣은 유지합니다. 세밀한 볼트와 잔흠집 색상은 생략했습니다.

설치된 Legion Vehicles : Acies Gravitatis(Workshop 3786789710)의
LGIFV_east.png와 LGIFVturret_autocannon.png를 직접 확인했습니다.
넓은 단색 면, 밝은 지붕, 중간 명도의 경사면, 어두운 측면/해치라는 표현을
TAN 팔레트에 적용했습니다. 모드 이미지 픽셀을 복사하지 않았습니다.
투사 그림자와 광원은 없으며 재질 자체의 명도로 형상을 표현합니다.
그릴은 단순 절차적 줄무늬입니다. GLB는 그릴을 단색 대체 재질로 출력합니다.

재생성: Blender에서 `../simplify_for_rimworld.py`, 이어 별도 프로세스로
`../render_rimworld.py` 실행. `validate.py`는 저장된 모델/애니메이션을 검사합니다.
PNG는 투명 1024px 마스터이며 실제 게임 내 설치/동작 시험은 아직 하지 않았습니다.
