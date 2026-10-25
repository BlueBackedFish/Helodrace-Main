# C-17 완전 상면 텍스처 시험본

제공된 C17.obj를 본판 공중지원(P-47/A-10C/MH-60M)과 아스널(P-47/DH-4)의 평면 항공기 스타일로 제작했다. 기체 전체의 굵은 검은 외곽선, 가는 회색 내부선, 고정 회색 면색과 청회색 창을 사용한다.

## 결과

- `output/HD_C17.png`: 기본 결과, 1024×1024 투명 RGBA, 기수가 위쪽.
- `output/Textures/{Plain,Outlined}/{1024,512,256,128}/HD_C17.png`: 윤곽선 유무와 크기별 결과. 같은 캔버스 중앙을 유지한다.
- `output/style_comparison.png`: 본판 A-10C / C-17 / 아스널 DH-4를 같은 화면 날개 폭으로 비교한 판.
- `output/native_sizes.png`: 128/256/512px 실제 크기 확인.
- `output/alpha_backgrounds.png`: 밝은·중간·어두운 배경의 윤곽선·투명 경계.
- `analysis/aircraft_references.png`: 양쪽 모드의 다섯 항공기 비교와 출처 기록.
- `output/aircraft.blend`: 원본 161개 그룹·486,906면과 UV를 보존한 편집 모델. 조명 없이 상면 카메라와 고정 면색만 사용한다.
- `output/top_plain_2048.png`, `panel_mask_2048.png`: 윤곽선 후처리 전 렌더와 내부선 마스크.

원본 파일과 해시는 `vehicle.json`, 색·선·크기 설정은 `style.json`에 있다. `ART_BRIEF.md`에 레퍼런스 근거·형상 보존·미실시 검증을 기록했다. 본판/아스널 항공기 정의와 설치 자산은 변경하지 않는다. 운영용 drawSize·실제 게임 표시는 아직 확정하지 않았다.

## 재생성

저장소 루트에서 Python(Pillow/numpy)과 Blender 5.1을 사용한다.

```powershell
python ArtSource/C17Globemaster/audit_references.py
python ArtSource/C17Globemaster/configure_trial.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/C17Globemaster/build_trial.py
python ArtSource/C17Globemaster/package_trial.py
```

공통 VehiclePipeline은 OBJ 수집·해시·좌표 변환과 이미지 처리를 재사용한다. 기존 차량의 TAN 팔레트나 포탑 분리 렌더를 항공기에 적용하지 않는다. 항공기 전용 빌더는 이 폴더에 한정된다. 역할/팔레트 변경은 설정에서 처리하고, 원본 OBJ를 덮어쓰지 않는다.

현재 레퍼런스의 수평 경계 픽셀 표본은 1024px에서 약 11~14px이고, 초안의 외곽선은 10px다. 내부 구분선은 2px이며 크기에 비례해 축소한다. `texture_validation.json`에 자동 검사, `visual_review.json`에 실제 시각 검수를 구분해 기록한다.
