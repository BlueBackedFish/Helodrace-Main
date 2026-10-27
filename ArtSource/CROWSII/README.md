# CROWS II / M2HB QCB 아트

`ext/machine gun/m153-crows-untextured/source/M153 CROWS bevel.fbx`의 M153 CROWS 프레임·받침·탄통·급탄 슈트를 사용한다. 원본의 총 3개 메시를 제거하고 `../HeavyMachineGuns/output/M2HB/model.blend`의 M2HB QCB 총몸·총열을 실제로 이식했다. FDE 색상 계열 `(143,123,92)`에 `../VEHICLE_ART_STYLE.md`의 부품·표면별 명도 규칙을 적용한다. 금속색 M2, 어두운 급탄 슈트와 광학창은 고유색 예외로 구분한다. 광학 장비 전면에는 고정색 창·렌즈 메시 5개를 추가했다. 모든 재질은 고정 RGB Emission이며 조명·그림자·AO 없이 투명 배경으로 출력한다.

공유 팔레트는 `../PropAssets/weapon_paint.json`에 있다. 지지부·플랫폼·장비 상면·탄통 뚜껑을 구분하며 직접 장착 경계에는 표시 명도 약 24 이상 차이를 둔다. 평판의 상면·정면·측면·바닥에는 고정 역할색을 쓰고 어두운 페인트는 채도를 낮춘다. 작은 원통과 둥근 기구는 원주 전체 단색, 받침대 원통의 윗면은 별색이다. 월드 높이 순서로 모든 장비색을 바꾸지 않는다. `build.py`의 `PLANAR_COMPONENTS`가 평판 장비와 역할을 명시하고 그 외 작은 곡면에는 방향별 명암을 적용하지 않는다. 최종 팔레트·면 분류는 `output/paint_validation.json`에 기록한다.

이식한 총은 지상용 M2HB QCB와 동일한 원본 메시·UV·면색을 사용한다. 별도의 CROWS 총 재채색이나 이전 `.50CAL` 부품 매핑은 없다. 지상용 삼각대·탄통·탄띠는 가져오지 않는다. 기존 총열 축과 총구 위치에 맞춘 단일 균일 배율/이동만 적용하며 무기 길이는 2.1421875칸이다. QCB 손잡이·결합부도 지상용 모델의 같은 부품을 복사한다. 방열구멍 안쪽과 외피, 총열 단차는 지상용 총에서 만든 구분을 그대로 유지한다. `output/detail_paint.json`에 적용된 채색 설정과 장착 변환을 기록한다.

급탄 슈트는 원본 72개 단면 링 중 총 쪽 24개 링을 새 급탄구에 맞춰 조정했다. 지상용 M2HB의 첫 두 탄의 길이·높이 범위와 급탄 덮개 가장자리에서 입구의 중심·폭을 계산한다. 슈트 끝을 해당 면에 맞추고 연결 방향과 폭을 점진적으로 바꾸며 탄통 쪽 384개 정점은 고정한다. 변경한 정점은 192개이며 기존 면·UV는 유지했다. `articulation.json`, `feed_alignment.py`, `output/feed_validation.json`에 설정과 실제 끝점 대조를 기록한다.

원본 파일은 수정하지 않는다. M153 프레임 메시의 14,192개 면과 UV 루프 57,140개를 원본과 대조했다. M153 원본 SHA-256은 `9d347b6422e6745d3dc97055e71b0bacab1b24a2ad51b03a69a0639ef1f78cb4`이다. `validate_donor.py`에서 지상용 QCB의 20개 메시(그중 QCB 추가 부품 8개), 정점 21,408개·면 23,755개·UV 루프 87,329개 및 모든 면의 Emission 색을 대조했다. 모델을 다시 만들면 현재 결과로 검증 파일을 갱신한다. 소스 위치는 `HELODRACE_REFERENCE_EXT` 환경변수로 지정할 수 있다.

받침대 중심을 회전축 `(0,0,0)`으로 사용하며 M2 길이는 기존 M2 아트에 맞춘 2.1421875칸이다. 2048px 마스터에서 외곽선을 처리한 뒤 1024×1024 RGBA로 축소한다. 외곽선은 월드 기준 0.026칸, UI는 10px이다. 부품별 이미지는 동일한 캔버스와 원점을 유지한다.

게임용 PNG는 `Textures/Buildings/Security/CROWSII/`에 있다.

| 파일 | 구성 / 투영 |
| --- | --- |
| `HD_CROWSII_M2_Base.png` | 고정 받침대 / 상부 90° |
| `HD_CROWSII_M2_Frame.png` | 회전 프레임·탄통·광학 장비 / 상부 90° |
| `HD_CROWSII_M2Body.png` | M2 총몸·고정 슬리브 / 상부 90° |
| `HD_CROWSII_M2Barrel.png` | 움직이는 총열·QCB 손잡이·결합부 / 상부 90° |
| `HD_CROWSII_M2_FeedOverlay.png` | 총몸 앞에 실제로 보이는 고정 급탄 슈트의 가림 보정 |
| `HD_CROWSII_M2_{north,east,south,west}.png` | 조립 상태의 실제 4방향 카메라 / 64° |
| `HD_CROWSII_M2_UI.png` | 조립 상태 상부 UI 아이콘 |

상부 부품의 drawSize는 `(3.2,3.2)`, 4방향 이미지는 `(3.6,3.6)`이며 drawOffset은 `(0,0,0)`이다. M2 총몸과 총열은 총 전체 실루엣의 외곽선을 공유해 접합부의 검은 선을 피한다. 총열을 포함하던 두 통합 Top PNG는 제거했다. 조립 아이콘과 4방향 이미지만 완성 상태로 출력하며, 반동에는 분리한 상부 레이어를 사용한다.

그리는 순서는 **Base → Frame → M2Barrel → M2Body → FeedOverlay**다. Frame의 슈트 중 총몸 앞에 보이는 부분을 마지막 보정 레이어에서 복원해 가림 순서를 맞춘다. 이 보정에는 가려진 절단면의 검은 테두리를 새로 만들지 않는다. 움직이는 레이어는 M2Barrel 하나다. 총몸·슬리브·잠금 탭·급탄 슈트·프레임은 고정이다. PNG 1024px 기준 뒤쪽으로 12px(0.0375칸) 움직이는 검수 예제를 저장했으며, 큰 이동으로 QCB 손잡이가 슬리브를 관통하는 현상을 피한다.

Blender 모델에는 `CROWS_YAW`와 그 아래 `BARREL_RECOIL`을 분리했다. 총열과 QCB 손잡이·결합부를 포함한 7개 메시만 반동 컨트롤러에 속한다. 60fps의 1/2/6 프레임에서 정상/최대 후퇴/복귀하는 애니메이션이 있으며 저장 시 1프레임 정상 위치로 돌린다. `output/recoil_layers.json`이 텍스처 경로·원점·순서·반동량의 연결 명세다. 실제 게임 정의나 사격 코드 연결은 추가하지 않았다.

- 편집 모델: `output/crows_ii_m2.blend`
- 미리보기: `output/preview.png`
- 원본 비교 및 출력 기록: `output/metadata.json`, `output/validation.json`
- 이식 무기 원본 대조: `output/donor_validation.json`
- 급탄·반동 메시 검증: `output/articulation_validation.json`
- 3D 렌더와 레이어 합성 대조: `output/layer_validation.json`
- 분리/반동 미리보기: `output/recoil_preview.png`, `output/recoil_preview.gif`

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/build.py -- --kind M2HB
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/validate_donor.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/validate_articulation.py
python ArtSource/CROWSII/package.py
python ArtSource/CROWSII/validate_layers.py
```

Blender 모델 비교, PNG 크기·투명도·여백과 출력 미리보기를 검증했다. 실제 게임 실행 검증은 하지 않았다. 공유 모델·출력 도구는 `ArtSource/PropAssets`에 있다.
