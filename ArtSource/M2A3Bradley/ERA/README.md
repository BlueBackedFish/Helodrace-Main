# 분리 가능한 ERA 장착 버전

- `m2a3_bradley_era.blend`: 전체 차량과 독립 ERA 블록 96개. 원본의 포탑/포신/문 애니메이션 유지.
- `m2a3_bradley_era.glb`: 전체 차량 교환용.
- `era_kit.glb`: 장착 위치를 유지한 ERA 모음.
- `Tiles/ERA_*.glb`: 블록마다 별도 파일. 각각 하나의 메시이며 중심이 원점이고 회전은 초기화되어 있음.
- `tile_manifest.json`: 각 파일의 차량 기준 위치, 회전(라디안), 크기. Blender 좌표는 Z-up, GLB는 표준 Y-up 변환 적용.
- `VF_East/preview_east.png`: 장착형 east 합성 미리보기. 차체와 포탑의 분리 PNG도 같은 폴더에 저장.

Blender에서 `ERA | independently removable tiles` 컬렉션을 숨기면 ERA 블록이 숨겨집니다. 장착형은 헤드램프와 받침대도 높였으므로 완전한 기본형은 원본 파일을 사용하세요.
각 블록은 `ERA_KIT` 또는 `ERA_TURRET_KIT` 아래 개별 메시이므로 이동/삭제/분리 애니메이션을 독립적으로 설정할 수 있습니다.
블록의 작은 체결부는 해당 블록 메시와 함께 움직입니다. ERA 분리 애니메이션 자체는 추가하지 않았습니다.

측면 55개, 전면 26개, 포탑 15개로 재구성했습니다. 앞부분은 한 단 낮아지고
뒤쪽은 2단이 되는 상부 측면 띠와 경사판을 덮는 전면 격자를 실차 사진에 맞췄습니다.
기존의 임의 34블록 배치를 폐기했습니다. 각 블록은 실제 장갑 메시 표면에 맞춰
배치하고 뒤쪽 장착 채널이 장갑에 닿도록 했습니다. 헤드램프는 전면 ERA 위로 높이고 받침대를 추가했습니다. 포탑 블록은 `ERA_TURRET_KIT`
아래에서 포탑과 함께 회전합니다. 하부 XM33/BUSK 확장 스커트는 섞지 않았습니다.

공개 제조사 자료의 96개 구성(M3 18 / M4 8 / M5 55 / M6 7 / M7 8)을
외형 모델의 분류 기준으로 사용했습니다. 각 위치의 형식 배정과 치수는 사진으로
추정했으며, 치수 기입 전체 설치 청사진에 따른 정밀 복제는 아닙니다.
참고 자료와 적용/제외 근거는 [REFERENCES.md](REFERENCES.md)에 기록했습니다.
사진이나 도면 자체를 에셋에 포함하지 않았습니다.

Blender와 PNG는 카메라/높이 보정 TAN 재질을 사용하고, GLB는 기본형과 동일한 고정 Emission 대체 재질입니다.
기본형 파일은 ERA를 포함하지 않습니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python ArtSource/M2A3Bradley/build_era.py
```
