# 한국어 번역 누락 전수 점검 및 임시 번역안

- 점검 범위: `Defs/**/*.xml`, `Languages/English/Keyed/**/*.xml`
- Def 사용자 노출 문자열 후보: 2,018개
- 한국어 DefInjected 누락: 686개
- English Keyed 키: 850개
- 한국어 Keyed 누락: 0개
- XML 파싱 오류: 0개

> 아래 내용은 외부 서비스로 저장소 텍스트를 전송하지 않고 작성한 임시 번역안입니다. 고유명사, 군사 용어, RimWorld 문체와 자리표시자는 실제 적용 전에 검수해야 합니다.

## English Keyed 점검 결과

English Keyed의 모든 키에 대응하는 한국어 키가 존재합니다.

## DefInjected 누락 및 임시 번역안

### Defs/ColdWar/Buildings/Defense_ColdWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 1 | ThingDef | `HD_Building_M220TOW.label` | M220 TOW launcher | M220 TOW 발사기 |
| 2 | ThingDef | `HD_Building_M220TOW.description` | A crew-served tube-launched, optically tracked, wire-guided anti-tank missile launcher. Its launch tube remains in place after firing and accepts several types of TOW missile. | 여러 명이 운용하는 발사관식·광학추적·유선유도 대전차미사일 발사기입니다. 발사 후에도 발사관이 남으며 여러 종류의 TOW 미사일을 장전할 수 있습니다. |
| 3 | ThingDef | `HD_Building_M167VADS.label` | M167 VADS | M167 VADS |
| 4 | ThingDef | `HD_Building_M167VADS.description` | A towed 20 mm Vulcan air defense system with a 500-round ammunition drum. Its six-barrel rotary cannon saturates exposed targets with an extremely dense burst of fire. It must be directly operated by a pawn. | 500발 탄약 드럼을 장착한 견인식 20mm 벌컨 방공체계입니다. 6총열 회전식 기관포가 노출된 표적에 고밀도 탄막을 퍼부으며, 폰이 직접 운용해야 합니다. |
| 5 | ThingDef | `HD_Building_M167VADS.comps.0.fuelLabel` | M167 ammunition | M167 탄약 |
| 6 | ThingDef | `HD_Building_M167VADS.comps.0.fuelGizmoLabel` | Ammunition | 탄약 |
| 7 | ThingDef | `HD_Building_M167VADS.comps.0.outOfFuelMessage` | Needs ammunition | 탄약 필요 |

### Defs/ColdWar/Items/A10C_CAS.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 8 | ThingDef | `HD_Projectile_A10C_PGU13.label` | 30×173 mm PGU-13/B HEI round | 30×173 mm PGU-13/B HEI 탄 |
| 9 | ThingDef | `HD_Projectile_A10C_PGU14.label` | 30×173 mm PGU-14/B API round | 30×173 mm PGU-14/B API 탄 |

### Defs/ColdWar/Items/Ammunition_ColdWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 10 | ThingCategoryDef | `HD_TOWMunitions.label` | TOW missiles | TOW 미사일 |
| 11 | ThingCategoryDef | `HD_TOWMunitions.modExtensions.0.caliber` | 152mm | 152mm |
| 12 | ThingCategoryDef | `HD_20x102mmAmmunition.label` | 20x102mm ammunition | 20×102mm 탄약 |
| 13 | ThingCategoryDef | `HD_20x102mmAmmunition.modExtensions.0.caliber` | 20×102mm | 20×102mm |
| 14 | ThingDef | `HD_20x102mmMixedRound.label` | 20x102mm mixed ammunition | 20×102mm 혼합 탄약 |
| 15 | ThingDef | `HD_20x102mmMixedRound.description` | A linked 20x102mm ammunition supply for the M167 VADS, assembled in its standard mixed feed of high-explosive incendiary, high-explosive incendiary tracer, and armor-piercing incendiary projectiles. | M167 VADS용 20×102mm 링크 탄약입니다. 표준 혼합 급탄 구성에 따라 고폭소이탄, 고폭소이예광탄, 철갑소이탄이 섞여 있습니다. |
| 16 | ThingDef | `HD_BGM71A_Round.label` | BGM-71A missile | BGM-71A 미사일 |
| 17 | ThingDef | `HD_BGM71A_Round.description` | An early wire-guided anti-tank missile for the M220 TOW launcher. | M220 TOW 발사기용 초기형 유선유도 대전차미사일입니다. |

### Defs/ColdWar/Items/Grenades_ColdWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 18 | ThingDef | `HD_M14_ClipEffect.label` | AN-M14 safety clip | AN-M14 안전 클립 |
| 19 | ThingDef | `HD_M14_PinEffect.label` | AN-M14 safety pin | AN-M14 안전핀 |
| 20 | ThingDef | `HD_M7A2_ClipEffect.label` | M7A2 safety clip | M7A2 안전 클립 |
| 21 | ThingDef | `HD_M7A2_PinEffect.label` | M7A2 safety pin | M7A2 안전핀 |
| 22 | ThingDef | `HD_ThermiteGrenadeEmitter.label` | thermite reaction | 테르밋 반응 |
| 23 | ThingDef | `HD_CSGasEmitter.label` | CS gas emitter | CS 가스 방출기 |

### Defs/ColdWar/Items/M79_ColdWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 24 | ThingCategoryDef | `HD_40x46mmLowVelocityAmmo.modExtensions.0.caliber` | 40×46mm | 40×46mm |
| 25 | ThingDef | `HD_40mmM381HE_Proj.label` | M381 HE grenade | M381 HE 수류탄 |
| 26 | ThingDef | `HD_40mmM433HEDP_Proj.label` | M433 HEDP grenade | M433 HEDP 수류탄 |
| 27 | ThingDef | `HD_40mmM576Pellet_Proj.label` | M576 buckshot pellet | M576 벅샷 산탄 |
| 28 | ThingDef | `HD_40mmM651CS_Proj.label` | M651 CS grenade | M651 CS 수류탄 |
| 29 | ThingDef | `HD_40mmSponge_Proj.label` | 40×46mm sponge projectile | 40×46mm 스펀지탄 |
| 30 | ThingDef | `HD_M651CSGasEmitter.label` | M651 CS gas emitter | M651 CS 가스 방출기 |
| 31 | ThingDef | `HD_M79CartridgeMote.label` | 40×46mm cartridge case | 40×46mm 탄피 |
| 32 | ThingDef | `HD_Gun_M79_Weapon.tools.0.label` | stock | 개머리판 |

### Defs/ColdWar/Items/Weapons_ColdWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 33 | ThingDef | `HD_Projectile_M167_20mmHEI.label` | 20 mm high-explosive incendiary projectile | 20 mm 고폭소이탄 |
| 34 | ThingDef | `HD_Projectile_M167_20mmAPI.label` | 20 mm armor-piercing incendiary projectile | 20 mm 철갑소이탄 |
| 35 | ThingDef | `HD_Projectile_M167_20mmHEIT.label` | 20 mm high-explosive incendiary tracer projectile | 20 mm 고폭소이예광탄 |
| 36 | ThingDef | `HD_Projectile_20mmHEIFragment.label` | 20 mm HEI fragment | 20 mm HEI 파편 |
| 37 | ThingDef | `HD_Gun_M167VADS_TurretGun.label` | M167 VADS 20 mm rotary cannon | M167 VADS 20 mm 회전식 기관포 |
| 38 | ThingDef | `HD_BGM71A_Proj.label` | BGM-71A missile | BGM-71A 미사일 |
| 39 | ThingDef | `HD_Gun_M220TOW_TurretGun.label` | M220 TOW launcher | M220 TOW 발사기 |
| 40 | ThingDef | `HD_M47_M222_Proj.label` | M222 missile | M222 미사일 |
| 41 | ThingDef | `HD_M47_M222_Proj.description` | A 140 mm shaped-charge missile guided through a command wire by an M47 Dragon launcher. | M47 드래곤 발사기의 지령선을 통해 유도되는 140mm 성형작약 미사일입니다. |
| 42 | ThingDef | `HD_Gun_M47_Dragon.label` | M47 Dragon | M47 드래곤 |
| 43 | ThingDef | `HD_Gun_M47_Dragon.description` | A disposable man-portable anti-tank guided missile system. The operator must keep the target in sight while commands travel through a wire to the missile. Pulsed steering charges correct its course in visible steps, and the operator may transfer guidance to another target while the missile is in flight. | 일회용 휴대식 대전차유도미사일 체계입니다. 조작자는 유도 명령이 전선을 통해 미사일에 전달되는 동안 표적을 시야에 유지해야 합니다. 펄스식 조향 장약이 단계적으로 비행 경로를 수정하며, 비행 중 다른 표적으로 유도를 전환할 수도 있습니다. |
| 44 | ThingDef | `HD_Gun_M47_Dragon.tools.0.label` | launch tube | 발사관 |
| 45 | ThingDef | `HD_Gun_M60E3_Weapon.tools.0.label` | stock | 개머리판 |
| 46 | ThingDef | `HD_Gun_M60E3_Weapon.tools.1.label` | barrel | 총열 |
| 47 | ThingDef | `HD_PowerCutter.tools.0.label` | grip | 손잡이 |
| 48 | ThingDef | `HD_PowerCutter.tools.1.label` | cutting blade | 절단날 |
| 49 | ThingDef | `HD_Gun_M16A1_Weapon.label` | M16A1 | M16A1 |
| 50 | ThingDef | `HD_Gun_M16A1_Weapon.description` | An early lightweight 5.56 mm service rifle with full-automatic fire. A Helod sharpshooter can trade eight tiles of effective range for much faster target acquisition. | 완전자동 사격이 가능한 초기형 경량 5.56mm 제식소총입니다. 헤로드 명사수는 유효 사거리 8칸을 대가로 표적 획득 속도를 크게 높일 수 있습니다. |
| 51 | ThingDef | `HD_Gun_M16A2_Weapon.label` | M16A2 | M16A2 |
| 52 | ThingDef | `HD_Gun_M16A2_Weapon.description` | A refined 5.56 mm service rifle with improved sights, heavier furniture, and a controlled three-round burst. A Helod sharpshooter can trade eight tiles of effective range for much faster target acquisition. | 개선된 조준기와 더 무거운 부품, 제어된 3점사를 갖춘 개량형 5.56mm 제식소총입니다. 헤로드 명사수는 유효 사거리 8칸을 대가로 표적 획득 속도를 크게 높일 수 있습니다. |
| 53 | ThingDef | `HD_Gun_M16A3_Weapon.label` | M16A3 | M16A3 |
| 54 | ThingDef | `HD_Gun_M16A3_Weapon.description` | A full-automatic derivative of the M16A2 intended for sustained close-to-medium-range fire. A Helod sharpshooter can trade eight tiles of effective range for much faster target acquisition. | 근거리에서 중거리까지 지속 사격하도록 만든 M16A2의 완전자동 파생형입니다. 헤로드 명사수는 유효 사거리 8칸을 대가로 표적 획득 속도를 크게 높일 수 있습니다. |

### Defs/ColdWar/PowerCutter_Breach.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 55 | ThingDef | `HD_PowerCutterMetalFlashLight.label` | power cutter flash light | 동력 절단기 손전등 |

### Defs/Future/Items/Weapons_Future.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 56 | ThingDef | `HD_LANCE_LRA7_Beam.label` | LRA-7 coupled-mass shot | LRA-7 결합 질량탄 |
| 57 | ThingDef | `HD_LANCE_LRA7_BeamEffect.label` | LRA-7 beam effect | LRA-7 빔 효과 |
| 58 | ThingDef | `HD_LANCE_LRA7_FeedbackWave.label` | LRA-7 feedback wave | LRA-7 피드백 파동 |
| 59 | ThingDef | `HD_Gun_LANCE_LRA7_Weapon.label` | LANCE LRA-7 | LANCE LRA-7 |
| 60 | ThingDef | `HD_Gun_LANCE_LRA7_Weapon.description` | A second-generation LANCE rifle using a single-measurement system to couple its positive- and negative-mass projectiles. The coupled shot gains destructive energy over distance; beyond its safe separation envelope it collapses into a feedback runaway. | 양질량탄과 음질량탄을 결합하는 단일 측정 체계를 사용하는 2세대 LANCE 소총입니다. 결합탄은 비행 거리가 늘수록 파괴 에너지를 얻지만 안전 분리 범위를 넘으면 피드백 폭주로 붕괴합니다. |
| 61 | ThingDef | `HD_Gun_LANCE_LRA7_Weapon.tools.0.label` | stock | 개머리판 |

### Defs/GasDefs_Helod.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 62 | Helodrace.HelodGasDef | `HD_PhotochlorogenGasGrid.label` | photochlorogen gas | 포토클로로겐 가스 |
| 63 | Helodrace.HelodGasDef | `HD_PhotochlorogenGasGrid.description` | A pale delayed-action choking agent cloud. Photochlorogen first irritates the airways, then may seem to fade during a latent period before pulmonary edema begins damaging the lungs. Gas masks and other tox-gas exposure immunity block it. | 옅은 색의 지연성 질식 작용제 구름입니다. 포토클로로겐은 먼저 기도를 자극하고 잠복기 동안 사라진 듯 보이지만, 이후 폐부종이 발생해 폐를 손상시킵니다. 방독면과 독성 가스 노출 면역으로 차단할 수 있습니다. |
| 64 | Helodrace.HelodGasDef | `HD_SweetGasGrid.label` | sweet gas | 스위트 가스 |
| 65 | Helodrace.HelodGasDef | `HD_SweetGasGrid.description` | A yellow delayed blister agent cloud. Sweet gas dose does not naturally fall; if no dose change occurs for several hours, the exposure is considered cleared. Gas masks and tox-gas exposure immunity do not block it; if Odyssey is installed, vacuum resistance reduces exposure. | 노란색의 지연성 수포 작용제 구름입니다. 스위트 가스의 노출량은 자연 감소하지 않으며, 몇 시간 동안 변화가 없으면 노출이 해소된 것으로 봅니다. 방독면과 독성 가스 면역으로 막을 수 없지만 Odyssey가 설치된 경우 진공 저항이 노출을 줄입니다. |
| 66 | Helodrace.HelodGasDef | `HD_CSGasGrid.label` | CS gas | CS 가스 |
| 67 | Helodrace.HelodGasDef | `HD_CSGasGrid.description` | A dense tear-agent cloud that causes immediate eye, skin, and respiratory irritation. Gas protection and toxic-environment resistance reduce exposure. | 눈, 피부와 호흡기를 즉시 자극하는 짙은 최루 작용제 구름입니다. 가스 방호와 유독 환경 저항이 노출을 줄입니다. |
| 68 | Helodrace.HelodGasDef | `HD_CNGasGrid.label` | CN gas | CN 가스 |
| 69 | Helodrace.HelodGasDef | `HD_CNGasGrid.description` | A persistent tear-agent cloud that develops more slowly than CS. Heavy exposure causes severe eye, skin, and respiratory irritation and may inflict minor tissue damage. Gas protection and toxic-environment resistance reduce exposure. | CS보다 느리게 작용하는 지속성 최루 작용제 구름입니다. 심하게 노출되면 눈, 피부와 호흡기가 강하게 자극되고 경미한 조직 손상이 생길 수 있습니다. 가스 방호와 유독 환경 저항이 노출을 줄입니다. |
| 70 | Helodrace.HelodGasDef | `HD_WhitePhosphorusSmokeGrid.label` | white phosphorus smoke | 백린 연막 |
| 71 | Helodrace.HelodGasDef | `HD_WhitePhosphorusSmokeGrid.description` | A dense, persistent cloud produced by burning white phosphorus. It obscures gunfire as effectively as an ordinary smoke screen and can ignite flammable things caught inside. | 백린이 타면서 만들어지는 짙고 오래가는 구름입니다. 일반 연막처럼 사격선을 효과적으로 가리며 내부의 가연성 물체에 불을 붙일 수 있습니다. |

### Defs/GreatWar/ApparelLayers_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 72 | ApparelLayerDef | `HD_Pouch.label` | pouch | 파우치 |

### Defs/GreatWar/Buildings/Defense_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 73 | ThingDef | `HD_Building_M1917HMG.comps.0.fuelLabel` | M1917 ammunition | M1917 탄약 |
| 74 | ThingDef | `HD_Building_M1917HMG.comps.0.fuelGizmoLabel` | Ammunition | 탄약 |
| 75 | ThingDef | `HD_Building_M1917HMG.comps.0.outOfFuelMessage` | Needs ammunition | 탄약 필요 |
| 76 | ThingDef | `HD_Building_M2HB.comps.0.fuelLabel` | M2HB ammunition | M2HB 탄약 |
| 77 | ThingDef | `HD_Building_M2HB.comps.0.fuelGizmoLabel` | Ammunition | 탄약 |
| 78 | ThingDef | `HD_Building_M2HB.comps.0.outOfFuelMessage` | Needs ammunition | 탄약 필요 |
| 79 | ThingDef | `HD_Building_3InchHawkinsMortar.label` | 3-inch Hawkins mortar | 3인치 호킨스 박격포 |
| 80 | ThingDef | `HD_Building_3InchHawkinsMortar.description` | A crew-served 3-inch trench mortar for lobbing high-explosive shells over cover. It must be directly operated by a pawn and reloaded with prepared HE shells. | 엄폐물 너머로 고폭탄을 투사하는 다인 운용식 3인치 참호 박격포입니다. 폰이 직접 운용해야 하며 준비된 고폭탄으로 재장전합니다. |

### Defs/GreatWar/Flamethrower_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 81 | ThingDef | `HD_Gun_M2FlameThrower_Weapon.tools.0.label` | stock | 개머리판 |

### Defs/GreatWar/Items/Grenades_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 82 | ThingDef | `HD_M8_ClipEffect.label` | M8 safety clip | M8 안전 클립 |
| 83 | ThingDef | `HD_M8_PinEffect.label` | M8 safety pin | M8 안전핀 |
| 84 | ThingDef | `HD_M15_ClipEffect.label` | M15 safety clip | M15 안전 클립 |
| 85 | ThingDef | `HD_M15_PinEffect.label` | M15 safety pin | M15 안전핀 |
| 86 | ThingDef | `HD_MKII_ClipEffect.label` | MK II safety clip | MK II 안전 클립 |
| 87 | ThingDef | `HD_MKII_PinEffect.label` | MK II safety pin | MK II 안전핀 |
| 88 | ThingDef | `HD_MKIII_ClipEffect.label` | MK III safety clip | MK III 안전 클립 |
| 89 | ThingDef | `HD_MKIII_PinEffect.label` | MK III safety pin | MK III 안전핀 |

### Defs/GreatWar/Items/Items_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 90 | ThingCategoryDef | `HD_3InchMortarShells.modExtensions.0.caliber` | 3-inch | 3인치 |
| 91 | ThingCategoryDef | `HD_81mmMortarShells.modExtensions.0.caliber` | 81mm | 81mm |
| 92 | ThingCategoryDef | `HD_37mmShells.modExtensions.0.caliber` | 37mm | 37mm |
| 93 | ThingCategoryDef | `HD_105mmHowitzerShells.modExtensions.0.caliber` | 105mm | 105mm |
| 94 | ThingCategoryDef | `HD_155mmHowitzerShells.modExtensions.0.caliber` | 155mm | 155mm |
| 95 | ThingCategoryDef | `HD_Rockets.modExtensions.0.caliber` | 60mm | 60mm |
| 96 | ThingDef | `HD_Hardtack.tools.0.label` | hardtack | 건빵 |
| 97 | ThingDef | `HD_3InchMortarShell_HE.label` | 3-inch mortar HE shell | 3인치 박격포 고폭탄 |
| 98 | ThingDef | `HD_3InchMortarShell_HE.description` | A high-explosive bomb for the 3-inch Hawkins mortar. It is packed for indirect fire against clustered infantry and light fieldworks. | 3인치 호킨스 박격포용 고폭탄입니다. 밀집 보병과 경야전 진지를 간접 사격하도록 장약되어 있습니다. |
| 99 | ThingDef | `HD_3InchMortarShell_CG.label` | 3-inch mortar photochlorogen shell | 3인치 박격포 포토클로로겐탄 |
| 100 | ThingDef | `HD_3InchMortarShell_CG.description` | A chemical bomb for the 3-inch Hawkins mortar. On impact it bursts once and releases a compact photochlorogen cloud, much narrower than a deployed canister. | 3인치 호킨스 박격포용 화학탄입니다. 충돌 시 한 번 폭발해 살포통보다 훨씬 좁고 응축된 포토클로로겐 구름을 방출합니다. |
| 101 | ThingDef | `HD_Apparel_GreatWarM1Helmet.label` | m1 Helmet | M1 헬멧 |
| 102 | ThingDef | `HD_Apparel_GreatWarM1Helmet.description` | m1 helmet with hole for ears | 귀가 들어갈 구멍이 있는 M1 헬멧입니다. |

### Defs/GreatWar/Items/M8FlareGun_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 103 | ThingDef | `HD_Gun_M8FlareGun_Weapon.tools.0.label` | grip | 손잡이 |

### Defs/GreatWar/Items/Weapons_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 104 | ThingDef | `HD_3InchMortarShell_HE_Proj.label` | 3-inch mortar HE shell | 3인치 박격포 고폭탄 |
| 105 | ThingDef | `HD_3InchMortarShell_CG_Proj.label` | 3-inch mortar photochlorogen shell | 3인치 박격포 포토클로로겐탄 |
| 106 | ThingDef | `HD_Gun_M1911_Weapon.tools.0.label` | grip | 손잡이 |
| 107 | ThingDef | `HD_Gun_M1A1Harrington_Weapon.tools.0.label` | stock | 개머리판 |
| 108 | ThingDef | `HD_Gun_M1A1Harrington_Weapon.tools.1.label` | barrel | 총열 |
| 109 | ThingDef | `HD_Gun_M3A1_Weapon.tools.0.label` | stock | 개머리판 |
| 110 | ThingDef | `HD_Gun_M3A1_Weapon.tools.1.label` | barrel | 총열 |
| 111 | ThingDef | `HD_Gun_M9A1_Weapon.tools.0.label` | tube | 관 |
| 112 | ThingDef | `HD_Gun_3InchHawkinsMortar_TurretGun.label` | 3-inch Hawkins mortar | 3인치 호킨스 박격포 |
| 113 | ThingDef | `HD_Gun_3InchHawkinsMortar_TurretGun.description` | A simple infantry mortar firing high-explosive bombs in a high arc. It requires a crew and prepared shells. | 고각으로 고폭탄을 발사하는 단순한 보병 박격포입니다. 운용 인원과 준비된 포탄이 필요합니다. |
| 114 | ThingDef | `HD_Projectile_3InchHEFragment.label` | 3-inch HE shell fragment | 3인치 고폭탄 파편 |
| 115 | ThingDef | `HD_Projectile_81mmHEFragment.label` | 81mm HE shell fragment | 81mm 고폭탄 파편 |
| 116 | ThingDef | `HD_Projectile_105mmHEFragment.label` | 105mm HE shell fragment | 105mm 고폭탄 파편 |
| 117 | ThingDef | `HD_Gun_KragJorgensen_Weapon.tools.0.label` | stock | 개머리판 |
| 118 | ThingDef | `HD_Gun_M1897_Weapon.tools.0.label` | stock | 개머리판 |
| 119 | ThingDef | `HD_Gun_M1897_Weapon.tools.1.label` | barrel | 총열 |
| 120 | ThingDef | `HD_Gun_M1A1Carbine_Weapon.tools.0.label` | stock | 개머리판 |
| 121 | ThingDef | `HD_Gun_M2Carbine_Weapon.tools.0.label` | stock | 개머리판 |
| 122 | ThingDef | `HD_Gun_M1Garand_Weapon.tools.0.label` | stock | 개머리판 |
| 123 | ThingDef | `HD_Gun_M1D_Weapon.tools.0.label` | stock | 개머리판 |
| 124 | ThingDef | `HD_Gun_M1918A2HAR_Weapon.tools.0.label` | stock | 개머리판 |
| 125 | ThingDef | `HD_Gun_M1918A2HAR_Weapon.tools.1.label` | barrel | 총열 |
| 126 | ThingDef | `HD_Gun_M1919A6LMG_Weapon.tools.0.label` | stock | 개머리판 |
| 127 | ThingDef | `HD_Gun_M1919A6LMG_Weapon.tools.1.label` | barrel | 총열 |
| 128 | ThingDef | `HD_Gun_SprMThree_Weapon.tools.0.label` | stock | 개머리판 |
| 129 | ThingDef | `HD_Gun_M1903A4_Weapon.tools.0.label` | stock | 개머리판 |
| 130 | ThingDef | `HD_Gun_M1903Pedersen_Weapon.tools.0.label` | stock | 개머리판 |
| 131 | ThingDef | `HD_M1902Saber.tools.0.label` | handle | 손잡이 |
| 132 | ThingDef | `HD_M1902Saber.tools.1.label` | point | 찌르기 |
| 133 | ThingDef | `HD_M1902Saber.tools.2.label` | edge | 날 |
| 134 | ThingDef | `HD_USMCFightingKnife.tools.0.label` | point | 찌르기 |
| 135 | ThingDef | `HD_USMCFightingKnife.tools.1.label` | edge | 날 |

### Defs/GreatWar/Jobs_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 136 | WorkGiverDef | `DoBillsHD_StirredTankReactor.label` | operate stirred-tank reactor | 교반식 탱크 반응기 운전 |
| 137 | WorkGiverDef | `DoBillsHD_StirredTankReactor.verb` | operate | 작동 |
| 138 | WorkGiverDef | `DoBillsHD_StirredTankReactor.gerund` | operating | 작동 중 |
| 139 | JobDef | `HD_MedibagLoadSupply.reportString` | loading treatment supplies into medic bag. | 의무가방에 치료 물자를 적재하는 중. |
| 140 | JobDef | `HD_MedibagHemostasis.reportString` | applying field hemostasis. | 야전 지혈을 시행하는 중. |
| 141 | JobDef | `HD_MedibagPlasmaTransfusion.reportString` | performing plasma transfusion. | 혈장 수혈을 시행하는 중. |
| 142 | JobDef | `HD_IgniteSweetGasCan.reportString` | igniting sweet gas can. | 스위트 가스 살포통을 점화하는 중. |
| 143 | JobDef | `HD_ReloadRecoillessWeaponFromBag.reportString` | reloading recoilless weapon from inventory. | 소지품에서 무반동 무기를 재장전하는 중. |
| 144 | JobDef | `HD_AssistReloadRecoillessWeapon.reportString` | assisting recoilless weapon reload. | 무반동 무기 재장전을 보조하는 중. |
| 145 | JobDef | `HD_StandbyRecoillessLoader.reportString` | standing by as recoilless weapon loader. | 무반동 무기 장전수로 대기하는 중. |
| 146 | JobDef | `HD_AssembleM1MortarMount.reportString` | installing the M1 mortar bipod. | M1 박격포 양각대를 설치하는 중. |
| 147 | JobDef | `HD_BeginM1MortarAssembly.reportString` | moving to the M1 mortar assembly site. | M1 박격포 조립 지점으로 이동하는 중. |
| 148 | JobDef | `HD_AssembleM1MortarBarrel.reportString` | installing the M1 mortar barrel. | M1 박격포 총열을 설치하는 중. |
| 149 | JobDef | `HD_DisassembleM1Mortar.reportString` | disassembling the M1 mortar. | M1 박격포를 분해하는 중. |
| 150 | JobDef | `HD_EvacuateRaidCasualty.reportString` | evacuating a wounded squadmate. | 부상당한 분대원을 후송하는 중. |
| 151 | JobDef | `HD_ReloadM79FromInventory.reportString` | reloading M79 grenade launcher from inventory. | 소지품에서 M79 유탄발사기를 재장전하는 중. |
| 152 | JobDef | `HD_ThrowInventoryGrenadeClose.reportString` | preparing a close grenade throw. | 근거리 수류탄 투척을 준비하는 중. |
| 153 | JobDef | `HD_ThrowInventoryGrenadeNormal.reportString` | preparing a grenade throw. | 수류탄 투척을 준비하는 중. |
| 154 | JobDef | `HD_OperateMultiCrewTurret.reportString` | operating a crew-served turret. | 다인 운용 포탑을 조작하는 중. |

### Defs/GreatWar/RaidStrategies_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 155 | RaidStrategyDef | `HD_GW_MortarAssault.letterLabelEnemy` | Mortar assault | 박격포 강습 |
| 156 | RaidStrategyDef | `HD_GW_MortarAssault.arrivalTextEnemy` | The raiders are establishing a field position with an M1 mortar, automatic-rifle cover, ammunition, and rations. | 습격자들이 M1 박격포, 자동소총 엄호, 탄약과 식량을 갖춘 야전 진지를 구축하고 있습니다. |
| 157 | RaidStrategyDef | `HD_GW_ChemicalMortarAssault.letterLabelEnemy` | Chemical mortar assault | 화학 박격포 강습 |
| 158 | RaidStrategyDef | `HD_GW_ChemicalMortarAssault.arrivalTextEnemy` | The raiders are establishing a protected field position with an M1 mortar and BA chemical shells. Every member is carrying CBRN protection. | 습격자들이 M1 박격포와 BA 화학탄을 갖춘 방호 야전 진지를 구축하고 있습니다. 전원이 화생방 보호장비를 휴대했습니다. |

### Defs/GreatWar/Recipes_GreatWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 159 | RecipeDef | `HD_MakeFLAM.label` | make FLAM x20 | FLAM ×20 만들기 |
| 160 | RecipeDef | `HD_MakeFLAM.description` | Can a large batch of finely processed meat into compact tins of FLAM. | 곱게 가공한 고기를 대량으로 통조림 처리해 작은 FLAM 캔으로 만듭니다. |
| 161 | RecipeDef | `HD_MakeFLAM.jobString` | Making FLAM. | FLAM 제작 중. |
| 162 | RecipeDef | `HD_Make3InchMortarShellHE.label` | make 3-inch mortar HE shell | 3인치 박격포 고폭탄 만들기 |
| 163 | RecipeDef | `HD_Make3InchMortarShellHE.description` | Assemble a high-explosive shell for the 3-inch Hawkins mortar. | 3인치 호킨스 박격포용 고폭탄을 조립합니다. |
| 164 | RecipeDef | `HD_Make3InchMortarShellHE.jobString` | Making 3-inch mortar HE shell. | 3인치 박격포 고폭탄 제작 중. |
| 165 | RecipeDef | `HD_Make3InchMortarShellCG.label` | make 3-inch mortar photochlorogen shell | 3인치 박격포 포토클로로겐탄 만들기 |
| 166 | RecipeDef | `HD_Make3InchMortarShellCG.description` | Assemble a compact photochlorogen shell for the 3-inch Hawkins mortar. | 3인치 호킨스 박격포용 소형 포토클로로겐탄을 조립합니다. |
| 167 | RecipeDef | `HD_Make3InchMortarShellCG.jobString` | Making 3-inch mortar photochlorogen shell. | 3인치 박격포 포토클로로겐탄 제작 중. |

### Defs/Helod/Appearance/Hair.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 168 | HairDef | `HD_HelodHair2.label` | Helod Hair 2 | 헤로드 헤어 2 |
| 169 | HairDef | `HD_HelodHair3.label` | Helod Hair 3 | 헤로드 헤어 3 |
| 170 | HairDef | `HD_HelodHair4.label` | Helod Hair 4 | 헤로드 헤어 4 |
| 171 | HairDef | `HD_HelodHair5.label` | Helod Hair 5 | 헤로드 헤어 5 |
| 172 | HairDef | `HD_HelodHair6.label` | Helod Hair 6 | 헤로드 헤어 6 |
| 173 | HairDef | `HD_HelodHair7.label` | Helod Hair 7 | 헤로드 헤어 7 |
| 174 | HairDef | `HD_HelodHair8.label` | Helod Hair 8 | 헤로드 헤어 8 |
| 175 | HairDef | `HD_HelodHair9.label` | Helod Hair 9 | 헤로드 헤어 9 |
| 176 | HairDef | `HD_HelodHair10.label` | Helod Hair 10 | 헤로드 헤어 10 |

### Defs/Helod/Genetics/Xenotypes.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 177 | XenotypeDef | `HD_MackenzieValleyHelod.descriptionShort` | A large, resilient, and authoritative helod. | 크고 강인하며 권위적인 헤로드입니다. |
| 178 | XenotypeDef | `HD_TexasHelod.descriptionShort` | A swift, heat-tolerant sharpshooter helod. | 빠르고 열에 강한 명사수 헤로드입니다. |
| 179 | XenotypeDef | `HD_ArcticHelod.descriptionShort` | An elite polar sniper and lone wolf helod. | 정예 극지 저격수이자 고독한 헤로드입니다. |
| 180 | XenotypeDef | `HD_EasternHelod.descriptionShort` | An intellectual, highly social 내정-focused monopolist helod. | 지적이고 사교적이며 내정에 특화된 독점가 헤로드입니다. |
| 181 | XenotypeDef | `HD_MexicanHelod.descriptionShort` | A robust, swift combat PMC helod with amphetamine dependency. | 튼튼하고 민첩한 전투 PMC 헤로드로, 암페타민 의존증이 있습니다. |

### Defs/Helod/Health/BTX.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 182 | HediffDef | `HD_BTXToxicity.stages.0.label` | mild | 경증 |
| 183 | HediffDef | `HD_BTXToxicity.stages.1.label` | sick | 중증 |
| 184 | HediffDef | `HD_BTXToxicity.stages.2.label` | severe | 심각 |
| 185 | HediffDef | `HD_BTXDeficiency.stages.0.label` | minor | 경미 |
| 186 | HediffDef | `HD_BTXDeficiency.stages.1.label` | deficiency | 결핍 |
| 187 | HediffDef | `HD_BTXDeficiency.stages.2.label` | critical | 위급 |
| 188 | HediffDef | `HD_BTXAddiction.stages.1.label` | withdrawal | 금단 |

### Defs/Helod/Pawns/PawnKinds.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 189 | PawnKindDef | `HD_GW_HelodRifleman.label` | Great War Helod rifleman | 대전기 헤로드 소총수 |
| 190 | HD_GW_HelodLeaderPawnKind | `HD_GW_HelodSquadLeader.label` | Great War Helod squad leader | 대전기 헤로드 부분대장 |
| 191 | PawnKindDef | `HD_GW_HelodScout.label` | Great War Helod scout | 대전기 헤로드 정찰병 |
| 192 | PawnKindDef | `HD_GW_HelodAssistantSquadLeader.label` | Great War Helod assistant squad leader | 대전기 헤로드 부분대장 보조 |
| 193 | PawnKindDef | `HD_GW_HelodAutomaticRifleman.label` | Great War Helod automatic rifleman | 대전기 헤로드 자동소총수 |
| 194 | PawnKindDef | `HD_GW_HelodMortarmanA.label` | Great War Helod mortarman A | 대전기 헤로드 박격포병 A |
| 195 | PawnKindDef | `HD_GW_HelodMortarmanB.label` | Great War Helod mortarman B | 대전기 헤로드 박격포병 B |
| 196 | PawnKindDef | `HD_GW_HelodMortarmanC.label` | Great War Helod mortarman C | 대전기 헤로드 박격포병 C |
| 197 | PawnKindDef | `HD_GW_HelodMortarShellBearer.label` | Great War Helod mortar ammunition bearer | 대전기 헤로드 박격포 탄약 운반병 |
| 198 | PawnKindDef | `HD_GW_HelodFieldRationBearer.label` | Great War Helod field ration bearer | 대전기 헤로드 야전식량 운반병 |

### Defs/Helod/Race/HelodRace.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 199 | ThingDef | `Helod.tools.0.label` | left fist | 왼주먹 |
| 200 | ThingDef | `Helod.tools.1.label` | right fist | 오른주먹 |
| 201 | ThingDef | `Helod.tools.2.label` | teeth | 이빨 |

### Defs/Helod/Traits/Traits_Helod.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 202 | TraitDef | `HD_RawBTXMadman.degreeDatas.0.label` | 미친놈 | 미친놈 |
| 203 | TraitDef | `HD_RawBTXMadman.degreeDatas.0.description` | {PAWN_nameDef} has a deranged taste for raw BTX fractions. Naphtha and similar unrefined BTX sources smell like medicine, fuel, and home all at once. | {PAWN_nameDef}은(는) 정제되지 않은 BTX 유분을 광적으로 좋아합니다. 나프타와 비슷한 미정제 BTX 원료에서 약품과 연료, 고향의 냄새를 동시에 느낍니다. |

### Defs/Items/Cigarette.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 204 | ThingDef | `HD_Cigarette.label` | cigarette | 담배 |
| 205 | ThingDef | `HD_Cigarette.description` | A paper-wrapped roll of cured leaf. It gives a brief, mild lift and leaves a trail of smoke and ash. | 말린 잎을 종이로 만 물건입니다. 잠시 가벼운 각성감을 주고 연기와 재를 남깁니다. |
| 206 | ThingDef | `HD_Cigarette.ingestible.ingestCommandString` | Smoke {0} | 피우기: {0} |
| 207 | ThingDef | `HD_Cigarette.ingestible.ingestReportString` | Smoking {0}. | 피우는 중: {0}. |
| 208 | ThingDef | `HD_CigaretteButt.label` | cigarette butt | 담배꽁초 |
| 209 | ThingDef | `HD_CigaretteButt.description` | A discarded cigarette butt that should be cleaned away. | 버려진 담배꽁초입니다. 청소해서 치워야 합니다. |
| 210 | HediffDef | `HD_NicotineRush.label` | nicotine buzz | 니코틴 각성 |
| 211 | HediffDef | `HD_NicotineRush.description` | A brief feeling of alert calm after smoking a cigarette. | 담배를 피운 뒤 잠시 느끼는 차분한 각성감입니다. |
| 212 | ChemicalDef | `HD_Nicotine.label` | nicotine | 니코틴 |
| 213 | NeedDef | `HD_Chemical_Nicotine.label` | nicotine | 니코틴 |
| 214 | NeedDef | `HD_Chemical_Nicotine.description` | Because of nicotine dependence, this person needs to smoke regularly to avoid withdrawal symptoms. | 니코틴 의존 때문에 금단 증상을 피하려면 정기적으로 담배를 피워야 합니다. |
| 215 | HediffDef | `HD_NicotineTolerance.label` | nicotine tolerance | 니코틴 내성 |
| 216 | HediffDef | `HD_NicotineTolerance.description` | A built-up tolerance to nicotine. Greater tolerance reduces the effect of each cigarette. | 누적된 니코틴 내성입니다. 내성이 높을수록 담배 한 개비의 효과가 줄어듭니다. |
| 217 | HediffDef | `HD_NicotineAddiction.label` | nicotine dependence | 니코틴 의존증 |
| 218 | HediffDef | `HD_NicotineAddiction.description` | A chemical dependence on nicotine. Going without cigarettes causes irritability and impaired concentration, but sustained abstinence will eventually resolve the dependence. | 니코틴에 대한 화학적 의존입니다. 담배를 피우지 않으면 짜증과 집중력 저하가 생기지만 금연을 계속하면 결국 의존증이 사라집니다. |
| 219 | HediffDef | `HD_NicotineAddiction.stages.1.label` | withdrawal | 금단 |

### Defs/ModernWar/GrenadeEffects_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 220 | ThingDef | `HD_FlashbangLightEffect.label` | flashbang light | 섬광탄 빛 |

### Defs/ModernWar/Hediffs_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 221 | HediffDef | `HD_WeaponFlashlightDazzle.label` | weapon-light dazzle | 무기 조명 눈부심 |
| 222 | HediffDef | `HD_WeaponFlashlightDazzle.description` | A high-intensity weapon light is shining directly into this pawn's eyes, temporarily reducing effective vision. | 고광도 무기 조명이 이 폰의 눈을 직접 비추어 일시적으로 시야를 떨어뜨립니다. |
| 223 | HediffDef | `HD_WeaponFlashlightDazzle.stages.0.label` | illuminated | 조명 노출 |
| 224 | HediffDef | `HD_WeaponFlashlightDazzle.stages.1.label` | dazzled | 눈부심 |
| 225 | HediffDef | `HD_WeaponFlashlightDazzle.stages.2.label` | heavily dazzled | 심한 눈부심 |

### Defs/ModernWar/Heliborne_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 226 | PawnsArrivalModeDef | `HD_MH60M_Heliborne.textEnemy` | A helicopter is inserting {0} from {1} by fast-rope! | 헬리콥터가 {1}에서 {0}을(를) 패스트로프로 투입하고 있습니다! |
| 227 | PawnsArrivalModeDef | `HD_MH60M_Heliborne.textFriendly` | A friendly helicopter is inserting {0} from {1} by fast-rope. | 우호적인 헬리콥터가 {1}에서 {0}을(를) 패스트로프로 투입하고 있습니다. |
| 228 | PawnsArrivalModeDef | `HD_MH60M_Heliborne.textWillArrive` | {0_pawnsPluralDef} will arrive by helicopter. | {0_pawnsPluralDef}이(가) 헬리콥터로 도착합니다. |
| 229 | ThingDef | `HD_MH60M_HeliborneAircraft.label` | MH-60M helicopter | MH-60M 헬리콥터 |

### Defs/ModernWar/Items/Ammunition_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 230 | ThingCategoryDef | `HD_SwitchbladeMunitions.label` | Switchblade munitions | Switchblade 탄약 |
| 231 | ThingDef | `HD_Switchblade600_Round.label` | Switchblade 600 round | Switchblade 600 탄 |
| 232 | ThingDef | `HD_Switchblade600_Round.description` | A sealed Switchblade 600 loitering munition packed with its wings folded for loading into a launch canister. | 발사관에 장전할 수 있도록 날개를 접어 밀봉 포장한 Switchblade 600 체공형 탄약입니다. |

### Defs/ModernWar/Items/Grenades_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 233 | ThingDef | `HD_M67_ClipEffect.label` | M67 safety clip | M67 안전 클립 |
| 234 | ThingDef | `HD_M67_PinEffect.label` | M67 safety pin | M67 안전핀 |
| 235 | ThingDef | `HD_M84_ClipEffect.label` | M84 safety clip | M84 안전 클립 |
| 236 | ThingDef | `HD_M84_PinEffect.label` | M84 safety pin | M84 안전핀 |
| 237 | ThingDef | `HD_M111_ClipEffect.label` | M111 safety clip | M111 안전 클립 |
| 238 | ThingDef | `HD_M111_PinEffect.label` | M111 safety pin | M111 안전핀 |

### Defs/ModernWar/Items/ModularWeaponCasings.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 239 | ThingDef | `HD_Mote_Shell55645.label` | 5.56x45mm cartridge case | 5.56×45mm 탄피 |
| 240 | ThingDef | `HD_Mote_Shell300BLK.label` | .300 BLK cartridge case | .300 BLK 탄피 |
| 241 | ThingDef | `HD_Mote_Shell3006.label` | .30-06 cartridge case | .30-06 탄피 |
| 242 | ThingDef | `HD_Mote_Shell919.label` | 9x19mm cartridge case | 9×19mm 탄피 |
| 243 | ThingDef | `HD_Mote_Shell45ACP.label` | .45 ACP cartridge case | .45 ACP 탄피 |
| 244 | ThingDef | `HD_Mote_Shell76251.label` | 7.62x51mm cartridge case | 7.62×51mm 탄피 |

### Defs/ModernWar/Items/ModularWeapons_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 245 | ThingCategoryDef | `HD_ModularWeaponParts.label` | modular weapon parts | 모듈식 무기 부품 |
| 246 | ThingDef | `HD_ModularPart_Bolt_AR15.label` | AR-15 bolt carrier group | AR-15 노리쇠 운반체 뭉치 |
| 247 | ThingDef | `HD_ModularPart_Bolt_AR15.description` | An internal AR-15 bolt carrier group animated through the firing cycle. | AR-15 노리쇠 운반체 뭉치입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 248 | ThingDef | `HD_ModularPart_Trigger_AR15.label` | AR-15 trigger | AR-15 방아쇠 |
| 249 | ThingDef | `HD_ModularPart_Trigger_AR15.description` | An internal AR-15 trigger animated when the weapon fires. | AR-15 방아쇠입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 250 | ThingDef | `HD_ModularPart_UpperReceiver_M4A1.label` | M4A1 upper receiver | M4A1 상부 총몸 |
| 251 | ThingDef | `HD_ModularPart_UpperReceiver_M4A1.comps.0.sockets.0.label` | barrel extension | 총열 연장부 |
| 252 | ThingDef | `HD_ModularPart_UpperReceiver_M4A1.comps.0.sockets.1.label` | handguard mount | 총열덮개 장착부 |
| 253 | ThingDef | `HD_ModularPart_UpperReceiver_M4A1.comps.0.sockets.2.label` | bolt carrier group | 노리쇠 운반체 뭉치 |
| 254 | ThingDef | `HD_ModularPart_UpperReceiver_M4A1.comps.0.sockets.3.label` | upper receiver top rail | 상부 총몸 상단 레일 |
| 255 | ThingDef | `HD_ModularPart_Barrel_AR15103.label` | 10.3 inch AR-15 barrel | 10.3인치 AR-15 총열 |
| 256 | ThingDef | `HD_ModularPart_Barrel_AR15103.comps.0.sockets.0.label` | gas block journal | 가스 블록 장착부 |
| 257 | ThingDef | `HD_ModularPart_Barrel_AR15103.comps.0.sockets.1.label` | muzzle thread | 총구 나사산 |
| 258 | ThingDef | `HD_ModularPart_GasBlock_MK12LP.label` | MK12 low-profile gas block | MK12 저상형 가스 블록 |
| 259 | ThingDef | `HD_ModularPart_Barrel_AR15300BLK103.label` | 10.3 inch .300 BLK AR-15 barrel | 10.3인치 .300 BLK AR-15 총열 |
| 260 | ThingDef | `HD_ModularPart_Barrel_AR15300BLK103.description` | A compact 10.3-inch AR-15 barrel chambered for .300 Blackout. | 10.3인치 .300 BLK AR-15 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 261 | ThingDef | `HD_ModularPart_Barrel_AR15300BLK103.comps.0.sockets.0.label` | pistol-length gas block journal | 권총 길이 가스 블록 장착부 |
| 262 | ThingDef | `HD_ModularPart_Barrel_AR15300BLK103.comps.0.sockets.1.label` | 5/8x24 muzzle thread | 5/8x24 총구 나사산 |
| 263 | ThingDef | `HD_ModularPart_GasBlock_300BLKLP.label` | .300 BLK low-profile gas block | .300 BLK 저상형 가스 블록 |
| 264 | ThingDef | `HD_ModularPart_GasBlock_300BLKLP.description` | A compact low-profile gas block positioned for a pistol-length .300 Blackout gas system. | .300 BLK 저상형 가스 블록입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 265 | ThingDef | `HD_ModularPart_Barrel_AR15145.label` | 14.5 inch AR-15 barrel | 14.5인치 AR-15 총열 |
| 266 | ThingDef | `HD_ModularPart_Barrel_AR15145.description` | A carbine-length 14.5-inch AR-15 barrel. | 14.5인치 AR-15 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 267 | ThingDef | `HD_ModularPart_Barrel_AR15145.comps.0.sockets.0.label` | gas block journal | 가스 블록 장착부 |
| 268 | ThingDef | `HD_ModularPart_Barrel_AR15145.comps.0.sockets.1.label` | muzzle thread | 총구 나사산 |
| 269 | ThingDef | `HD_ModularPart_Muzzle_HACNT4.label` | HAC NT4 muzzle device | HAC NT4 총구 장치 |
| 270 | ThingDef | `HD_ModularPart_Muzzle_HACNT4.comps.0.sockets.0.label` | NT4 suppressor interface | NT4 소음기 결합부 |
| 271 | ThingDef | `HD_ModularPart_Suppressor_BrightStrike4Prong.label` | BrightStrike four-prong suppressor | BrightStrike 4갈래 소음기 |
| 272 | ThingDef | `HD_ModularPart_Muzzle_BrightStrikeSF3P762.label` | BrightStrike SF3P 7.62 flash hider | BrightStrike SF3P 7.62 소염기 |
| 273 | ThingDef | `HD_ModularPart_Muzzle_BrightStrikeSF3P762.description` | A three-prong flash hider for 7.62 mm and .300 Blackout 5/8x24 muzzle threads. | BrightStrike SF3P 7.62 소염기입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 274 | ThingDef | `HD_ModularPart_Handguard_HACRISFDE.label` | HAC RIS FDE handguard | HAC RIS FDE 총열덮개 |
| 275 | ThingDef | `HD_ModularPart_Handguard_HACRISFDE.comps.0.sockets.0.label` | top rail front | 상단 전방 레일 |
| 276 | ThingDef | `HD_ModularPart_Handguard_HACRISFDE.comps.0.sockets.1.label` | side rail | 측면 레일 |
| 277 | ThingDef | `HD_ModularPart_Handguard_HACRISFDE.comps.0.sockets.2.label` | bottom rail | 하단 레일 |
| 278 | ThingDef | `HD_ModularPart_Optic_VOTechEXPS.label` | VOTech EXPS sight | VOTech EXPS 조준기 |
| 279 | ThingDef | `HD_ModularPart_Handguard_HACURXM4UP.label` | HAC URX M4 upper handguard | HAC URX M4 상부 총열덮개 |
| 280 | ThingDef | `HD_ModularPart_Handguard_HACURXM4UP.description` | The upper section of a compact two-piece HAC URX M4 rail handguard. | HAC URX M4 상부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 281 | ThingDef | `HD_ModularPart_Handguard_HACURXM4UP.comps.0.sockets.0.label` | URX M4 lower handguard interface | URX M4 하부 총열덮개 결합부 |
| 282 | ThingDef | `HD_ModularPart_Handguard_HACURXM4UP.comps.0.sockets.1.label` | top rail front | 상단 전방 레일 |
| 283 | ThingDef | `HD_ModularPart_Handguard_HACURXM4UP.comps.0.sockets.2.label` | side rail | 측면 레일 |
| 284 | ThingDef | `HD_ModularPart_HandguardExt_HACURXM4DOWN.label` | HAC URX M4 lower handguard | HAC URX M4 하부 총열덮개 |
| 285 | ThingDef | `HD_ModularPart_HandguardExt_HACURXM4DOWN.description` | The removable lower section of a compact HAC URX M4 rail handguard. | HAC URX M4 하부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 286 | ThingDef | `HD_ModularPart_HandguardExt_HACURXM4DOWN.comps.0.sockets.0.label` | continuous bottom rail | 연속형 하단 레일 |
| 287 | ThingDef | `HD_ModularPart_Magnifier_VOTechG33.label` | VOTech G33 magnifier | VOTech G33 배율 확대경 |
| 288 | ThingDef | `HD_ModularPart_Laser_ANPEQ15.label` | AN/PEQ-15 laser module | AN/PEQ-15 레이저 모듈 |
| 289 | ThingDef | `HD_ModularPart_Light_BrightStrikeM600.label` | BrightStrike M600 weapon light | BrightStrike M600 총기 조명 |
| 290 | ThingDef | `HD_ModularPart_Laser_IZLIDUltra.label` | IZLID Ultra laser module | IZLID Ultra 레이저 모듈 |
| 291 | ThingDef | `HD_ModularPart_Grip_DaltonDefenseVFG.label` | Dalton Defense vertical foregrip | Dalton Defense 수직 전방손잡이 |
| 292 | ThingDef | `HD_ModularPart_PistolGrip_IronFangA2.label` | IronFang A2 pistol grip | IronFang A2 권총손잡이 |
| 293 | ThingDef | `HD_ModularPart_RailPanel_HACURXShortLow.label` | HAC URX short rail panel | HAC URX 단축 레일 패널 |
| 294 | ThingDef | `HD_ModularPart_RailPanel_HACURXShortLow.description` | A short protective panel for a Picatinny rail. | HAC URX 단축 레일 패널입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 295 | ThingDef | `HD_ModularPart_BufferTube_Colt.label` | Colt buffer tube | Colt 버퍼 튜브 |
| 296 | ThingDef | `HD_ModularPart_BufferTube_Colt.comps.0.sockets.0.label` | adjustable carbine stock track | 조절식 카빈 개머리판 레일 |
| 297 | ThingDef | `HD_ModularPart_Stock_FieldFormMOEFDE.label` | FieldForm MOE FDE stock | FieldForm MOE FDE 개머리판 |
| 298 | ThingDef | `HD_ModularPart_Stock_M4.label` | M4 collapsible stock | M4 신축식 개머리판 |
| 299 | ThingDef | `HD_ModularPart_Stock_M4.description` | A conventional collapsible M4 carbine stock. | M4 신축식 개머리판입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 300 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.label` | modular M4 development carbine | 모듈식 M4 개발용 카빈 |
| 301 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.description` | A development weapon for authoring the tree-based modular attachment system. | 모듈식 M4 개발용 카빈입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 302 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.comps.0.sockets.0.label` | upper receiver interface | 상부 총몸 결합부 |
| 303 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.comps.0.sockets.1.label` | pistol grip interface | 권총손잡이 결합부 |
| 304 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.comps.0.sockets.2.label` | buffer tube interface | 버퍼 튜브 결합부 |
| 305 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.comps.0.sockets.3.label` | STANAG magazine well | STANAG 탄창 삽입구 |
| 306 | ThingDef | `HD_Gun_ModularM4_Test_Weapon.comps.0.sockets.4.label` | trigger group | 방아쇠 뭉치 |

### Defs/ModernWar/Items/ModularWeapons_M14_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 307 | ThingDef | `HD_ModularPart_Barrel_M1422.label` | M14 22 inch barrel | M14 22인치 총열 |
| 308 | ThingDef | `HD_ModularPart_Barrel_M1422.description` | M14 22 inch barrel for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 22인치 총열입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 309 | ThingDef | `HD_ModularPart_Barrel_M1422.comps.0.sockets.0.label` | muzzle | 총구 |
| 310 | ThingDef | `HD_ModularPart_Barrel_M1422.comps.0.sockets.1.label` | gas tube | 가스관 |
| 311 | ThingDef | `HD_ModularPart_Barrel_M1418.label` | M14 18 inch barrel | M14 18인치 총열 |
| 312 | ThingDef | `HD_ModularPart_Barrel_M1418.description` | M14 18 inch barrel for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 18인치 총열입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 313 | ThingDef | `HD_ModularPart_Barrel_M1418.comps.0.sockets.0.label` | muzzle | 총구 |
| 314 | ThingDef | `HD_ModularPart_Barrel_M1418.comps.0.sockets.1.label` | gas tube | 가스관 |
| 315 | ThingDef | `HD_ModularPart_Barrel_M1A16.label` | M1A SOCOM 16 barrel | M1A SOCOM 16 총열 |
| 316 | ThingDef | `HD_ModularPart_Barrel_M1A16.description` | M1A SOCOM 16 barrel for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M1A SOCOM 16 총열입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 317 | ThingDef | `HD_ModularPart_Barrel_M1A16.comps.0.sockets.0.label` | muzzle | 총구 |
| 318 | ThingDef | `HD_ModularPart_Barrel_M1A16.comps.0.sockets.1.label` | gas tube | 가스관 |
| 319 | ThingDef | `HD_ModularPart_Bolt_M14.label` | M14 bolt | M14 노리쇠 |
| 320 | ThingDef | `HD_ModularPart_Bolt_M14.description` | M14 bolt for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 노리쇠입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 321 | ThingDef | `HD_ModularPart_OperatingRod_M14.label` | M14 operating rod | M14 작동봉 |
| 322 | ThingDef | `HD_ModularPart_OperatingRod_M14.description` | M14 operating rod for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 작동봉입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 323 | ThingDef | `HD_ModularPart_TriggerGroup_M14.label` | M14 trigger group | M14 방아쇠 뭉치 |
| 324 | ThingDef | `HD_ModularPart_TriggerGroup_M14.description` | M14 trigger group for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 방아쇠 뭉치입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 325 | ThingDef | `HD_ModularPart_TriggerGroup_M14.comps.0.sockets.0.label` | trigger | 방아쇠 |
| 326 | ThingDef | `HD_ModularPart_TriggerGroup_M14.comps.0.sockets.1.label` | pistol grip interface | 권총손잡이 결합부 |
| 327 | ThingDef | `HD_ModularPart_Trigger_M14.label` | M14 trigger | M14 방아쇠 |
| 328 | ThingDef | `HD_ModularPart_Trigger_M14.description` | M14 trigger for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 방아쇠입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 329 | ThingDef | `HD_ModularPart_GasTube_M14.label` | M14 gas system | M14 가스 작동계 |
| 330 | ThingDef | `HD_ModularPart_GasTube_M14.description` | M14 gas system for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 가스 작동계입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 331 | ThingDef | `HD_ModularPart_GasTube_MK14EBR.label` | MK14 EBR gas system | MK14 EBR 가스 작동계 |
| 332 | ThingDef | `HD_ModularPart_GasTube_MK14EBR.description` | MK14 EBR gas system for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | MK14 EBR 가스 작동계입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 333 | ThingDef | `HD_ModularPart_GasTube_M1ASocom16.label` | M1A SOCOM 16 gas system | M1A SOCOM 16 가스 작동계 |
| 334 | ThingDef | `HD_ModularPart_GasTube_M1ASocom16.description` | M1A SOCOM 16 gas system for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M1A SOCOM 16 가스 작동계입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 335 | ThingDef | `HD_ModularPart_Stock_M14Wood.label` | M14 wooden stock | M14 목제 개머리판 |
| 336 | ThingDef | `HD_ModularPart_Stock_M14Wood.description` | M14 wooden stock for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 목제 개머리판입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 337 | ThingDef | `HD_ModularPart_Stock_M14Wood.comps.0.sockets.0.label` | wooden stock top cover | 목제 개머리판 상부 덮개 |
| 338 | ThingDef | `HD_ModularPart_StockExt_M14WoodTopCover.label` | M14 wooden stock top cover | M14 목제 개머리판 상부 덮개 |
| 339 | ThingDef | `HD_ModularPart_StockExt_M14WoodTopCover.description` | A wooden top cover fitted over the M14 stock. | M14 목제 개머리판 상부 덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 340 | ThingDef | `HD_ModularPart_Stock_Socom16.label` | SOCOM 16 stock | SOCOM 16 개머리판 |
| 341 | ThingDef | `HD_ModularPart_Stock_Socom16.description` | SOCOM 16 stock for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | SOCOM 16 개머리판입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 342 | ThingDef | `HD_ModularPart_Muzzle_M14.label` | M14 flash hider | M14 소염기 |
| 343 | ThingDef | `HD_ModularPart_Muzzle_M14.description` | M14 flash hider for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | M14 소염기입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 344 | ThingDef | `HD_ModularPart_Muzzle_SEI2000V.label` | SEI 2000V muzzle brake | SEI 2000V 제퇴기 |
| 345 | ThingDef | `HD_ModularPart_Muzzle_SEI2000V.description` | SEI 2000V muzzle brake for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | SEI 2000V 제퇴기입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 346 | ThingDef | `HD_ModularPart_Muzzle_Socom16.label` | SOCOM 16 muzzle brake | SOCOM 16 제퇴기 |
| 347 | ThingDef | `HD_ModularPart_Muzzle_Socom16.description` | SOCOM 16 muzzle brake for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | SOCOM 16 제퇴기입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 348 | ThingDef | `HD_ModularPart_Muzzle_Socom16Thread.label` | SOCOM 16 muzzle thread adapter | SOCOM 16 총구 나사산 어댑터 |
| 349 | ThingDef | `HD_ModularPart_Muzzle_Socom16Thread.description` | SOCOM 16 muzzle thread adapter for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | SOCOM 16 총구 나사산 어댑터입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 350 | ThingDef | `HD_ModularPart_Muzzle_Socom16Thread.comps.0.sockets.0.label` | muzzle | 총구 |
| 351 | ThingDef | `HD_ModularPart_Magazine_M1420.label` | 20-round M14 magazine | 20-탄 M14 탄창 |
| 352 | ThingDef | `HD_ModularPart_Magazine_M1420.description` | 20-round M14 magazine for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | 20-탄 M14 탄창입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 353 | ThingDef | `HD_ModularPart_Magazine_M1420.comps.0.sockets.0.label` | ammunition | 탄약 |
| 354 | ThingDef | `HD_ModularPart_Ammunition_762M80.label` | 7.62x51mm M80 ammunition | 7.62×51mm M80 탄약 |
| 355 | ThingDef | `HD_ModularPart_Ammunition_762M80.description` | 7.62x51mm M80 ammunition for the modular M14 family. Attachment placement can be adjusted in the in-game editor. | 7.62×51mm M80 탄약입니다. 부착 위치는 게임 내 편집기에서 조정할 수 있습니다. |
| 356 | ThingDef | `HD_ModularPart_Ammunition_762M80.comps.0.ammunitionType` | 7.62 M80 | 7.62 M80 |
| 357 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.label` | modular M14 development rifle | 모듈식 M14 개발용 소총 |
| 358 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.description` | A configurable M14-family rifle. Initial geometry and animation settings are editable in the attachment editor. | 모듈식 M14 개발용 소총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 359 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.tools.0.label` | stock | 개머리판 |
| 360 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.0.label` | barrel | 총열 |
| 361 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.1.label` | handguard interface | 총열덮개 결합부 |
| 362 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.2.label` | bolt | 노리쇠 |
| 363 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.3.label` | operating rod | 작동봉 |
| 364 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.4.label` | trigger group | 방아쇠 뭉치 |
| 365 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.5.label` | stock | 개머리판 |
| 366 | ThingDef | `HD_Gun_ModularM14_Test_Weapon.comps.0.sockets.6.label` | magazine | 탄창 |

### Defs/ModernWar/Items/ModularWeapons_M14EBR_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 367 | ThingDef | `HD_ModularPart_Handguard_MK14EBR.label` | MK14 EBR chassis handguard | MK14 EBR 섀시 총열덮개 |
| 368 | ThingDef | `HD_ModularPart_Handguard_MK14EBR.description` | A modular MK14 EBR chassis handguard with a continuous top rail. | MK14 EBR 섀시 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 369 | ThingDef | `HD_ModularPart_Handguard_MK14EBR.comps.0.sockets.0.label` | top rail extension interface | 상단 레일 연장 결합부 |
| 370 | ThingDef | `HD_ModularPart_Handguard_MK14EBR.comps.0.sockets.1.label` | MK14 handguard top rail | MK14 총열덮개 상단 레일 |
| 371 | ThingDef | `HD_ModularPart_HandguardExt_MK14EBRTopRail.label` | MK14 EBR receiver top rail | MK14 EBR 총몸 상단 레일 |
| 372 | ThingDef | `HD_ModularPart_HandguardExt_MK14EBRTopRail.description` | A receiver-to-handguard top rail extension for the MK14 EBR chassis. | MK14 EBR 총몸 상단 레일입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 373 | ThingDef | `HD_ModularPart_HandguardExt_MK14EBRTopRail.comps.0.sockets.0.label` | MK14 receiver top rail | MK14 총몸 상단 레일 |
| 374 | ThingDef | `HD_ModularPart_PistolGrip_MK14EBR.label` | MK14 EBR pistol grip | MK14 EBR 권총손잡이 |
| 375 | ThingDef | `HD_ModularPart_PistolGrip_MK14EBR.description` | An MK14 EBR chassis pistol-grip assembly. | MK14 EBR 권총손잡이입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 376 | ThingDef | `HD_ModularPart_PistolGrip_MK14EBR.comps.0.sockets.0.label` | MK14 EBR stock interface | MK14 EBR 개머리판 결합부 |
| 377 | ThingDef | `HD_ModularPart_Stock_MK14EBR.label` | MK14 EBR adjustable stock | MK14 EBR 조절식 개머리판 |
| 378 | ThingDef | `HD_ModularPart_Stock_MK14EBR.description` | An adjustable MK14 EBR chassis stock. | MK14 EBR 조절식 개머리판입니다. 모듈식 무기 구성에 장착할 수 있습니다. |

### Defs/ModernWar/Items/ModularWeapons_M16A4_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 379 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.label` | M16A4 upper receiver | M16A4 상부 총몸 |
| 380 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.description` | A flat-top M16A4 upper receiver for the modular AR assembly. | M16A4 상부 총몸입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 381 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.comps.0.sockets.0.label` | barrel extension | 총열 연장부 |
| 382 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.comps.0.sockets.1.label` | handguard mount | 총열덮개 장착부 |
| 383 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.comps.0.sockets.2.label` | bolt carrier group | 노리쇠 운반체 뭉치 |
| 384 | ThingDef | `HD_ModularPart_UpperReceiver_M16A4.comps.0.sockets.3.label` | upper receiver top rail | 상부 총몸 상단 레일 |
| 385 | ThingDef | `HD_ModularPart_Barrel_AR1520.label` | 20 inch AR-15 barrel | 20인치 AR-15 총열 |
| 386 | ThingDef | `HD_ModularPart_Barrel_AR1520.description` | A full-length 20-inch AR-15 barrel. | 20인치 AR-15 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 387 | ThingDef | `HD_ModularPart_Barrel_AR1520.comps.0.sockets.0.label` | gas block journal | 가스 블록 장착부 |
| 388 | ThingDef | `HD_ModularPart_Barrel_AR1520.comps.0.sockets.1.label` | muzzle thread | 총구 나사산 |
| 389 | ThingDef | `HD_ModularPart_GasBlock_AR15.label` | AR-15 front sight gas block | AR-15 가늠쇠 일체형 가스 블록 |
| 390 | ThingDef | `HD_ModularPart_GasBlock_AR15.description` | An AR-15 gas block with an integrated front sight tower. | AR-15 가늠쇠 일체형 가스 블록입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 391 | ThingDef | `HD_ModularPart_Handguard_HACURXUP.label` | HAC URX upper handguard | HAC URX 상부 총열덮개 |
| 392 | ThingDef | `HD_ModularPart_Handguard_HACURXUP.description` | The upper section of a two-piece HAC URX rail handguard. | HAC URX 상부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 393 | ThingDef | `HD_ModularPart_Handguard_HACURXUP.comps.0.sockets.0.label` | URX lower handguard interface | URX 하부 총열덮개 결합부 |
| 394 | ThingDef | `HD_ModularPart_Handguard_HACURXUP.comps.0.sockets.1.label` | top rail front | 상단 전방 레일 |
| 395 | ThingDef | `HD_ModularPart_Handguard_HACURXUP.comps.0.sockets.2.label` | upper side rail | 상부 측면 레일 |
| 396 | ThingDef | `HD_ModularPart_HandguardExt_HACURXDOWN.label` | HAC URX lower handguard | HAC URX 하부 총열덮개 |
| 397 | ThingDef | `HD_ModularPart_HandguardExt_HACURXDOWN.description` | The removable lower section of a HAC URX rail handguard. | HAC URX 하부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 398 | ThingDef | `HD_ModularPart_HandguardExt_HACURXDOWN.comps.0.sockets.0.label` | continuous bottom rail | 연속형 하단 레일 |
| 399 | ThingDef | `HD_ModularPart_Magazine_STANAG.label` | STANAG magazine | STANAG 탄창 |
| 400 | ThingDef | `HD_ModularPart_Magazine_STANAG.description` | A detachable STANAG-pattern rifle magazine represented as a modular weapon part. | STANAG 탄창입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 401 | ThingDef | `HD_ModularPart_Magazine_STANAG.comps.0.sockets.0.label` | magazine floorplate | 탄창 바닥판 |
| 402 | ThingDef | `HD_ModularPart_Magazine_STANAG.comps.0.sockets.1.label` | magazine body wrap | 탄창 몸체 랩 |
| 403 | ThingDef | `HD_ModularPart_Magazine_STANAG.comps.0.sockets.2.label` | loaded ammunition | 장전 탄약 |
| 404 | ThingDef | `HD_ModularPart_MagazineExt_StanagGrip.label` | STANAG magazine pull grip | STANAG 탄창 당김 손잡이 |
| 405 | ThingDef | `HD_ModularPart_MagazineExt_StanagGrip.description` | A pull grip fitted to a STANAG magazine floorplate. | STANAG 탄창 당김 손잡이입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 406 | ThingDef | `HD_ModularPart_MagazineWrap_StanagTapeBlue.label` | blue STANAG magazine tape | 파란색 STANAG 탄창 식별 테이프 |
| 407 | ThingDef | `HD_ModularPart_MagazineWrap_StanagTapeBlue.description` | Blue identification tape wrapped around a full-size STANAG magazine body. | 파란색 STANAG 탄창 식별 테이프입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 408 | ThingDef | `HD_ModularPart_BufferTube_ColtA2.label` | Colt A2 rifle buffer tube | Colt A2 소총 버퍼 튜브 |
| 409 | ThingDef | `HD_ModularPart_BufferTube_ColtA2.description` | A fixed-stock rifle buffer tube for an AR-pattern lower receiver. | Colt A2 소총 버퍼 튜브입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 410 | ThingDef | `HD_ModularPart_BufferTube_ColtA2.comps.0.sockets.0.label` | fixed stock placement track | 고정형 개머리판 배치 레일 |
| 411 | ThingDef | `HD_ModularPart_Stock_IronFangA2.label` | IronFang A2 stock | IronFang A2 개머리판 |
| 412 | ThingDef | `HD_ModularPart_Stock_IronFangA2.description` | A fixed A2-pattern rifle stock. | IronFang A2 개머리판입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 413 | ThingDef | `HD_ModularPart_OpticMount_LumiconTA51.label` | Lumicon TA51 rail mount | Lumicon TA51 레일 마운트 |
| 414 | ThingDef | `HD_ModularPart_OpticMount_LumiconTA51.description` | A Picatinny mounting base for compatible Lumicon combat optics. | Lumicon TA51 레일 마운트입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 415 | ThingDef | `HD_ModularPart_OpticMount_LumiconTA51.comps.0.sockets.0.label` | Lumicon scope interface | Lumicon 조준경 결합부 |
| 416 | ThingDef | `HD_ModularPart_Scope_LumiconTA11.label` | Lumicon TA11 scope | Lumicon TA11 조준경 |
| 417 | ThingDef | `HD_ModularPart_Scope_LumiconTA11.description` | A fixed-power combat optic for a compatible TA-series mount. | Lumicon TA11 조준경입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 418 | ThingDef | `HD_ModularPart_Grip_HACVFG.label` | HAC vertical foregrip | HAC 수직 전방손잡이 |
| 419 | ThingDef | `HD_ModularPart_Grip_HACVFG.description` | A compact Picatinny vertical foregrip. | HAC 수직 전방손잡이입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 420 | ThingDef | `HD_ModularPart_RailPanel_HACURXLow.label` | HAC URX lower rail panel | HAC URX 하부 레일 패널 |
| 421 | ThingDef | `HD_ModularPart_RailPanel_HACURXLow.description` | A protective panel for a lower Picatinny rail. | HAC URX 하부 레일 패널입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 422 | ThingDef | `HD_ModularPart_RailPanel_HACURXMid.label` | HAC URX side rail panel | HAC URX 측면 레일 패널 |
| 423 | ThingDef | `HD_ModularPart_RailPanel_HACURXMid.description` | A protective panel for a side Picatinny rail. | HAC URX 측면 레일 패널입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 424 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.label` | modular M16A4 development rifle | 모듈식 M16A4 개발용 소총 |
| 425 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.description` | A development rifle for authoring the expanded modular part set. | 모듈식 M16A4 개발용 소총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 426 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.comps.0.sockets.0.label` | upper receiver interface | 상부 총몸 결합부 |
| 427 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.comps.0.sockets.1.label` | pistol grip interface | 권총손잡이 결합부 |
| 428 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.comps.0.sockets.2.label` | buffer tube interface | 버퍼 튜브 결합부 |
| 429 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.comps.0.sockets.3.label` | STANAG magazine well | STANAG 탄창 삽입구 |
| 430 | ThingDef | `HD_Gun_ModularM16A4_Test_Weapon.comps.0.sockets.4.label` | trigger group | 방아쇠 뭉치 |

### Defs/ModernWar/Items/ModularWeapons_M16Legacy_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 431 | ThingDef | `HD_ModularPart_UpperReceiver_M16A1.label` | M16A1 upper receiver | M16A1 상부 총몸 |
| 432 | ThingDef | `HD_ModularPart_UpperReceiver_M16A1.description` | A fixed-carry-handle M16A1 upper receiver. | M16A1 상부 총몸입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 433 | ThingDef | `HD_ModularPart_UpperReceiver_M16A1.comps.0.sockets.0.label` | barrel extension | 총열 연장부 |
| 434 | ThingDef | `HD_ModularPart_UpperReceiver_M16A1.comps.0.sockets.1.label` | handguard mount | 총열덮개 장착부 |
| 435 | ThingDef | `HD_ModularPart_UpperReceiver_M16A1.comps.0.sockets.2.label` | bolt carrier group | 노리쇠 운반체 뭉치 |
| 436 | ThingDef | `HD_ModularPart_Handguard_M16A1.label` | M16A1 triangular handguard | M16A1 삼각형 총열덮개 |
| 437 | ThingDef | `HD_ModularPart_Handguard_M16A1.description` | A full triangular M16A1 handguard assembly. | M16A1 삼각형 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 438 | ThingDef | `HD_ModularPart_UpperReceiver_M16A2.label` | M16A2 upper receiver | M16A2 상부 총몸 |
| 439 | ThingDef | `HD_ModularPart_UpperReceiver_M16A2.description` | A fixed-carry-handle M16A2 upper receiver. | M16A2 상부 총몸입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 440 | ThingDef | `HD_ModularPart_UpperReceiver_M16A2.comps.0.sockets.0.label` | barrel extension | 총열 연장부 |
| 441 | ThingDef | `HD_ModularPart_UpperReceiver_M16A2.comps.0.sockets.1.label` | handguard mount | 총열덮개 장착부 |
| 442 | ThingDef | `HD_ModularPart_UpperReceiver_M16A2.comps.0.sockets.2.label` | bolt carrier group | 노리쇠 운반체 뭉치 |
| 443 | ThingDef | `HD_ModularPart_Handguard_M16A2UP.label` | M16A2 upper handguard | M16A2 상부 총열덮개 |
| 444 | ThingDef | `HD_ModularPart_Handguard_M16A2UP.description` | The upper half of a two-piece M16A2 handguard. | M16A2 상부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 445 | ThingDef | `HD_ModularPart_Handguard_M16A2UP.comps.0.sockets.0.label` | M16A2 lower handguard interface | M16A2 하부 총열덮개 결합부 |
| 446 | ThingDef | `HD_ModularPart_HandguardExt_M16A2DOWN.label` | M16A2 lower handguard | M16A2 하부 총열덮개 |
| 447 | ThingDef | `HD_ModularPart_HandguardExt_M16A2DOWN.description` | The removable lower half of a two-piece M16A2 handguard. | M16A2 하부 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 448 | ThingDef | `HD_ModularPart_Barrel_AR1520Pencil.label` | 20 inch AR-15 pencil barrel | 20인치 AR-15 펜슬 총열 |
| 449 | ThingDef | `HD_ModularPart_Barrel_AR1520Pencil.description` | A lightweight 20-inch pencil-profile AR-15 barrel. | 20인치 AR-15 펜슬 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 450 | ThingDef | `HD_ModularPart_Barrel_AR1520Pencil.comps.0.sockets.0.label` | gas block journal | 가스 블록 장착부 |
| 451 | ThingDef | `HD_ModularPart_Barrel_AR1520Pencil.comps.0.sockets.1.label` | muzzle thread | 총구 나사산 |
| 452 | ThingDef | `HD_ModularPart_Muzzle_M16A1.label` | M16A1 flash suppressor | M16A1 소염기 |
| 453 | ThingDef | `HD_ModularPart_Muzzle_M16A1.description` | An M16A1 birdcage flash suppressor for a 5.56 mm muzzle thread. | M16A1 소염기입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 454 | ThingDef | `HD_ModularPart_Magazine_STANAG20.label` | 20-round STANAG magazine | 20-탄 STANAG 탄창 |
| 455 | ThingDef | `HD_ModularPart_Magazine_STANAG20.description` | A compact twenty-round STANAG-pattern rifle magazine. | 20-탄 STANAG 탄창입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 456 | ThingDef | `HD_ModularPart_Magazine_STANAG20.comps.0.sockets.0.label` | loaded ammunition | 장전 탄약 |
| 457 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.label` | modular M16A1 development rifle | 모듈식 M16A1 개발용 소총 |
| 458 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.description` | A development rifle for authoring the modular M16A1 part set. | 모듈식 M16A1 개발용 소총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 459 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.comps.0.sockets.0.label` | upper receiver interface | 상부 총몸 결합부 |
| 460 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.comps.0.sockets.1.label` | pistol grip interface | 권총손잡이 결합부 |
| 461 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.comps.0.sockets.2.label` | buffer tube interface | 버퍼 튜브 결합부 |
| 462 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.comps.0.sockets.3.label` | STANAG magazine well | STANAG 탄창 삽입구 |
| 463 | ThingDef | `HD_Gun_ModularM16A1_Test_Weapon.comps.0.sockets.4.label` | trigger group | 방아쇠 뭉치 |
| 464 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.label` | modular M16A2 development rifle | 모듈식 M16A2 개발용 소총 |
| 465 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.description` | A development rifle for authoring the modular M16A2 part set. | 모듈식 M16A2 개발용 소총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 466 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.comps.0.sockets.0.label` | upper receiver interface | 상부 총몸 결합부 |
| 467 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.comps.0.sockets.1.label` | pistol grip interface | 권총손잡이 결합부 |
| 468 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.comps.0.sockets.2.label` | buffer tube interface | 버퍼 튜브 결합부 |
| 469 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.comps.0.sockets.3.label` | STANAG magazine well | STANAG 탄창 삽입구 |
| 470 | ThingDef | `HD_Gun_ModularM16A2_Test_Weapon.comps.0.sockets.4.label` | trigger group | 방아쇠 뭉치 |

### Defs/ModernWar/Items/ModularWeapons_M1911_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 471 | ThingDef | `HD_ModularPart_Slide_M1911A1_UP.label` | M1911A1 slide | M1911A1 슬라이드 |
| 472 | ThingDef | `HD_ModularPart_Slide_M1911A1_DOWN.label` | Slide M1911A1 DOWN | M1911A1 하부 슬라이드 |
| 473 | ThingDef | `HD_ModularPart_Barrel_M1911A1.label` | Barrel M1911A1 | 총열 M1911A1 |
| 474 | ThingDef | `HD_ModularPart_Barrel_M1911A1.comps.0.sockets.0.label` | muzzle | 총구 |
| 475 | ThingDef | `HD_ModularPart_Hammer_M1911A1.label` | Hammer M1911A1 | 해머 M1911A1 |
| 476 | ThingDef | `HD_ModularPart_Safety_M1911A1Grip.label` | Safety M1911A1Grip | 안전장치 M1911A1손잡이 |
| 477 | ThingDef | `HD_ModularPart_Trigger_M1911A1.label` | Trigger M1911A1 | 방아쇠 M1911A1 |
| 478 | ThingDef | `HD_ModularPart_PistolGrip_1911GI.label` | PistolGrip 1911GI | 권총손잡이 1911GI |
| 479 | ThingDef | `HD_ModularPart_Magazine_1911GI7.label` | Magazine 1911GI7 | 탄창 1911GI7 |
| 480 | ThingDef | `HD_ModularPart_Ammunition_45ACPM1911.label` | .45 ACP M1911 ammunition | .45 ACP M1911 탄약 |
| 481 | ThingDef | `HD_ModularPart_Ammunition_45ACPM1911.comps.0.ammunitionType` | .45 ACP | .45 ACP |
| 482 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.label` | modular M1911A1 development pistol | 모듈식 M1911A1 개발용 권총 |
| 483 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.description` | A modular .45 ACP pistol with a reciprocating slide, tilting barrel, hammer and grip safety. | 모듈식 M1911A1 개발용 권총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 484 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.0.label` | slide | 슬라이드 |
| 485 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.1.label` | barrel | 총열 |
| 486 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.2.label` | hammer | 해머 |
| 487 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.3.label` | grip safety | 손잡이 안전장치 |
| 488 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.4.label` | trigger | 방아쇠 |
| 489 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.5.label` | grip | 손잡이 |
| 490 | ThingDef | `HD_Gun_ModularM1911_Test_Weapon.comps.0.sockets.6.label` | magazine | 탄창 |

### Defs/ModernWar/Items/ModularWeapons_MP5_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 491 | ThingDef | `HD_ModularPart_CockingTube_MP5.label` | MP5 cocking tube | MP5 장전관 |
| 492 | ThingDef | `HD_ModularPart_CockingTube_MP5.description` | The forward receiver extension containing the MP5 cocking tube and charging-handle track. | MP5 장전관입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 493 | ThingDef | `HD_ModularPart_CockingTube_MP5.comps.0.sockets.0.label` | MP5 cocking-tube front sight base | MP5 장전관 가늠쇠 받침 |
| 494 | ThingDef | `HD_ModularPart_CockingTube_MP5SD.label` | MP5SD cocking tube | MP5SD 장전관 |
| 495 | ThingDef | `HD_ModularPart_CockingTube_MP5SD.description` | The MP5SD receiver extension containing the cocking tube and its dedicated front-sight interface. | MP5SD 장전관입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 496 | ThingDef | `HD_ModularPart_CockingTube_MP5SD.comps.0.sockets.0.label` | MP5SD cocking-tube front sight base | MP5SD 장전관 가늠쇠 받침 |
| 497 | ThingDef | `HD_ModularPart_Barrel_MP5.label` | MP5 8.9 inch barrel | MP5 8.9인치 총열 |
| 498 | ThingDef | `HD_ModularPart_Barrel_MP5.description` | A standard 9x19mm MP5 barrel and trunnion assembly. | MP5 8.9인치 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 499 | ThingDef | `HD_ModularPart_Barrel_MP5.comps.0.sockets.0.label` | MP5 bare muzzle | MP5 노출 총구 |
| 500 | ThingDef | `HD_ModularPart_Barrel_MP5.comps.0.sockets.1.label` | standard MP5 handguard interface | 표준형 MP5 총열덮개 결합부 |
| 501 | ThingDef | `HD_ModularPart_Barrel_MP5SD.label` | MP5SD ported barrel | MP5SD 포트형 총열 |
| 502 | ThingDef | `HD_ModularPart_Barrel_MP5SD.description` | A short ported MP5SD barrel intended for use with its integral suppressor. | MP5SD 포트형 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 503 | ThingDef | `HD_ModularPart_Barrel_MP5SD.comps.0.sockets.0.label` | MP5SD barrel muzzle | MP5SD 총열 총구 |
| 504 | ThingDef | `HD_ModularPart_Barrel_MP5SD.comps.0.sockets.1.label` | MP5SD integral suppressor thread | MP5SD 일체형 소음기 나사산 |
| 505 | ThingDef | `HD_ModularPart_Barrel_MP5SD.comps.0.sockets.2.label` | MP5SD handguard interface | MP5SD 총열덮개 결합부 |
| 506 | ThingDef | `HD_ModularPart_Handguard_MP5Default.label` | MP5 slim handguard | MP5 슬림 총열덮개 |
| 507 | ThingDef | `HD_ModularPart_Handguard_MP5Default.description` | The conventional slim polymer MP5 handguard. | MP5 슬림 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 508 | ThingDef | `HD_ModularPart_Handguard_MP5SD.label` | MP5SD handguard | MP5SD 총열덮개 |
| 509 | ThingDef | `HD_ModularPart_Handguard_MP5SD.description` | A wide polymer handguard shaped around the MP5SD integral suppressor. | MP5SD 총열덮개입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 510 | ThingDef | `HD_ModularPart_Suppressor_MP5SD.label` | MP5SD integral suppressor | MP5SD 일체형 소음기 |
| 511 | ThingDef | `HD_ModularPart_Suppressor_MP5SD.description` | The large-volume integral suppressor used with the MP5SD ported barrel. | MP5SD 일체형 소음기입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 512 | ThingDef | `HD_ModularPart_FrontSight_MP5.label` | MP5 front sight | MP5 가늠쇠 |
| 513 | ThingDef | `HD_ModularPart_FrontSight_MP5.description` | A hooded MP5 front sight assembly for the standard barrel. | MP5 가늠쇠입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 514 | ThingDef | `HD_ModularPart_FrontSight_MP5SD.label` | MP5SD front sight | MP5SD 가늠쇠 |
| 515 | ThingDef | `HD_ModularPart_FrontSight_MP5SD.description` | A shortened hooded front sight base for the MP5SD assembly. | MP5SD 가늠쇠입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 516 | ThingDef | `HD_ModularPart_RearSight_MP5.label` | MP5 drum rear sight | MP5 회전식 가늠자 |
| 517 | ThingDef | `HD_ModularPart_RearSight_MP5.description` | The adjustable rotary diopter rear sight used by the MP5 family. | MP5 회전식 가늠자입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 518 | ThingDef | `HD_ModularPart_OpticRail_MP5.label` | MP5 upper Picatinny rail | MP5 상부 피카티니 레일 |
| 519 | ThingDef | `HD_ModularPart_OpticRail_MP5.description` | A claw-mounted upper Picatinny rail for MP5-pattern receivers. | MP5 상부 피카티니 레일입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 520 | ThingDef | `HD_ModularPart_OpticRail_MP5.comps.0.sockets.0.label` | MP5 upper Picatinny rail | MP5 상부 피카티니 레일 |
| 521 | ThingDef | `HD_ModularPart_Bolt_MP5.label` | MP5 bolt group | MP5 노리쇠 뭉치 |
| 522 | ThingDef | `HD_ModularPart_Bolt_MP5.description` | A roller-delayed MP5 bolt group animated through the firing cycle. | MP5 노리쇠 뭉치입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 523 | ThingDef | `HD_ModularPart_Trigger_MP5.label` | MP5 trigger | MP5 방아쇠 |
| 524 | ThingDef | `HD_ModularPart_Trigger_MP5.description` | An MP5 fire-control trigger animated when the weapon fires. | MP5 방아쇠입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 525 | ThingDef | `HD_ModularPart_TriggerHousing_MP5A3.label` | MP5A3 trigger housing and pistol grip | MP5A3 방아쇠 하우징 및 권총손잡이 |
| 526 | ThingDef | `HD_ModularPart_TriggerHousing_MP5A3.description` | A polymer SEF trigger housing and pistol grip for the MP5 family. | MP5A3 방아쇠 하우징 및 권총손잡이입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 527 | ThingDef | `HD_ModularPart_Magazine_MP530.label` | 30-round MP5 magazine | 30-탄 MP5 탄창 |
| 528 | ThingDef | `HD_ModularPart_Magazine_MP530.description` | A curved thirty-round 9x19mm magazine for MP5-pattern weapons. | 30-탄 MP5 탄창입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 529 | ThingDef | `HD_ModularPart_Magazine_MP530.comps.0.sockets.0.label` | loaded 9x19mm ammunition | 장전된 9×19mm 탄약 |
| 530 | ThingDef | `HD_ModularPart_Stock_MP5A2.label` | MP5A2 fixed stock | MP5A2 고정형 개머리판 |
| 531 | ThingDef | `HD_ModularPart_Stock_MP5A2.description` | A fixed polymer shoulder stock for MP5-pattern receivers. | MP5A2 고정형 개머리판입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 532 | ThingDef | `HD_ModularPart_Stock_MP5A3.label` | MP5A3 retractable stock | MP5A3 인입식 개머리판 |
| 533 | ThingDef | `HD_ModularPart_Stock_MP5A3.description` | A retractable metal shoulder stock for MP5-pattern receivers. | MP5A3 인입식 개머리판입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 534 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.label` | modular MP5 development submachine gun | 모듈식 MP5 개발용 기관단총 |
| 535 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.description` | A roller-delayed 9x19mm submachine gun built from independently configurable MP5-family components. | 모듈식 MP5 개발용 기관단총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 536 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.0.label` | MP5 cocking-tube interface | MP5 장전관 결합부 |
| 537 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.1.label` | MP5 receiver barrel trunnion | MP5 총몸 총열 트러니언 |
| 538 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.2.label` | MP5 bolt group | MP5 노리쇠 뭉치 |
| 539 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.3.label` | MP5 rear sight base | MP5 가늠자 받침 |
| 540 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.4.label` | MP5 receiver claw mount | MP5 총몸 클로 마운트 |
| 541 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.5.label` | MP5 trigger housing interface | MP5 방아쇠 하우징 결합부 |
| 542 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.6.label` | MP5 magazine well | MP5 탄창 삽입구 |
| 543 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.7.label` | MP5 stock interface | MP5 개머리판 결합부 |
| 544 | ThingDef | `HD_Gun_ModularMP5_Test_Weapon.comps.0.sockets.8.label` | MP5 trigger group | MP5 방아쇠 뭉치 |

### Defs/ModernWar/Items/ModularWeapons_P320_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 545 | ThingDef | `HD_ModularPart_Receiver_P320.label` | P320 full-size grip module | P320 풀사이즈 손잡이 모듈 |
| 546 | ThingDef | `HD_ModularPart_Receiver_P320.description` | A standard full-size P320 grip and dust-cover module. | P320 풀사이즈 손잡이 모듈입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 547 | ThingDef | `HD_ModularPart_Receiver_P320.comps.0.sockets.0.label` | P320 lower rail | P320 하부 레일 |
| 548 | ThingDef | `HD_ModularPart_Receiver_FluxRaiderKit.label` | Flux Raider P320 chassis | Flux Raider P320 섀시 |
| 549 | ThingDef | `HD_ModularPart_Receiver_FluxRaiderKit.description` | A PDW chassis for a P320 fire-control unit, with stock and accessory rails. | Flux Raider P320 섀시입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 550 | ThingDef | `HD_ModularPart_Receiver_FluxRaiderKit.comps.0.sockets.0.label` | Flux stock extension track | Flux 개머리판 연장 레일 |
| 551 | ThingDef | `HD_ModularPart_Receiver_FluxRaiderKit.comps.0.sockets.1.label` | Flux upper rail | Flux 상부 레일 |
| 552 | ThingDef | `HD_ModularPart_Receiver_FluxRaiderKit.comps.0.sockets.2.label` | Flux lower rail | Flux 하부 레일 |
| 553 | ThingDef | `HD_ModularPart_Slide_P32047.label` | P320 4.7-inch slide | P320 4.7인치 슬라이드 |
| 554 | ThingDef | `HD_ModularPart_Slide_P32047.description` | A full-size P320 slide whose upper and lower artwork moves as one part. | P320 4.7인치 슬라이드입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 555 | ThingDef | `HD_ModularPart_Slide_P32047.comps.0.sockets.0.label` | P320 rear sight dovetail | P320 가늠자 도브테일 |
| 556 | ThingDef | `HD_ModularPart_Slide_P32047.comps.0.sockets.1.label` | P320 front sight dovetail | P320 가늠쇠 도브테일 |
| 557 | ThingDef | `HD_ModularPart_Barrel_P32047.label` | P320 4.7-inch barrel | P320 4.7인치 총열 |
| 558 | ThingDef | `HD_ModularPart_Barrel_P32047.description` | A standard full-size 9x19mm P320 barrel. | P320 4.7인치 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 559 | ThingDef | `HD_ModularPart_Barrel_P32047Threaded.label` | P320 4.7-inch threaded barrel | P320 4.7인치 나사산형 총열 |
| 560 | ThingDef | `HD_ModularPart_Barrel_P32047Threaded.description` | A threaded full-size 9x19mm P320 barrel. | P320 4.7인치 나사산형 총열입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 561 | ThingDef | `HD_ModularPart_Barrel_P32047Threaded.comps.0.sockets.0.label` | 1/2x28 muzzle thread | 1/2x28 총구 나사산 |
| 562 | ThingDef | `HD_ModularPart_Trigger_P320Flat.label` | P320 flat trigger | P320 평면형 방아쇠 |
| 563 | ThingDef | `HD_ModularPart_IronSight_P320Rear.label` | P320 rear iron sight | P320 기계식 가늠자 |
| 564 | ThingDef | `HD_ModularPart_IronSight_P320Front.label` | P320 front iron sight | P320 기계식 가늠쇠 |
| 565 | ThingDef | `HD_ModularPart_Stock_FluxRaiderKit.label` | Flux Raider folding stock | Flux Raider 접이식 개머리판 |
| 566 | ThingDef | `HD_ModularPart_Mount_T2Low.label` | T2 low Picatinny mount | T2 저상형 피카티니 마운트 |
| 567 | ThingDef | `HD_ModularPart_Mount_T2Low.comps.0.sockets.0.label` | T2 optic footprint | T2 광학장비 장착 규격 |
| 568 | ThingDef | `HD_ModularPart_Optic_PointSightT2.label` | PointSight T2 reflex sight | Point조준기 T2 반사식 조준기 |
| 569 | ThingDef | `HD_ModularPart_Laser_DBALPL.label` | DBAL-PL pistol laser | DBAL-PL 권총용 레이저 |
| 570 | ThingDef | `HD_ModularPart_Muzzle_9mmThreadProtector.label` | 9mm thread protector | 9mm 나사산 보호캡 |
| 571 | ThingDef | `HD_ModularPart_Suppressor_HelveticSRD9.label` | Helvetic SRD9 suppressor | Helvetic SRD9 소음기 |
| 572 | ThingDef | `HD_ModularPart_Magazine_P32017.label` | 17-round P320 magazine | 17-탄 P320 탄창 |
| 573 | ThingDef | `HD_ModularPart_Magazine_P32021.label` | 21-round P320 magazine | 21-탄 P320 탄창 |
| 574 | ThingDef | `HD_ModularPart_Magazine_P32030.label` | 30-round P320 magazine | 30-탄 P320 탄창 |
| 575 | ThingDef | `HD_ModularPart_Ammunition_919M882_P320.label` | 9x19mm M882 P320 ammunition | 9×19mm M882 P320 탄약 |
| 576 | ThingDef | `HD_ModularPart_Ammunition_919M882_P320.comps.0.ammunitionType` | 9x19 M882 | 9x19 M882 |
| 577 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.label` | modular P320 development pistol | 모듈식 P320 개발용 권총 |
| 578 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.description` | A modular full-size P320 pistol built around a serialized fire-control unit. | 모듈식 P320 개발용 권총입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 579 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.comps.0.sockets.0.label` | P320 grip/chassis module | P320 손잡이·섀시 모듈 |
| 580 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.comps.0.sockets.1.label` | P320 slide | P320 슬라이드 |
| 581 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.comps.0.sockets.2.label` | P320 barrel | P320 총열 |
| 582 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.comps.0.sockets.3.label` | P320 trigger | P320 방아쇠 |
| 583 | ThingDef | `HD_Gun_ModularP320_Test_Weapon.comps.0.sockets.4.label` | P320 magazine well | P320 탄창 삽입구 |
| 584 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.label` | modular Flux Raider development PDW | 모듈식 Flux Raider 개발용 개인방어화기 |
| 585 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.description` | A P320 fire-control unit installed in a Flux Raider chassis with a threaded barrel and accessory package. | 모듈식 Flux Raider 개발용 개인방어화기입니다. 모듈식 무기 구성에 장착할 수 있습니다. |
| 586 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.comps.0.sockets.0.label` | P320 grip/chassis module | P320 손잡이·섀시 모듈 |
| 587 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.comps.0.sockets.1.label` | P320 slide | P320 슬라이드 |
| 588 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.comps.0.sockets.2.label` | P320 barrel | P320 총열 |
| 589 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.comps.0.sockets.3.label` | P320 trigger | P320 방아쇠 |
| 590 | ThingDef | `HD_Gun_ModularFluxRaider_Test_Weapon.comps.0.sockets.4.label` | P320 magazine well | P320 탄창 삽입구 |

### Defs/ModernWar/Items/RifleAmmunition_Development.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 591 | ThingDef | `HD_Bullet_300BLKTACTX_Proj.label` | .300 BLK TACTX bullet | .300 BLK TACTX 탄자 |
| 592 | ThingDef | `HD_Bullet_76251M80A1_Proj.label` | 7.62x51mm M80A1 bullet | 7.62×51mm M80A1 탄자 |
| 593 | ThingCategoryDef | `HD_RifleAmmunition.label` | rifle ammunition | 소총 탄약 |
| 594 | ThingDef | `HD_Ammunition_556M855A1.label` | 5.56x45mm M855A1 cartridge | 5.56×45mm M855A1 탄약 |
| 595 | ThingDef | `HD_Ammunition_556M855A1.description` | An enhanced-performance 5.56x45mm ammunition selection installed inside a compatible STANAG magazine. | 5.56×45mm M855A1 탄약입니다. 호환되는 모듈식 무기 탄창에 장착됩니다. |
| 596 | ThingDef | `HD_Ammunition_556M855A1.comps.0.ammunitionType` | 5.56 M855A1 | 5.56 M855A1 |
| 597 | ThingDef | `HD_Ammunition_300BLKTACTX.label` | .300 BLK TACTX cartridge | .300 BLK TACTX 탄약 |
| 598 | ThingDef | `HD_Ammunition_300BLKTACTX.description` | A copper expanding .300 Blackout ammunition selection installed inside a compatible STANAG magazine. | .300 BLK TACTX 탄약입니다. 호환되는 모듈식 무기 탄창에 장착됩니다. |
| 599 | ThingDef | `HD_Ammunition_300BLKTACTX.comps.0.ammunitionType` | .300 BLK TACTX | .300 BLK TACTX |
| 600 | ThingDef | `HD_Ammunition_919M882.label` | 9x19mm M882 cartridge | 9×19mm M882 탄약 |
| 601 | ThingDef | `HD_Ammunition_919M882.description` | A NATO-pattern 9x19mm ball cartridge installed inside a compatible MP5 magazine. | 9×19mm M882 탄약입니다. 호환되는 모듈식 무기 탄창에 장착됩니다. |
| 602 | ThingDef | `HD_Ammunition_919M882.comps.0.ammunitionType` | 9x19 M882 | 9x19 M882 |
| 603 | ThingDef | `HD_Ammunition_76251M80A1.label` | 7.62x51mm M80A1 cartridge | 7.62×51mm M80A1 탄약 |
| 604 | ThingDef | `HD_Ammunition_76251M80A1.description` | An enhanced-performance 7.62x51mm cartridge for modular M14 magazines. | 7.62×51mm M80A1 탄약입니다. 호환되는 모듈식 무기 탄창에 장착됩니다. |
| 605 | ThingDef | `HD_Ammunition_76251M80A1.comps.0.ammunitionType` | 7.62 M80A1 | 7.62 M80A1 |

### Defs/ModernWar/Items/Weapons_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 606 | ThingDef | `HD_Gun_M4A1_Weapon.label` | M4A1 | M4A1 |
| 607 | ThingDef | `HD_Gun_M4A1_Weapon.description` | A compact full-automatic 5.56 mm carbine with a telescoping stock. It sacrifices some long-range performance for faster handling, while a Helod sharpshooter can trade eight tiles of effective range for near-instant target acquisition. | 신축식 개머리판을 갖춘 소형 완전자동 5.56mm 카빈입니다. 장거리 성능 일부를 희생해 조작성이 빠르며, 헤로드 명사수는 유효 사거리 8칸을 대가로 거의 즉시 표적을 획득할 수 있습니다. |
| 608 | ThingDef | `HD_Gun_Glock19Gen5_Weapon.tools.0.label` | grip | 손잡이 |
| 609 | ThingDef | `HD_Gun_Glock19Gen5_Weapon.tools.1.label` | slide | 슬라이드 |
| 610 | ThingDef | `HD_PowerforgeJackhammer.tools.0.label` | drill | 드릴 |
| 611 | ThingDef | `HD_PowerforgeJackhammer.tools.1.label` | reciprocating bit | 왕복 비트 |
| 612 | ThingDef | `HD_Gun_Nailgun_Weapon.tools.0.label` | body | 몸체 |
| 613 | ThingDef | `HD_Switchblade600_Drone.label` | Switchblade 600 drone | Switchblade 600 드론 |
| 614 | ThingDef | `HD_Switchblade600_Proj.label` | Switchblade 600 loitering munition | Switchblade 600 체공형 탄약 |
| 615 | ThingDef | `HD_Gun_Switchblade600Launcher_TurretGun.label` | Switchblade 600 launcher | 스위치블레이드 600 발사대 |
| 616 | ThingDef | `HD_Gun_Switchblade600Launcher_TurretGun.description` | A compact launch canister for Switchblade 600 loitering munitions. This early implementation fires a single long-range overhead strike and is reserved for spawning or scenario use. | Switchblade 600 체공형 탄약용 소형 발사관입니다. 초기 구현형은 장거리 상부 공격을 한 번 발사하며 생성 또는 시나리오 용도로 제한됩니다. |

### Defs/ModernWar/Jobs_ModernWar.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 617 | JobDef | `HD_ZaperX26Fire.reportString` | firing ZAPER X26 at TargetA. | TargetA에게 ZAPER X26을 발사하는 중. |
| 618 | JobDef | `HD_ZaperX26Contact.reportString` | striking TargetA with ZAPER X26 contact electrodes. | ZAPER X26 접촉 전극으로 TargetA을(를) 가격하는 중. |

### Defs/ModernWar/ModularArmorParts.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 619 | Helodrace.ModernWar.ModularArmorPositionDef | `HD_MA_Pos_RearLowerBody.label` | rear lower body | 하체 후면 |

### Defs/ModernWar/ModularLoadoutPresets.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 620 | Helodrace.ModernWar.ModularWeaponPresetDef | `HD_WeaponPreset_ModularM14_Default.label` | modular M14 standard configuration | 모듈식 M14 표준 구성 |
| 621 | Helodrace.ModernWar.ModularWeaponPresetDef | `HD_WeaponPreset_ModularMP5_Default.label` | modular MP5 standard configuration | 모듈식 MP5 표준 구성 |

### Defs/Scenarios/Scenarios_Helod.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 622 | ScenarioDef | `HD_Scenario_WWTech.label` | Helod Gilded Pioneers | 헤로드 도금시대 개척자들 |
| 623 | ScenarioDef | `HD_Scenario_WWTech.description` | Four Helod pioneers venture deep into the harsh, untamed frontier to establish an industrial outpost. Equipped with high starting funds of Sthaler banknotes, raw pemmican rations, three Graylock M1861 rifled muskets, and a steel knife (iron dagger), they are ready to seize control of local petroleum and build their financial empire. | 헤로드 개척자 네 명이 거칠고 길들지 않은 변방 깊숙이 들어가 산업 전초기지를 세우려 합니다. 넉넉한 스탈레르 지폐, 생 페미컨, 그레이록 M1861 강선 머스킷 세 정과 강철 칼을 갖추고 현지 석유를 장악해 금융 제국을 건설할 준비를 마쳤습니다. |
| 624 | ScenarioDef | `HD_Scenario_WWTech.scenario.summary` | Four Helod settlers starting with Sthaler money, pemmican, three M1861 rifles, and a steel knife. | 스탈레르 화폐, 페미컨, M1861 소총 세 정과 강철 칼을 가진 헤로드 정착민 네 명으로 시작합니다. |
| 625 | ScenarioDef | `HD_Scenario_WWTech.scenario.parts.ConfigurePawnsXenotypes.customSummary` | 네 명의 다양한 헤로드 아종 개척자로 시작합니다. | 네 명의 다양한 헤로드 아종 개척자로 시작합니다. |

### Defs/Tactical/TacticalMovement_Hediffs.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 626 | HediffDef | `HD_TacticalAim.label` | tactical aim | 전술 조준 |
| 627 | HediffDef | `HD_TacticalAim.description` | A sustained firing stance focused on a chosen direction. Targets inside the focus arc receive faster firing warmup; targets outside the arc but inside the firing arc remain possible but slower. | 선택한 방향에 집중하는 지속 사격 자세입니다. 집중 부채꼴 안의 표적은 사격 준비가 빨라지고, 집중 범위 밖이지만 사격 범위 안인 표적도 더 느리게 공격할 수 있습니다. |
| 628 | HediffDef | `HD_TacticalMovement.label` | tactical movement | 전술 기동 |
| 629 | HediffDef | `HD_TacticalMovement.description` | A movement firing posture that trades warmup time against accuracy while moving. | 이동 중 정확도와 사격 준비 시간을 맞바꾸는 이동 사격 자세입니다. |

### Defs/Tactical/TacticalMovement_Jobs.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 630 | JobDef | `HD_TacticalSlide.reportString` | sliding to TargetA. | TargetA 쪽으로 슬라이딩하는 중. |
| 631 | JobDef | `HD_TacticalTakedown.reportString` | approaching TargetA for a takedown. | TargetA을(를) 제압하려 접근하는 중. |
| 632 | JobDef | `HD_TacticalSubdue.reportString` | approaching TargetA to subdue. | TargetA을(를) 굴복시키려 접근하는 중. |

### Defs/Tactical/TacticalMovement_Training.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 633 | HediffDef | `HD_CQBTraining.label` | CQB training | CQB 교육 |
| 634 | HediffDef | `HD_CQBTraining.description` | Temporary training qualification for testing Helodrace close-quarters and tactical shooting skills. | 헤로드 종족의 근접전 및 전술 사격 기술을 시험하기 위한 임시 훈련 자격입니다. |

### Defs/WildWest/Buildings/Industry_WildWest.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 635 | ThingDef | `HD_SmallSteamEngine.comps.2.fuelLabel` | Wood | 목재 |
| 636 | ThingDef | `HD_SmallSteamEngine.comps.2.fuelGizmoLabel` | Wood fuel | 목재 연료 |
| 637 | ThingDef | `HD_SmallSteamEngine.comps.2.outOfFuelMessage` | Cannot function: Out of wood | 작동 불가: 목재 부족 |
| 638 | ThingDef | `HD_LargeSteamEngine.comps.2.fuelLabel` | Charcoal | 숯 |
| 639 | ThingDef | `HD_LargeSteamEngine.comps.2.fuelGizmoLabel` | Charcoal fuel | 숯 연료 |
| 640 | ThingDef | `HD_LargeSteamEngine.comps.2.outOfFuelMessage` | Cannot function: Out of charcoal | 작동 불가: 숯 부족 |
| 641 | ThingDef | `HD_IndustrialDieselEngine.comps.2.fuelLabel` | Diesel | 디젤 |
| 642 | ThingDef | `HD_IndustrialDieselEngine.comps.2.fuelGizmoLabel` | Diesel fuel | 디젤 연료 |
| 643 | ThingDef | `HD_IndustrialDieselEngine.comps.2.outOfFuelMessage` | Cannot function: Out of diesel | 작동 불가: 디젤 부족 |
| 644 | ThingDef | `HD_BatchStill.comps.1.fuelLabel` | Wood | 목재 |
| 645 | ThingDef | `HD_BatchStill.comps.1.fuelGizmoLabel` | Wood fuel | 목재 연료 |
| 646 | ThingDef | `HD_BatchStill.comps.1.outOfFuelMessage` | Cannot function: Out of wood | 작동 불가: 목재 부족 |
| 647 | ThingDef | `HD_DistillationTower.comps.2.fuelLabel` | Diesel | 디젤 |
| 648 | ThingDef | `HD_DistillationTower.comps.2.fuelGizmoLabel` | Diesel fuel | 디젤 연료 |
| 649 | ThingDef | `HD_DistillationTower.comps.2.outOfFuelMessage` | Cannot function: Out of diesel | 작동 불가: 디젤 부족 |
| 650 | ThingDef | `HD_KeroseneLamp.comps.0.fuelLabel` | Kerosene | 등유 |
| 651 | ThingDef | `HD_KeroseneLamp.comps.0.fuelGizmoLabel` | Kerosene fuel | 등유 연료 |
| 652 | ThingDef | `HD_BlastFurnace.comps.1.fuelLabel` | Charcoal | 숯 |
| 653 | ThingDef | `HD_BlastFurnace.comps.1.fuelGizmoLabel` | Charcoal fuel | 숯 연료 |
| 654 | ThingDef | `HD_BlastFurnace.comps.1.outOfFuelMessage` | Cannot function: Out of charcoal | 작동 불가: 숯 부족 |
| 655 | ThingDef | `HD_TelegraphTable.comps.1.fuelLabel` | Primary cells | 일차 전지 |
| 656 | ThingDef | `HD_TelegraphTable.comps.1.fuelGizmoLabel` | Primary cells | 일차 전지 |
| 657 | ThingDef | `HD_TelegraphTable.comps.1.outOfFuelMessage` | Cannot transmit: no primary cells | 송신 불가: 일차 전지 없음 |

### Defs/WildWest/Items/Materials_WildWest.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 658 | ThingDef | `HD_Naphtha.ingestible.ingestCommandString` | Drink naphtha | 마시기: naphtha |
| 659 | ThingDef | `HD_Naphtha.ingestible.ingestReportString` | Drinking naphtha. | 마시는 중: naphtha. |
| 660 | ThingDef | `HD_AromaticBaseOil.ingestible.ingestCommandString` | Drink aromatic base oil | 방향족 기유 마시기 |
| 661 | ThingDef | `HD_AromaticBaseOil.ingestible.ingestReportString` | Drinking aromatic base oil. | 방향족 기유를 마시는 중. |
| 662 | ThingDef | `HD_PlainDogChew.ingestible.ingestCommandString` | Chew {0} | 씹기: {0} |
| 663 | ThingDef | `HD_PlainDogChew.ingestible.ingestReportString` | Chewing {0}. | 씹는 중: {0}. |

### Defs/WildWest/Items/Weapons_WildWest.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 664 | ThingDef | `HD_Gun_ColtSAA_Weapon.tools.0.label` | grip | 손잡이 |
| 665 | ThingDef | `HD_Gun_ColtSAA_Weapon.tools.1.label` | barrel | 총열 |
| 666 | ThingDef | `HD_Gun_ColtArmyModel1860_Weapon.tools.0.label` | grip | 손잡이 |
| 667 | ThingDef | `HD_Gun_ColtArmyModel1860_Weapon.tools.1.label` | barrel | 총열 |
| 668 | ThingDef | `HD_Gun_SWModelThree_Weapon.tools.0.label` | grip | 손잡이 |
| 669 | ThingDef | `HD_Gun_Henry_Weapon.tools.0.label` | stock | 개머리판 |
| 670 | ThingDef | `HD_Gun_WinM1887_Weapon.tools.0.label` | stock | 개머리판 |
| 671 | ThingDef | `HD_Gun_WinMNinetyFive_Weapon.tools.0.label` | stock | 개머리판 |
| 672 | ThingDef | `HD_Gun_SprMOne_Weapon.tools.0.label` | stock | 개머리판 |
| 673 | ThingDef | `HD_Gun_SprMOne_Weapon.tools.1.label` | barrel | 총열 |
| 674 | ThingDef | `HD_Gun_SprMThreeHigh_Weapon.tools.0.label` | stock | 개머리판 |
| 675 | ThingDef | `HD_mSixZeroSaber.tools.0.label` | handle | 손잡이 |
| 676 | ThingDef | `HD_mSixZeroSaber.tools.1.label` | point | 찌르기 |
| 677 | ThingDef | `HD_mSixZeroSaber.tools.2.label` | edge | 날 |

### Defs/WildWest/Jobs_WildWest.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 678 | JobDef | `HD_SwitchSharpshooterMode.reportString` | switching weapon mode. | 무기 모드를 전환하는 중. |
| 679 | JobDef | `HD_IgnitePhotochlorogenCan.reportString` | igniting photochlorogen can. | 포토클로로겐 살포통을 점화하는 중. |
| 680 | JobDef | `HD_UseTelegraphTable.reportString` | using telegraph table. | 전신기를 사용하는 중. |

### Defs/WildWest/WorldObjects_WildWest.xml

| 번호 | Def 유형 | 번역 키 | 영문 원문 | 임시 한국어 번역 |
|---:|---|---|---|---|
| 681 | WorldObjectDef | `HD_ForwardBaseConstruction.label` | forward base construction | 전진기지 건설 |
| 682 | WorldObjectDef | `HD_ForwardBaseConstruction.description` | A contracted Helod forward base under construction. | 계약에 따라 건설 중인 헤로드 전진기지입니다. |
| 683 | WorldObjectDef | `HD_ForwardBase.label` | forward base | 전진기지 |
| 684 | WorldObjectDef | `HD_ForwardBase.description` | A completed Helod forward base. | 완공된 헤로드 전진기지입니다. |
| 685 | WorldObjectDef | `HD_ForwardBaseWithdrawal.label` | forward base withdrawal | 전진기지 철수 |
| 686 | WorldObjectDef | `HD_ForwardBaseWithdrawal.description` | A Helod forward base withdrawing after a failed fixed-price payment. | 정액 대금 지급 실패로 철수 중인 헤로드 전진기지입니다. |

## 판정 기준

- Def의 `label`, `description`, `jobString`, `reportString`, 단계·도구·동작 라벨, 섭취 명령/보고 문자열 등 사용자에게 표시되는 문자열을 후보로 수집했습니다.
- `defName`, 클래스명, 텍스처 경로, 수치, 열거형 값과 참조 키는 번역 대상에서 제외했습니다.
- 같은 번역 키가 한국어 DefInjected 어디에든 존재하면 번역 완료로 판정했습니다.
- English Keyed는 키 이름을 기준으로 한국어 Keyed 전체와 대조했습니다.
- 개발용 모듈식 무기 Def의 반복적인 설명은 항목명과 편집기 정보를 보존하는 간결한 임시 문장으로 정리했습니다.

