# Bradley BUSK III 외장 시안

현재 Bradley의 고정 TAN Emission 채색과 원본 형상을 바탕으로 만든 **별도 BUSK III 외장 버전**이다. 상부 BRAT 96개, 하부 외장 스커트 28구획, 외부 하부판, 상승 전조등 받침, 차장 보호 구조를 추가했다.

**실차 치수 도면과 목표 BUSK III 세트의 수량은 미확인이다.** 수량·치수의 확인 범위와 추정 근거는 [REFERENCES.md](REFERENCES.md)를 먼저 확인한다. BUSK 내부 개수 전체를 재현한 모델은 아니다. 현재 96개는 제조사 설명서의 한 BRAT 구성에 맞춘 값이며, 모든 BUSK III의 정확한 수량이라는 뜻이 아니다. 위치 매핑과 절대 치수도 미인증 초안이다.

## 결과물

- `output/bradley_busk_iii.blend`: 편집용 전체 모델. 기존 차체/포탑/포신/후방 램프/인원문/TOW 계층을 보존한다.
- `output/bradley_busk_iii.glb`: 같은 모델과 기존 5개 애니메이션 클립. 고정 unlit 재질 및 내장 그릴 이미지.
- `output/Tiles/`: 상부 96개와 하부 스커트 28개의 **개별 GLB 124개**. 각 파일은 메시 하나이며 받침과 하부 중앙 분할선을 포함한다. 원점은 케이스 중심이다.
- `output/VF_East/{256,512,1024}/`: 투명 배경 무윤곽 PNG. `hull_east`, `turret_east`, `barrel_east`, `turret_full_east`, `preview_east`.
- `output/OutlinedReview/{256,512,1024}/`: 차체와 포탑 각각 검은 외곽선을 넣은 후 피벗 합성한 검수본. 256px 1px, 512px 2px. 모델 재질에 윤곽선을 구워 넣지 않는다.
- `output/OutlinedReview/native_sizes.png`, `background_checks.png`: 실제 크기 및 배경별 검수표.
- `output/iso_front.png`, `iso_rear.png`: 같은 카메라를 사용하는 외형 검수 이미지. 이 두 이미지는 무윤곽 원본 렌더다.
- `output/manifest.json`, `validation.json`: 케이스별 크기·위치·방향·장착받침 검사와 원본 보존·동작 계층·GLB 검사 결과.

`turret_full_east`에는 포신도 포함된다. 이 레이어를 사용할 때 `barrel_east`를 중복 합성하지 않는다. 차체 64° / 포탑 90° 투영이며 같은 폴더 `pivots.json`의 중심을 사용한다. VF 정의/설치 파일은 추가하지 않았고 게임 내 검증은 미실시다.

## 분리와 동작

`BUSK_HULL_KIT`는 `HULL`, `BUSK_TURRET_KIT`는 `TURRET_YAW`를 따른다. 포탑 장갑 15개가 포탑을 따라 회전하고, 차체 81개와 하부 스커트 28개는 차체에 남는다. 각 케이스는 고유 메시와 자체 중심 피벗을 가진다.

상부 형식별 수량은 M3=18 / M4=8 / M5=55 / M6=7 / M7=8이다. 하부 28개는 별도 외장 구획 수다. `Tiles` 파일은 원래 장착 방향을 이미 담고 있으며 차체 중심에서의 위치만 제거했다. 개별 GLB를 원래 자리에 복원할 때는 Blender 좌표 기준 `manifest.objects[].case_center`로 이동한다. `rotation`을 다시 적용하면 이중 회전된다. GLB의 Y-up 변환은 가져오기 도구가 처리하도록 한다.

기존 147개 메시, 70,380면과 부모를 보존했다. 변형 내에서 기존 전조등 하우징 두 개만 Z로 0.32m 올렸으며 이 수치는 추정값이다. BUSK에서 전조등이 반응장갑과 구분되도록 하우징 상면의 표시 명도를 192→216, 다른 TAN 면은 각각 +12로 조정했다. 렌즈와 나머지 부품 색은 유지한다. 설정은 `settings.json`의 `headlamp_paint`다. 원본 `.blend`와 기존 ERA 작업 파일은 수정하지 않았다. 원본 자산의 M3A3 기반 형상과 프로젝트 M2A3 명칭 차이도 그대로 남아 있다.

원본 데모 액션 5개가 활성 상태이므로 Blender에서 한 동작만 확인하려면 다른 액션을 잠시 해제한다. 포탑 회전 시 장갑의 부모 동작은 검사했지만 포신·TOW·문을 포함한 전체 각도 범위의 간섭 검사는 미실시다.

## 재생성

프로젝트 루트에서 아래 순서로 실행한다. Blender 5.1, 외부 Python의 Pillow/numpy가 필요하다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/BUSKIII/build_busk.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/BUSKIII/validate_busk.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/M2A3Bradley/BUSKIII/render_busk.py
python ArtSource/M2A3Bradley/BUSKIII/package_busk.py
```

빌더는 원본 SHA-256 일치, 원본 형상/부모 보존, 전조등 하우징 이외의 색 보존, 받침의 원본 장갑 도달을 검사한다. 검증기는 전조등의 지정 팔레트와 렌즈 색 보존, 케이스 본체 간 OBB 충돌(2mm 허용), 개별 메시/GLB/중심 피벗, 포탑 회전 부모, 5개 클립, unlit 재질과 내장 그릴을 확인한다. 장착 받침은 원본에 닿도록 의도적으로 3mm 들어간다. 3D 전체의 모든 삼각형끼리 수행하는 충돌 검사는 아니다.

이전 원본 파일의 해시가 바뀌면 빌더는 중단한다. 새 버전을 확인한 뒤 `settings.json`의 입력과 해시를 갱신한다. 수량을 바꿀 때는 실차 근거와 검증 기대값도 함께 갱신한다.
