# 분리 가능한 ERA 장착 버전

- `m2a3_bradley_era.blend`: 전체 차량과 독립 ERA 블록 34개. 원본의 포탑/포신/문 애니메이션 유지.
- `m2a3_bradley_era.glb`: 전체 차량 교환용.
- `era_kit.glb`: 장착 위치를 유지한 ERA 모음.
- `Tiles/ERA_*.glb`: 블록마다 별도 파일. 각각 하나의 메시이며 중심이 원점이고 회전은 초기화되어 있음.
- `tile_manifest.json`: 각 파일의 차량 기준 위치, 회전(라디안), 크기. Blender 좌표는 Z-up, GLB는 표준 Y-up 변환 적용.
- `VF_East/preview_east.png`: 장착형 east 합성 미리보기. 차체와 포탑의 분리 PNG도 같은 폴더에 저장.

Blender에서 `ERA | independently removable tiles` 컬렉션을 숨기면 기본형으로 돌아갑니다.
각 블록은 `ERA_KIT` 아래 개별 메시이므로 이동/삭제/분리 애니메이션을 독립적으로 설정할 수 있습니다.
블록의 작은 체결부는 해당 블록 메시와 함께 움직입니다. ERA 분리 애니메이션 자체는 추가하지 않았습니다.

측면 28개(좌우 각 2열 × 7개), 전면 6개의 게임용 외형 근사 배치입니다.
실제 BRAT 키트의 정확한 블록 규격/배치를 재현했다고 보장하지 않습니다.
[미 육군의 Bradley armor tiles 소개](https://www.army.mil/article/97332/savings_served_up_for_bradley_armor_plates)를 참고했으며 원본 사진/도면을 에셋에 포함하지 않았습니다.

Blender와 PNG는 카메라/높이 보정 TAN 재질을 사용하고, GLB는 기본형과 동일한 고정 Emission 대체 재질입니다.
기본형 파일은 ERA를 포함하지 않습니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python ArtSource/M2A3Bradley/build_era.py
```
