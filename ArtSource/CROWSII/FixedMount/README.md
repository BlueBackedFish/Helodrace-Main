# CROWS II 고정진지 밑판

FDE `(143,123,92)`의 모서리를 자른 철제 밑판, 중앙 장착 플랜지, 방사형 보강 리브, 지면 앵커 4개, 플랜지 볼트 6개와 전원/데이터 접속함을 제작했다. 밑판은 1.50×1.36 모델 단위다. 원통 측면은 단색이며 면별 고정 Emission, 투명 배경, 조명·그림자·AO 없음이다.

기존 `../output/crows_ii_m2.blend`를 그대로 불러와 밑판 접촉면인 높이 0.175에 올렸다. CROWS 원본의 정점·부품·색·급탄 맞춤·회전축·리코일 컨트롤러는 유지한다. `CROWS_FOUNDATION_ANCHOR`가 장착 높이이며 밑판은 회전하거나 반동하지 않는다. 원본 CROWS 파일은 수정하지 않는다.

- 편집 모델: `output/fixed_mount.blend`
- 별도 밑판 PNG: `Textures/Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_{top,north,east,south,west}.png`
- 조립 UI 아이콘: `HD_CROWSII_FixedMount_UI.png`
- 미리보기: `output/preview.png`, `output/plate_preview.png`
- 기록: `output/metadata.json`, `output/validation.json`
- 저장 모델의 장착·반동 검증: `output/model_validation.json`

1024×1024 RGBA, 2048px 마스터, 외곽선 0.026칸을 사용한다. 상면은 기존 CROWS 반동 레이어와 같은 drawSize `(3.2,3.2)`, 원점 `(0,0)`이다. **밑판 → 기존 Base → Frame → Barrel → Body → FeedOverlay**로 합성한다. 방향별 밑판은 64° 정사영과 drawSize `(3.6,3.6)`이며 조립 방향별 렌더도 함께 보존했다. 높이로 올라간 CROWS의 측면 가림은 조립 모델에서 반영했다. UI는 10px 외곽선이다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/FixedMount/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/FixedMount/validate.py
python ArtSource/CROWSII/FixedMount/package.py
```

모델의 장착면 접촉, 원본 CROWS 위치 변환, 앵커 수, PNG 규격·투명도·여백과 4방향 출력을 검수했다. 새 건물 정의·설치 기능은 추가하지 않은 모델·텍스처 자료이며 게임 실행 검증은 하지 않았다.
