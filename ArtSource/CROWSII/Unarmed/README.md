# CROWS II 비무장 모델

기존 `../output/crows_ii_m2.blend`에서 M2HB QCB 화기 20개 메시와 프레임에 합쳐진 급탄 슈트 568면을 실제로 제거한 별도 모델이다. 무기를 숨기는 방식은 사용하지 않는다. 사용하지 않는 무기 데이터와 급탄 슈트 재질도 정리했다. 탄통·광학장비·프레임·빈 화기 받침·고정 받침대의 형상, UV, FDE 면색과 배치를 보존했다. 원본 M2 CROWS와 다른 무기 버전은 수정하지 않는다.

- 편집 모델: `output/crows_ii_unarmed.blend`
- 기본 미리보기: `output/station_preview.png`
- 기존 고정진지 밑판과 조합한 미리보기: `output/preview.png`. 밑판 모델은 포함하지 않으며 기존 FixedMount 아트를 별도로 사용한다.
- 모델 제거/보존 검사와 원본 해시: `output/metadata.json`
- 레이어 명세: `output/layers.json`
- PNG/원점/레이어 합성 검사: `output/validation.json`

게임용 아트는 `Textures/Buildings/Security/CROWSII/Unarmed/HD_CROWSII_Unarmed_{Frame,Top,north,east,south,west,UI}.png`이다. 모든 PNG는 1024×1024 RGBA이고, 2048px 마스터에 기존 0.026칸 cutout 외곽선을 적용했다. 상면은 90°·drawSize `(3.2,3.2)`, 방향별은 64°·drawSize `(3.6,3.6)`, 회전축은 기존 받침대 중심 `(0,0,0)`이다.

상면 레이어는 기존 `HD_CROWSII_M2_Base` → 새 `HD_CROWSII_Unarmed_Frame` 순서로 그린다. `Top`은 두 레이어를 합친 아트다. 모델의 `CROWS_YAW`에는 프레임만 연결되어 있고 총열 반동 컨트롤러와 화기/급탄 레이어는 없다. 기존 고정진지 밑판을 사용할 경우 밑판을 먼저 그리고 그 위에 CROWS를 그린다. 밑판과 CROWS의 윤곽선은 별도로 유지한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/Unarmed/build.py
python ArtSource/CROWSII/Unarmed/package.py
```

모델 생성 시 제거 대상 수, 남은 면의 원본 정점·UV·재질·월드 좌표, 회전축, 원본 파일 불변을 검사한다. 패키징에서 같은 원점의 레이어 합성과 실제 전체 3D 렌더를 대조하고 PNG 크기·투명도를 확인한다. 게임 정의나 무기 교체 기능은 추가하지 않았으며 게임 실행 검증은 하지 않았다.
