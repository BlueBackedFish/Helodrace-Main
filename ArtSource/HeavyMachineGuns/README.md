# M2HB · M1917 교체 아트

M1917 수랭 재킷과 두 총의 탄통·뚜껑은 조금 어두워진 FDE `(143,123,92)`로 통일했다. 삼각대·장착대는 더 어두운 FDE `(117,101,75)`를 사용한다. 기존 FDE `(153,132,99)`보다 기본 도색은 약 7%, 받침은 약 24% 낮은 표시 RGB 밝기다. 금속 총몸·총열, 목재 손잡이, 탄피·탄두·탄띠 색은 유지하며, FDE에도 측면 명암을 추가하지 않는다.

게임용 파일은 `Textures/Buildings/Security/HeavyMachineGunsFDE/`의 7개 PNG다. 두 건물의 받침·아이콘·M2 반동 레이어와 M1917 총 정의의 상부 경로를 모두 이 전용 폴더로 등록한다. 이전 루트 경로의 7개 PNG는 제거했다. 경로·도색·윤곽선 설정은 `art_style.json`에서 관리한다. 실행 중인 게임은 파일을 다시 쓰는 것만으로 텍스처를 갱신하지 않으므로 변경된 정의와 경로를 읽으려면 게임을 재시작한다.

M2 탄띠는 16개 탄마다 탄피·탄두·연결 링크를 한 묶음으로 유지해 경로를 조정한다. 총의 기울기 보정을 탄띠 전체에 그대로 적용하면 아래쪽 탄이 탄통의 앞뒤 벽 밖으로 밀려나므로, 총의 급탄구 쪽은 유지하고 탄통 입구에 가까워질수록 각 탄을 탄통 폭의 중앙으로 맞춘다. 끝의 두 탄이 실제 탄통 벽 안쪽이며 닫힌 뚜껑 아래에 있는지 모델 좌표로 검사한다. `review_feed.py`를 Blender에서 실행하면 같은 저장 모델의 급탄부를 위·앞에서 확대한 무윤곽 검수 이미지 `output/m2_feed_top.png`, `output/m2_feed_front.png`를 만든다.

사용자가 제공한 `ext/machine gun`의 모델로 본판 고정 기관총 두 종류를 다시 제작했다. 이전의 차량용 M2 메시와 임의로 구성한 M1917 모델은 사용하지 않는다.

- M2HB: `browning-m2/M2/M2.blend`. 총몸·총열·스페이드 그립·탄통·탄띠·삼각대 모두 이 모델의 실제 형상을 사용한다. 함께 제공된 `m2-browning/source/M2_sf.fbx`도 비교했으며, 받침과 탄통까지 포함한 전자를 채택했다.
- M1917: `browning-m1917/source/browning m1917.fbx`. 수랭 재킷·총몸·조준기·손잡이·탄통·탄띠·삼각대를 원본에서 분리한다. 추가로 만들어 붙인 장식 메시가 없다.

원본은 수정하지 않는다. 모델의 모든 면, 면에서 사용하는 꼭짓점, UV 루프를 보존한다. M2는 원본에서 16.32° 들린 총을 장착축 주위로 수평으로 맞추고, 두 총 모두 총구가 +Y를 향하도록 회전·평행 이동한다. 삼각대는 지면 자세를 유지한다. M2 탄통에는 총의 피치 보정을 적용하지 않아 총몸과 직각으로 수평을 유지한다. 원본의 열린 뚜껑과 손잡이·걸쇠를 실제 힌지 축 주위로 닫아 탄통 위에 맞춘다. FBX의 UV 경계 중복 꼭짓점은 부품 분류 때만 연결된 것으로 취급하며 출력 메시를 병합하거나 재구성하지 않는다. 각 출력 메시의 `source_vertex`, `source_face`, `source_transform`으로 원본과 대조할 수 있다.

카메라는 위에서 수직으로 내려다보는 90° 정사영이다. 2048 RGBA 마스터를 1024 런타임으로 축소한다. 금속·FDE·OD·목재·황동은 고정 RGB Emission의 제한된 색상으로 구분하고 조명·그림자·AO·사진 질감은 사용하지 않는다. M1917 수랭 재킷, M2 총열·냉각 슬리브, 두 삼각대, M2 탄통의 넓은 면은 법선 방향에 따른 명암 없이 단색으로 처리한다. M2의 실제 탄피·탄두·연결 링크 메시를 구분해 황동색·구리색·어두운 강철색으로 칠한다. M1917 탄띠는 탄피·탄두와 캔버스 띠의 색을 구분한다.

윤곽선은 게임에서 보이는 절대 두께 0.026타일로 통일했다. 파일마다 `0.026 / drawSize * 1024` 픽셀을 적용하므로 크기가 다른 두 총과 받침도 같은 월드 배율에서 동일한 굵기로 보인다. 기존의 모든 파일에 6px를 쓰던 방식보다 굵다. 메뉴 아이콘은 각각 크기를 채워도 1024 출력 기준 10px가 되도록 별도로 레이어를 맞추고 윤곽선을 넣는다. 원형 알파 확장과 2배 슈퍼샘플링을 유지하며, 수평 슬라이딩 최대값과 수직 원형 구간 합성으로 정확한 원형 경계를 계산한다.

총과 받침의 캔버스 중심은 원본 받침의 회전축에 맞춘다. `scale_reference.json`의 실제 총 길이(M2HB 1.654m, M1917/M1917A1 38.64인치=0.981456m)로 공통 표시 배율을 계산한다. M2HB 약 2.14타일을 기준으로 M1917은 약 1.27타일, M2 길이의 약 59.34%다. 이전 M1917 1.87타일보다 약 32% 작다.

M1917 삼각대는 원본 모델의 앞뒤 길이가 총 길이의 약 1.37배로 길게 뻗어 있었다. 1944년 Standard Ordnance Items의 M1917A1 삼각대 펼침 길이 44인치(1.1176m)에 맞추되, 회전축을 중심으로 가로·세로를 같은 배율로 줄인다. 이전의 앞뒤 축만 줄이던 보정은 제거했다. 따라서 원본 탑뷰의 다리 벌어짐 비율과 발판 형상을 유지하며 높이는 그대로 둔다. 원본이 좁은 자세이므로 문서의 완전히 벌린 폭 39인치를 재현한 것은 아니다. 모든 면과 UV를 보존하고 받침 캔버스와 `graphicData/drawSize`를 계산해 같은 피벗으로 합성한다.

M2HB의 총열과 본체는 같은 캔버스에서 분리해 기존 0.125타일 반동을 유지한다. 전체 실루엣의 외곽선만 각 레이어에 배분하므로 접합부에 내부 검은 테두리가 생기지 않는다. M1917은 기존 정적 상부 렌더를 사용한다. 사격 성능·탄약·코드 기능은 변경하지 않는다.

M1917 게임용 `HD_m1917HMG_Top.png`만 총구가 오른쪽(+X)을 향하도록 90° 시계 방향으로 회전해 내보낸다. 설치된 게임의 `TurretTop.DrawTurret`가 `ArtworkRotation=-90°`를 더하기 때문이다. 북쪽(+Y)을 향한 새 마스터를 그대로 게임용으로 쓰면 조준 방향에서 90° 어긋난다. M2의 자체 렌더러는 이 보정이 없으므로 북향 텍스처를 유지한다. 두 총의 편집 모델·마스터·아이콘·일반 비교 미리보기는 북향으로 유지하며 피벗과 표시 배율은 바꾸지 않는다.

M1917의 고정 받침은 회전축 아래의 소켓과 삼각대 다리다. 회전축 위의 크래들·측판·고저 조절 장치는 총과 함께 회전하는 상부로 분리한다. 상부 크래들을 받침 이미지에 남기면 총을 돌릴 때 조립 방향이 어긋난다. 모델 검사에서 고정 받침이 회전면 위로 올라오지 않고 회전 상부가 그 아래로 내려가지 않는지도 확인한다.

`validate_rotation.py`는 Windows PowerShell의 ReflectionOnly로 실제 게임 DLL의 ArtworkRotation 초기값과 DrawTurret의 사용 여부를 읽는다. 내보낸 PNG에서 총구 방향을 측정하고 게임 보정을 적용한 8방향이 조준각과 맞는지 확인한다. `output/m1917_rotation_review.png`는 고정 삼각대 위에 각 방향의 게임용 총 레이어를 합성한 검수 이미지이며, `rotation_validation.json`에 각도 오차와 검사한 게임 DLL 해시를 기록한다. 실제 게임 실행 화면은 아니다.

재생성 시 기본 입력은 `~/Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext`이다. 다른 위치에서는 `HELODRACE_REFERENCE_EXT` 환경 변수로 `ext` 경로를 지정한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/HeavyMachineGuns/validate_models.py
python ArtSource/HeavyMachineGuns/package.py --apply
python ArtSource/HeavyMachineGuns/validate_rotation.py
python ArtSource/HeavyMachineGuns/verify_registration.py
python ArtSource/HeavyMachineGuns/deploy.py
```

`output/{M2HB,M1917}/model.blend`가 편집 가능한 모델이며 `metadata.json`에 원본 경로·SHA256과 배율을 기록한다. `*_raw.png`는 무윤곽 원본, `output/Runtime`의 7개 PNG는 게임용 파일이다. `model_validation.json`은 실제 원본의 모든 면·꼭짓점·UV 대조 결과, `registration_validation.json`은 게임 등록·반동값·다른 정의 보존 검사 결과다.

`deploy.py`는 설치 모드의 7개 PNG와 두 XML의 해당 아트 필드만 백업 후 교체한다. 등록된 7개 경로가 실제 PNG로 연결되고 기본·받침 FDE 픽셀이 올바른지 설치 파일에서 직접 확인한다. 구형 루트 PNG 7개만 백업 후 제거하며, 결과는 `output/deployment_validation.json`에 기록한다. 다른 설치 위치는 `--installed`로 지정한다.

시각 검수는 실제 출력인 `preview.png`, `m2_recoil_review.png`를 사용한다. `preview.png`는 두 기관총을 동일한 월드 배율·회전축 기준으로 표시해 실제 크기 차이를 보여준다. `icon_preview.png`는 각각 크기를 채운 메뉴 아이콘 비교이므로 크기 비율 검수에 사용하지 않는다. `before_after.png`는 원래 게임 아트와 비교하며, `revision_comparison.png`는 위쪽의 거부된 절차형 초안과 아래쪽의 ext 모델 결과를 비교한다. `Reference/Previous`는 최초 교체 전 자료다. 게임 실행을 통한 회전·사격 검사는 수행하지 않았다.
