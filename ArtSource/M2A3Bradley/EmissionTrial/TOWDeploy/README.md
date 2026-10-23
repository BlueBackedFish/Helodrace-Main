# TOW 분리 및 전개 시험본

색 통일을 마친 `../bradley_emission_trial.blend`에서 만든 별도 모델이다. 원본의 접힌 형상과 고정 Emission 색을 보존하고 TOW 전개 축을 추가했다. ERA 작업은 포함하지 않는다.

- `bradley_tow_deploy.blend` / `.glb`: 차량 전체. 기존 4개 동작과 `TOW_DEPLOY_OPEN_CLOSE`를 포함한다.
- `tow_launcher.blend` / `.glb`: 발사기와 장착대만 있는 독립 모델. 힌지 원점은 `(0, 0, 0)`이며 TOW 동작 1개만 포함한다.
- `tow_deploy.gif`: 독립 모델의 접힘 → 전개 → 접힘 미리보기.
- `stowed_deployed.png`: 동일 카메라의 접힘/전개와 실제 크기 256px EAST 비교.

## 편집 및 동작

`TOW_DEPLOY`의 로컬 Y 회전은 0°에서 접힘, -90°에서 전개다. 키프레임은 1·20 접힘, 60·90 전개, 120 접힘이다.
전체 BLEND의 기존 포탑/주포/문 데모 액션도 활성 상태이므로 TOW만 확인할 때에는 다른 네 액션을 잠시 해제하거나 독립 모델을 사용한다. GLB는 5개 클립을 분리해 내보냈다.

| 부품 | 역할 |
|---|---|
| `WT_roll_05` | 전개되는 발사기 본체, 450면 |
| `WT_roll_04` | 별도 메시인 앞 방진 덮개, 본체와 함께 전개 |
| `WT_TOW_FIXED_BRACKET` | 기존 `WT_roll_05`에서 분리한 고정 브래킷 142면 |
| `WT_rocket_mount_01`, `WT_roll_03/06/07/08` | 포탑에 고정되는 기존 장착 부품 |

앞 덮개는 개별 숨김/편집이 가능하지만 열리는 힌지를 새로 만들지는 않았다. 원본 면을 복제하거나 삭제하지 않고 분리해 전체 52,878면을 유지했다. 메시 수는 103개에서 104개가 됐다.
전개 중 고정 장착대의 변환이 유지되고, 닫힐 때 원래 위치와 색이 복원되는지 검사했다. 독립 모델은 8개 메시다.

## EAST 텍스처

1024px 마스터와 `256/`, `512/` PNG를 함께 저장했다. 배경은 투명하며 그림자/광원을 사용하지 않는다. `pivots.json`은 왼쪽 위 원점의 픽셀 좌표다.

- `turret_base_top_east.png`: 움직이는 본체와 앞 덮개를 제외한 고정 포탑.
- `tow_stowed_top_east.png`, `tow_deployed_top_east.png`: 움직이는 두 메시만 렌더한 레이어. 고정 포탑은 holdout으로 가림을 처리했다.
- `turret_stowed_top_east.png`, `turret_deployed_top_east.png`: 해당 자세의 포탑 전체를 한 번에 렌더한 대안.
- `preview_stowed_east.png`, `preview_deployed_east.png`: 기존 차체 64°와 포탑 90°의 합성 확인용.

포탑 전체/고정 레이어에는 **포신도 포함**된다. 기존 `barrel_top_east`를 중복으로 얹지 않는다. 차체는 상위 `../VF_East/`의 같은 해상도 `vehicle_east.png`를 사용한다.
차체 위의 `hull_anchor`에 포탑 레이어 중심을 맞추고, 고정 포탑 → 해당 TOW 레이어 순서로 겹친다. 두 포탑 레이어의 피벗은 항상 이미지 중심이다. 중간 전개각은 모델에서 추가 렌더한다.

별도 렌더한 두 레이어는 안티앨리어싱 경계에서 전체 렌더와 미세한 차이가 있다. 경계를 제외한 안정된 불투명 영역의 최대 채널 오차는 0.078/255 미만이다. 경계까지 한 번에 렌더한 결과가 필요하면 `turret_STATE_top_east.png`를 쓴다. 선형 RGB·알파 선곱 Lanczos 축소를 사용했다.

## 근거와 범위

[실제 전개 사진](https://www.dvidshub.net/image/5007679/bradley-tow-missile)과 [M2A3 장착부 사진](https://www.dvidshub.net/image/1635149/cav-conducts-tow-missile-training)을 확인했다. [Army OD1609 외부 구조 설명](https://armyordnance.tpub.com/Od1609/Tow-Launcher-Exterior-34.htm)은 방진 덮개가 탈착되는 부품임을 확인하는 데 사용했다.
전개 방향은 실물 사진을 참고했으며 축 위치와 90° 회전은 제공된 메시 상단 안쪽 모서리에 맞춘 **게임용 근사 동작**이다. 실측 링크 기구를 재현한 것은 아니다. Vehicle Framework 정의나 게임 코드는 변경하지 않았으며 게임 내 검증은 미실시다.

재생성 순서:

1. 상위 시험본을 먼저 빌드한다.
2. Blender 5.1에서 `--python-exit-code 1 --python build_tow.py` 실행.
3. 별도 Blender 프로세스에서 `validate_tow.py`, `render_tow.py` 실행.
4. Pillow/numpy가 있는 Python으로 `package_tow.py` 실행.

`settings.json`은 대상 메시·축·각도, `manifest.json`은 원본 해시와 면 분리 이력, `validation.json`은 모델/동작/GLB 검사 결과다. `render_validation.json`과 `package_validation.json`은 레이어 합성·잘림·출력 크기를 기록한다.
