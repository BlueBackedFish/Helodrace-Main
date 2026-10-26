# M2HB · M1917 교체 아트

사용자가 제공한 `ext/machine gun`의 모델로 본판 고정 기관총 두 종류를 다시 제작했다. 이전의 차량용 M2 메시와 임의로 구성한 M1917 모델은 사용하지 않는다.

- M2HB: `browning-m2/M2/M2.blend`. 총몸·총열·스페이드 그립·탄통·탄띠·삼각대 모두 이 모델의 실제 형상을 사용한다. 함께 제공된 `m2-browning/source/M2_sf.fbx`도 비교했으며, 받침과 탄통까지 포함한 전자를 채택했다.
- M1917: `browning-m1917/source/browning m1917.fbx`. 수랭 재킷·총몸·조준기·손잡이·탄통·탄띠·삼각대를 원본에서 분리한다. 추가로 만들어 붙인 장식 메시가 없다.

원본은 수정하지 않는다. 모델의 모든 면, 면에서 사용하는 꼭짓점, UV 루프를 보존한다. M2는 원본에서 16.32° 들린 총을 장착축 주위로 수평으로 맞추고, 두 총 모두 총구가 +Y를 향하도록 회전·평행 이동한다. 삼각대는 지면 자세를 유지한다. M2 탄통에는 총의 피치 보정을 적용하지 않아 총몸과 직각으로 수평을 유지한다. 원본의 열린 뚜껑과 손잡이·걸쇠를 실제 힌지 축 주위로 닫아 탄통 위에 맞춘다. FBX의 UV 경계 중복 꼭짓점은 부품 분류 때만 연결된 것으로 취급하며 출력 메시를 병합하거나 재구성하지 않는다. 각 출력 메시의 `source_vertex`, `source_face`, `source_transform`으로 원본과 대조할 수 있다.

카메라는 위에서 수직으로 내려다보는 90° 정사영이다. 2048 RGBA 마스터를 1024 런타임으로 축소한다. 금속·올리브·목재·황동은 고정 RGB Emission의 제한된 색상으로 구분하고 조명·그림자·AO·사진 질감은 사용하지 않는다. M1917 수랭 재킷, M2 총열·냉각 슬리브, 두 삼각대, M2 탄통의 넓은 면은 법선 방향에 따른 명암 없이 단색으로 처리한다. M2의 실제 탄피·탄두·연결 링크 메시를 구분해 황동색·구리색·어두운 강철색으로 칠한다. M1917 탄띠는 탄피·탄두와 캔버스 띠의 색을 구분한다. 윤곽선은 런타임 기준 검정 6px이며 원형 알파 확장과 2배 슈퍼샘플링으로 Cutout 경계를 정리한다.

총과 받침의 캔버스 중심은 원본 받침의 회전축에 맞춘다. 총의 화면상 길이는 기존 값인 M2HB 약 2.14타일, M1917 약 1.87타일을 유지한다. 받침은 원본 비율을 보존하며 잘리지 않도록 각 건물의 `graphicData/drawSize`를 별도로 계산한다. 아이콘은 두 캔버스의 실제 표시 배율을 맞춰 합성한 뒤 전체 실루엣에 맞춰 자른다.

M2HB의 총열과 본체는 같은 캔버스에서 분리해 기존 0.125타일 반동을 유지한다. 전체 실루엣의 외곽선만 각 레이어에 배분하므로 접합부에 내부 검은 테두리가 생기지 않는다. M1917은 기존 정적 상부 렌더를 사용한다. 사격 성능·탄약·코드 기능은 변경하지 않는다.

재생성 시 기본 입력은 `~/Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext`이다. 다른 위치에서는 `HELODRACE_REFERENCE_EXT` 환경 변수로 `ext` 경로를 지정한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/validate_models.py
python ArtSource/HeavyMachineGuns/package.py --apply
python ArtSource/HeavyMachineGuns/verify_registration.py
```

`output/{M2HB,M1917}/model.blend`가 편집 가능한 모델이며 `metadata.json`에 원본 경로·SHA256과 배율을 기록한다. `*_raw.png`는 무윤곽 원본, `output/Runtime`의 7개 PNG는 게임용 파일이다. `model_validation.json`은 실제 원본의 모든 면·꼭짓점·UV 대조 결과, `registration_validation.json`은 게임 등록·반동값·다른 정의 보존 검사 결과다.

시각 검수는 실제 출력인 `preview.png`, `m2_recoil_review.png`를 사용한다. `before_after.png`는 원래 게임 아트와 비교하며, `revision_comparison.png`는 위쪽의 거부된 절차형 초안과 아래쪽의 ext 모델 결과를 비교한다. `Reference/Previous`는 최초 교체 전 자료다. 게임 실행을 통한 회전·사격 검사는 수행하지 않았다.
