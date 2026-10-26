# M2HB · M1917 교체 아트

기존 본판 고정 기관총 텍스처를 대체한다. 카메라는 위에서 수직으로 내려다보는 90° 정사영이다. 2048 RGBA 마스터를 1024 런타임으로 축소하며 고정 RGB Emission, 투명 배경, 조명·그림자·AO 없음으로 제작했다. 런타임 윤곽선은 검은색 6px이며 원형 알파 확장 후 2배 슈퍼샘플링으로 매끈한 Cutout 경계를 만든다.

M2HB는 기존 `M8AGS/output/WeaponLayers/vehicle.blend`의 g0044 외형을 사용한다. 원본 파일을 수정하지 않고 분리본에서 꼭짓점·면·UV 루프를 보존해 180° 회전 및 평행 이동했다. 원래 탄통은 올리브색으로 유지하고 보이는 탄띠·냉각구·상부 덮개만 별도 외관 메시로 보강했다. 차량용 M2HB 출력은 변경하지 않는다.

M1917은 [미 육군 군사사 센터의 M1917A1 박물관 사진](https://commons.wikimedia.org/wiki/File:Browning1917.jpg)을 참고해 새 외형 모델로 제작했다. 이 사진은 미 연방정부 공개 저작물이다. 수랭 재킷, 앞뒤 테두리, 운반 손잡이와 띠, 주입구, 상부 덮개·접힌 가늠자, 후방 손잡이, 탄통과 탄띠를 표현했다. 기존 게임 정의의 이름은 M1917이며 참고 사진의 형식은 M1917A1이다. 온라인 무료 모델 페이지는 접근이 차단되어 다운로드한 것으로 취급하지 않았다.

각 총에 맞는 삼각대를 별도 렌더했다. 공유 `HD_Tripod1`을 덮어쓰지 않고 해당 두 건물만 새 받침을 사용한다. 총 회전 중심은 새 모델의 장착축 `(0,0,0)`에 맞췄다. 표시 배율은 기존 총의 화면상 길이(M2HB 약 2.14타일, M1917 약 1.87타일)를 보존하도록 계산한다. 메뉴 아이콘은 총과 삼각대를 합성한 뒤 별도로 가운데 맞춰 크기를 채운다.

M2HB의 기존 포신·본체 경로와 0.125타일 반동은 유지한다. 두 레이어를 같은 캔버스·피벗에서 출력하고 합친 외곽 실루엣의 테두리만 분배한다. 접합부에 검은 내부 윤곽선을 따로 만들지 않는다. M1917은 기존 정적 상부 렌더를 사용한다. 사격 성능이나 탄약 기능은 변경하지 않는다.

재생성:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 4 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/validate_models.py
python ArtSource/HeavyMachineGuns/package.py --apply
```

`output/{M2HB,M1917}/model.blend`가 편집 가능한 모델이고 `*_raw.png`가 무윤곽 원본이다. `output/Runtime`에는 실제 교체 파일 7개가 있으며 `manifest.json`은 해시·투명도·잘림·배율 검사를 기록한다. `preview.png`, `before_after.png`, `m2_recoil_review.png`를 실제 출력에서 시각 검수한다. 이전 텍스처는 비교용 `Reference/Previous`에 보존한다. 이번 작업은 모델·텍스처·정의 등록 검증이며, 게임 실행을 통한 회전·사격 검증은 수행하지 않았다.
