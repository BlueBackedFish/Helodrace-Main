# CROWS 장착용 Mk 19 · M240B · M249

사용자가 ext에 둔 3D FBX에서 각 무기를 가져와 기존 CROWS의 프레임·광학장비·받침·탄통에 장착한 아트다. 원본의 선택한 정점·면·UV를 유지하고 균일 배율과 장착 변환만 적용했다. 급탄부는 기존 M2의 단순한 폐쇄형 슈트 메시를 재사용한다. 개별 탄·링크·새 급탄구 블록은 사용하지 않는다. 기존 M2 CROWS 파일과 게임 정의·발사 코드는 수정하지 않는다.

| 무기 | ext/machine gun 아래 원본 | 장착 시 정리 |
| --- | --- | --- |
| Mk 19 | `ukrainian-hmmwv-mk-19/source/hmmwv-mk19.fbx`의 `Mk19_custom_mod_1_Baked`만 사용 | 험비 차체·다른 메시 제외. 원래 옆 탄통과 탄띠 대신 CROWS 탄통에서 급탄 |
| M240B | `m240b-machine-gun/source/M240B_low.fbx`의 `gun_low` | 펼친 양각대 제외. 기존 아래로 늘어진 `bullet.001` 대신 장착 위치에 맞춘 탄띠 |
| M249 | `m249/source/M249_Low.fbx` | 펼친 양각대·보병용 확대 조준경 제외. 별도 탄띠 추가 |

무기의 고유 금속·검정 폴리머와 기존 CROWS FDE `(143,123,92)` 계열을 구분한다. 조명·그림자·AO 없는 고정 RGB Emission, 투명 배경이다. 90° 상면은 drawSize `(3.2,3.2)`, 원점 `(0,0)`이며 기존 밑판과 Base를 그대로 재사용한다. 방향별 렌더는 실제 64° 정사영과 drawSize `(3.6,3.6)`이다. 모두 1024×1024 RGBA, 2048px 마스터와 기존 0.026칸 cutout 윤곽선 규격이다.

급탄 슈트는 원본 M2의 568면·576정점 메시와 UV·단색 `(48,56,52)`를 그대로 가져온다. 탄통 쪽 384개 정점은 고정하고 총몸 쪽 24개 링만 부드럽게 변형해 각 급탄구에 연결한다. 기존 탄통 출구 `(-0.51906625,-0.27242,0.85398)`를 유지하며, 마지막 링은 급탄구 안으로 0.004칸 겹친다. 단면의 앞뒤 폭은 Mk 19 140mm, M240B 86mm, M249 72mm 상당으로 맞춘다. 총몸에 가려지는 부분은 실제 3D 가림을 반영한다.

크기는 **현재 CROWS에 장착된 M2 메시를 직접 측정한 전장**을 기준으로 계산한다. M2 실물 전장 1.654m와 현재 메시 약 2.1421876칸의 비율을 사용하며, 가로·세로·높이에 동일한 배율을 적용해 총열 단면이나 원본 비율을 찌그러뜨리지 않는다. 이전 수동 기준값을 반복하지 않고 빌드할 때 M2 파일에서 다시 측정한다.

| 무기 | 기준 전장 | M2 대비 전장 |
| --- | ---: | ---: |
| M2HB | 1,654mm | 100% |
| Mk 19 | 1,095mm | 약 66.2% |
| M240B | 1,263mm | 약 76.4% |
| M249 | 1,015mm | 약 61.4% |

규격 출처는 U.S. Ordnance의 [M2HB](https://www.usord.com/weapons/m2hb), [Mk 19](https://www.usord.com/weapons/mk19-mod3), [M240/GPMG](https://www.usord.com/weapons/m240-gpmg), [고정식 개머리판 M249](https://www.usord.com/weapons/m249) 자료다. 동일 제조사 자료로 통일하면서 이전 출력 대비 M240B는 약 2.5% 확대, M249는 약 2% 축소했다. Mk 19는 기존 비율을 거의 유지한다. 각 FBX의 고유 형상을 실측 형상이라고 주장하는 것은 아니다.

## 파일

게임용 PNG는 `Textures/Buildings/Security/CROWSII/Variants/`에 있다. `HD_CROWSII_{MK19,M240B,M249}_` 접두사별로 다음을 제공한다.

- `Frame`, `Barrel`, `Body`, `Feed`: 별도 등록 레이어. 기존 `HD_CROWSII_M2_Base` 위에 **Frame → Barrel → Body → Feed**로 합성한다. 무기 교체 시 이 네 레이어를 함께 바꾼다.
- `Top`: 같은 레이어를 합성한 상면 완성형.
- `north/east/south/west`: 실제 방향별 조립 모습.
- `UI`: 아이콘.

총몸과 총열은 하나의 무기 실루엣 윤곽선을 나눠 사용하므로 경계에 가짜 절단면이 생기지 않는다. 이 세 무기의 외부 총열은 고정되어 있다. M2의 포신 왕복 애니메이션을 그대로 적용하지 않는다. 게임 내 무기 교체·탄약·발사 기능은 이번 아트 제작 범위에 포함하지 않았다.

- 편집 모델: `output/{무기}/crows_{무기 소문자}.blend`
- 미리보기: `output/preview.png`, `output/weapon_preview.png`, `output/direction_preview.png`, `output/scale_comparison.png`
- 장착·급탄·출력 기록: 각 `metadata.json`, `output/layers.json`, `output/model_validation.json`, `output/validation.json`
- M2 기준 실측 기록: `output/scale_calibration.json`
- 원본 검사 기록: `output/source_inventory.json`, `output/component_inventory.json`, `output/station_inventory.json`

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/Variants/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/CROWSII/Variants/validate.py
python ArtSource/CROWSII/Variants/package.py
```

선택한 무기 형상·UV, 기존 프레임 보존, M2 대비 전장 비율, 슈트 양 끝 접속, 원본 슈트 UV·색 보존과 고정 재질을 검사하고 출력 크기로 확인한다. 게임 실행 검증은 하지 않았다.
