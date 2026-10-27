# CROWS II / M2 아트

`ext/machine gun/m153-crows-untextured/source/M153 CROWS bevel.fbx`의 M153 CROWS와 모델에 포함된 M2HB를 사용했다. FDE 색상 계열 `(143,123,92)`에 `../VEHICLE_ART_STYLE.md`의 부품·표면별 명도 규칙을 적용한다. 금속색 M2, 어두운 급탄 슈트와 광학창은 고유색 예외로 구분한다. 광학 장비 전면에는 고정색 창·렌즈 메시 5개를 추가했다. 모든 재질은 고정 RGB Emission이며 조명·그림자·AO 없이 투명 배경으로 출력한다.

공유 팔레트는 `../PropAssets/weapon_paint.json`에 있다. 지지부·플랫폼·장비 상면·탄통 뚜껑을 구분하며 직접 장착 경계에는 표시 명도 약 24 이상 차이를 둔다. 평판의 상면·정면·측면·바닥에는 고정 역할색을 쓰고 어두운 페인트는 채도를 낮춘다. 작은 원통과 둥근 기구는 원주 전체 단색, 받침대 원통의 윗면은 별색이다. 월드 높이 순서로 모든 장비색을 바꾸지 않는다. `build.py`의 `PLANAR_COMPONENTS`가 평판 장비와 역할을 명시하고 그 외 작은 곡면에는 방향별 명암을 적용하지 않는다. 최종 팔레트·면 분류는 `output/paint_validation.json`에 기록한다.

원본 파일은 수정하지 않는다. 공통 이동·균일 배율 변환과 부품별 분리 후 원본 23,052개 면과 UV 루프 91,546개를 대조했다. 원본 SHA-256은 `9d347b6422e6745d3dc97055e71b0bacab1b24a2ad51b03a69a0639ef1f78cb4`이다. 소스 위치는 `HELODRACE_REFERENCE_EXT` 환경변수로 지정할 수 있다.

받침대 중심을 회전축 `(0,0,0)`으로 사용하며 M2 길이는 기존 M2 아트에 맞춘 2.1421875칸이다. 2048px 마스터에서 외곽선을 처리한 뒤 1024×1024 RGBA로 축소한다. 외곽선은 월드 기준 0.026칸, UI는 10px이다. 부품별 이미지는 동일한 캔버스와 원점을 유지한다.

게임용 PNG는 `Textures/Buildings/Security/CROWSII/`에 있다.

| 파일 | 구성 / 투영 |
| --- | --- |
| `HD_CROWSII_M2_Base.png` | 고정 받침대 / 상부 90° |
| `HD_CROWSII_M2_Frame.png` | 회전 프레임·탄통·광학 장비 / 상부 90° |
| `HD_CROWSII_M2_Top.png` | 프레임과 M2 통합, 총구 북쪽 / 상부 90° |
| `HD_CROWSII_M2_TurretTop.png` | 바닐라 `TurretTop`의 artwork rotation -90°에 맞춘 동쪽 총구 |
| `HD_CROWSII_M2Body.png` | M2 총몸·고정 슬리브 / 상부 90° |
| `HD_CROWSII_M2Barrel.png` | 리코일에 사용할 별도 총열 / 상부 90° |
| `HD_CROWSII_M2_{north,east,south,west}.png` | 조립 상태의 실제 4방향 카메라 / 64° |
| `HD_CROWSII_M2_UI.png` | 조립 상태 상부 UI 아이콘 |

상부 부품의 drawSize는 `(3.2,3.2)`, 4방향 이미지는 `(3.6,3.6)`이며 drawOffset은 `(0,0,0)`이다. M2 총몸과 총열은 총 전체 실루엣의 외곽선을 공유해 접합부의 검은 선을 피한다. 통합 Top 이미지는 조립 상태에서 렌더해 부품 간 가림을 반영한다. 분리 이미지는 후속 구현용이며 회전·리코일 동작이나 건물 정의는 이번 작업에 추가하지 않았다.

- 편집 모델: `output/crows_ii_m2.blend`
- 미리보기: `output/preview.png`
- 원본 비교 및 출력 기록: `output/metadata.json`, `output/validation.json`

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/build.py
python ArtSource/CROWSII/package.py
```

Blender 모델 비교, PNG 크기·투명도·여백과 출력 미리보기를 검증했다. 실제 게임 실행 검증은 하지 않았다. 공유 모델·출력 도구는 `ArtSource/PropAssets`에 있다.
