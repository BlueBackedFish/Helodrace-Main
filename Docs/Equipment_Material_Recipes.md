# 헤로드 장비의 전용 중간재 제작 재료

2026-11-04. 방패의 플라스틸을 없애고 헤로드 장비의 부품·고급 부품을 전용 중간재로 교체했다. 총기·방어구·방탄판·수류탄·무전기·조준 장치·장착 부품·부품 상자·CROWS·발사대와 남은 산업 가공기 2종을 포함한다.

기계식 장비는 `HD_GeneralMachinePart`, 전기 공구와 화염방사기의 전기계통은 `HD_BasicElectricalComponents`, SCR-300은 연구 단계에 맞는 진공관 계열 `HD_BasicElectronicComponents`, 현대 전자 장비·유도 무기·자동화 장비는 `HD_ElectronicComponent`를 사용한다. 기존 부품 1개는 해당 중간재 1개, 고급 부품 1개는 해당 중간재 2개로 치환하고 같은 재료 항목은 합쳤다. 새 중간재 아이템은 만들지 않았다.

DefenTech IIIA는 플라스틸 30을 아라미드 30으로, IronHide IV는 플라스틸 40을 아라미드 40으로 바꾸었다. IronHide IV의 알루미나 40은 세라믹 타격면으로 유지한다. 진압 방패는 기존 알루미늄 합금·강철·화학연료를 사용한다. 이 제작 배정은 게임용 설계이며 실제 PE 방패의 재료를 아라미드라고 정정하는 의미가 아니다. [기존 실제품 참고 자료](Ballistic_Reference_Notes.md).

`costList`뿐 아니라 고정 재료/기본 허용 필터, M16·MP5 공통 상속 정의와 M1903A4·M1D·M60E3 별도 조립·개조 레시피도 변경했다. 기존에 직접 제작을 비활성화한 총기는 조립 전용 정책을 유지한다. 방호·공격·질량·시장가·작업량 등 명시 기본 수치는 바꾸지 않았다. IBTV는 계속 빈 본체로 제작된다.

모드 전체의 `costList`에 `ComponentIndustrial`과 `ComponentSpacer`가 남지 않는다. 기존 바닐라 부품을 범용 기계 부품으로 전환하는 선택적 자원 재활용 레시피 3개는 유지한다. 전용 중간재는 자체 생산 경로가 있어 바닐라 부품을 먼저 만들 필요가 없다.

## 정의별 현재 재료

[이전/현재 수량·유지한 수치의 기계 판독 표](Data/EquipmentMaterials/recipe-materials.json). 70개 정의는 구체 ThingDef 65개, 추상 총기 공통 정의 2개, 별도 개조 RecipeDef 3개다. 상속받는 구체 총기들도 실제 엔진에서 검사한다.

| 정의 | 현재 재료 |
|---|---|
| `HD_Grenade_M14_Item` | `Steel` 24 + `Chemfuel` 80 + `HD_GeneralMachinePart` 1 |
| `HD_Grenade_M7A2_Item` | `Steel` 14 + `Chemfuel` 35 + `HD_GeneralMachinePart` 1 |
| `HD_Gun_M79_Weapon` | `Steel` 55 + `HD_GeneralMachinePart` 5 |
| `HD_M60Igniter` | `Steel` 8 + `HD_GeneralMachinePart` 1 |
| `HD_M81Igniter` | `Steel` 10 + `HD_GeneralMachinePart` 2 |
| `HD_Gun_M47_Dragon` | `Steel` 60 + `HD_ElectronicComponent` 4 |
| `HD_Gun_M60E3_Weapon` | `Steel` 95 + `HD_GeneralMachinePart` 10 |
| `HD_PowerCutter` | `Steel` 55 + `HD_BasicElectricalComponents` 4 + `HD_GeneralMachinePart` 2 |
| `HD_Gun_LANCE_LRA7_Weapon` | `Plasteel` 80 + `HD_ElectronicComponent` 12 |
| `HD_Gun_M2FlameThrower_Weapon` | `Steel` 90 + `HD_BasicElectricalComponents` 6 + `HD_GeneralMachinePart` 2 + `Chemfuel` 25 |
| `HD_Gun_M8FlareGun_Weapon` | `Steel` 18 + `HD_GeneralMachinePart` 1 |
| `HD_Apparel_SCR300_Radio` | `Steel` 45 + `HD_BasicElectronicComponents` 5 + `Cloth` 20 |
| `HD_Gun_M1A1Harrington_Weapon` | `Steel` 70 + `HD_GeneralMachinePart` 7 |
| `HD_Gun_M3A1_Weapon` | `Steel` 52 + `HD_GeneralMachinePart` 6 |
| `HD_Gun_KragJorgensen_Weapon` | `Steel` 75 + `HD_GeneralMachinePart` 4 |
| `HD_Gun_M1897_Weapon` | `Steel` 70 + `HD_GeneralMachinePart` 5 |
| `HD_Gun_M1A1Carbine_Weapon` | `Steel` 58 + `HD_GeneralMachinePart` 6 |
| `HD_Gun_M2Carbine_Weapon` | `Steel` 62 + `HD_GeneralMachinePart` 7 |
| `HD_Gun_M1Garand_Weapon` | `Steel` 90 + `HD_GeneralMachinePart` 7 |
| `HD_Gun_M1D_Weapon` | `Steel` 95 + `HD_GeneralMachinePart` 8 |
| `HD_Gun_M1918A2HAR_Weapon` | `Steel` 85 + `HD_GeneralMachinePart` 8 |
| `HD_Gun_M1919A6LMG_Weapon` | `Steel` 110 + `HD_GeneralMachinePart` 8 + `HD_UniformRolledSteelPlate` 1 |
| `HD_Gun_SprMThree_Weapon` | `Steel` 80 + `HD_GeneralMachinePart` 5 |
| `HD_Gun_M1903A4_Weapon` | `Steel` 85 + `HD_GeneralMachinePart` 6 |
| `HD_Gun_M1903Pedersen_Weapon` | `Steel` 85 + `HD_GeneralMachinePart` 6 |
| `HD_M16FamilyBase` | `Steel` 55 + `HD_GeneralMachinePart` 7 |
| `HD_CrowsComputer` | `Steel` 80 + `WoodLog` 60 + `HD_BasicElectricalComponents` 12 + `HD_ElectronicComponent` 4 |
| `HD_Building_CrowsII` | `HD_GeneralMachinePart` 12 + `HD_CarbonSteel` 110 + `HD_PressComponent` 8 + `HD_UniformRolledSteelPlate` 4 + `HD_BasicElectricalComponents` 16 + `HD_ElectronicComponent` 6 |
| `HD_Building_Switchblade600Launcher` | `Steel` 80 + `HD_ElectronicComponent` 12 |
| `HD_ModularWeaponPartsBox` | `Steel` 45 + `HD_GeneralMachinePart` 2 |
| `HD_Apparel_ZaperX26_Device` | `Steel` 20 + `HD_ElectronicComponent` 2 |
| `HD_Apparel_IBTVAssault` | `HD_Nylon` 60 + `HD_Aramid` 40 + `HD_GeneralMachinePart` 3 |
| `HD_Apparel_MOLLEBattleBelt` | `HD_Nylon` 30 + `Steel` 10 + `HD_GeneralMachinePart` 1 |
| `HD_Apparel_FASTMT` | `HD_Nylon` 15 + `HD_Aramid` 25 + `HD_GeneralMachinePart` 2 |
| `HD_Apparel_ShieldDefenTechIIIA` | `Steel` 55 + `HD_Aramid` 30 + `HD_GeneralMachinePart` 2 |
| `HD_Apparel_ShieldIronHideIV` | `Steel` 120 + `HD_Aramid` 40 + `HD_Alumina` 40 + `HD_GeneralMachinePart` 4 |
| `HD_ArmorPlate_SAPI` | `HD_Alumina` 18 + `HD_Aramid` 18 + `HD_Nylon` 6 + `HD_GeneralMachinePart` 1 |
| `HD_ArmorPlate_ESAPI` | `HD_Alumina` 28 + `HD_Aramid` 16 + `HD_Nylon` 6 + `HD_GeneralMachinePart` 2 |
| `HD_ArmorPlate_SSAPI` | `HD_Alumina` 40 + `HD_Aramid` 12 + `HD_Nylon` 4 + `HD_GeneralMachinePart` 3 |
| `HD_ArmorPlate_ESBI` | `HD_Alumina` 44 + `HD_Aramid` 10 + `HD_Nylon` 4 + `HD_GeneralMachinePart` 3 |
| `HD_ArmorPlate_RAMPART4800_Plate` | `Uranium` 10 + `Plasteel` 48 + `HD_Nylon` 6 + `HD_GeneralMachinePart` 4 |
| `HD_Grenade_M84_Item` | `Steel` 14 + `Chemfuel` 35 + `HD_GeneralMachinePart` 1 |
| `HD_Grenade_M111_Item` | `Steel` 10 + `Chemfuel` 75 + `HD_GeneralMachinePart` 1 |
| `HD_Apparel_ANPEQ1C` | `Steel` 60 + `HD_ElectronicComponent` 12 |
| `HD_Apparel_IZLIDUltra` | `Steel` 22 + `HD_ElectronicComponent` 6 |
| `HD_Apparel_LA16uPEQ` | `Steel` 18 + `HD_ElectronicComponent` 5 |
| `HD_ModularPart_GPNVG18_Part` | `Plasteel` 18 + `HD_ElectronicComponent` 8 |
| `HD_ModularPart_MultiHitVisor` | `Chemfuel` 24 + `HD_GeneralMachinePart` 2 |
| `HD_ModularPart_WalkieTalkie` | `HD_Nylon` 10 + `HD_ElectronicComponent` 1 |
| `HD_ModularPart_IBTVCamelBak` | `HD_Nylon` 20 + `HD_GeneralMachinePart` 1 |
| `HD_Gun_M14_Weapon` | `Steel` 55 + `HD_GeneralMachinePart` 5 |
| `HD_Gun_M249_Weapon` | `Steel` 75 + `HD_GeneralMachinePart` 7 |
| `HD_MP5FamilyBase` | `Steel` 45 + `HD_GeneralMachinePart` 6 |
| `HD_Gun_P320_Weapon` | `Steel` 24 + `HD_GeneralMachinePart` 3 |
| `HD_Gun_Glock19Gen5_Weapon` | `Steel` 28 + `HD_GeneralMachinePart` 5 |
| `HD_PowerforgeJackhammer` | `Steel` 60 + `HD_BasicElectricalComponents` 4 + `HD_GeneralMachinePart` 2 |
| `HD_Gun_Nailgun_Weapon` | `Steel` 35 + `HD_BasicElectricalComponents` 3 + `HD_GeneralMachinePart` 1 |
| `HD_Make_Gun_M1903A4_Weapon` | `HD_Gun_SprMThree_Weapon` 1 + `HD_CarbonSteel` 18 + `HD_GeneralMachinePart` 6 |
| `HD_Make_Gun_M1D_Weapon` | `HD_Gun_M1Garand_Weapon` 1 + `HD_CarbonSteel` 18 + `HD_GeneralMachinePart` 6 |
| `HD_Make_Gun_M60E3_Weapon` | `HD_GeneralMachinePart` 12 + `HD_CarbonSteel` 105 + `HD_PressComponent` 6 |
| `HD_Gun_ColtSAA_Weapon` | `Steel` 30 + `HD_GeneralMachinePart` 2 |
| `HD_Gun_ColtArmyModel1860_Weapon` | `Steel` 28 + `HD_GeneralMachinePart` 1 |
| `HD_Gun_SWModelThree_Weapon` | `Steel` 30 + `HD_GeneralMachinePart` 2 |
| `HD_Gun_Henry_Weapon` | `Steel` 60 + `HD_GeneralMachinePart` 3 |
| `HD_Gun_WinM1887_Weapon` | `Steel` 65 + `HD_GeneralMachinePart` 3 |
| `HD_Gun_WinMNinetyFive_Weapon` | `Steel` 70 + `HD_GeneralMachinePart` 4 |
| `HD_Gun_SprMOne_Weapon` | `Steel` 50 + `WoodLog` 20 + `HD_GeneralMachinePart` 1 |
| `HD_Gun_SprMThreeHigh_Weapon` | `Steel` 60 + `HD_GeneralMachinePart` 2 |
| `HD_MachiningCenter1970Bench` | `Steel` 190 + `HD_CarbonSteel` 90 + `HD_GeneralMachinePart` 14 + `HD_BasicElectricalComponents` 12 + `HD_ElectronicComponent` 10 |
| `HD_MultiTaskMachine2000Bench` | `Steel` 260 + `HD_CarbonSteel` 100 + `HD_GeneralMachinePart` 16 + `HD_UniformRolledSteelPlate` 24 + `HD_BasicElectricalComponents` 12 + `HD_ElectronicComponent` 32 |

## 검증

실제 RimWorld 1.7.4995 rev309 `.codex-staging/EquipmentMaterials/native-03`: **293 PASS, COMPLETE, 호스트 종료 후 로그 수집**. [검사 원문](Data/EquipmentMaterials/native-checks.txt), [배포 빌드 해시·상세 범위](Data/EquipmentMaterials/verification.json).

구체 장비/건물 65개와 별도 조립 레시피 3개의 실제 비용·자동 생성 제작법·새 Bill 재료 허용을 대조했다. 모드의 모든 로드된 ThingDef 비용과 제작 RecipeDef를 추가 순회해 바닐라 부품이 남지 않는지, 모든 방패에서 플라스틸이 빠졌는지 확인했다. 자원 전환용 범용 기계 부품 산출 레시피는 이 전체 제작법 검사에서 별도로 구분한다. 직접 제작이 금지된 조립 전용 총기도 자동 제작법이 새로 생기지 않았음을 확인했다.

실제 전력망과 선반·밀링 겸용 설비를 작업대에 연결하고 DefenTech IIIA·IronHide IV·IBTV·SAPI·GPNVG-18·SCR-300·M4A1·MP5·M1903A4·M60E3·부품 상자 11종을 실제 작업 제공자로 제작했다. 새 재료의 정확한 소비·물품 산출과 IBTV가 빈 본체로 남는 것을 확인했다. 기존 방탄 장비 재료/수치 검사 19종·방탄모와 방패 5종, 레시피 연결 93개·ThingDef 544개, 중간재 가격 27종 검사도 통과했다.

초기 검사는 고정 재료에도 전체 fixedIngredientFilter 허용을 요구해 중단했다. 실제 1.7 Bill은 고정 IngredientCount에 먼저 허용을 확인하므로 `Bill.IsFixedOrAllowedIngredient`와 새 Bill의 기본 필터를 함께 검사하도록 바로잡았다. 다음 실행은 총기 제작용 연결 설비가 없는 검사 배치에서 중단돼 겸용 가공기와 전력망을 추가했다. 두 경우 제품의 기존 총기 조립 정책을 우회하지 않았다.

수집기가 다시 연결되기 전에 호스트가 이미 종료되어 숫자 종료 코드는 확보하지 못했다. 검사 완료 표시와 종료된 엔진 로그를 수집했다. 마지막 엔진 ERROR는 기존 Workshop 3781143823 폴더 부재 1건이다. 영어 번역 경고와 디스폰한 quicktest 검사 폰의 자동저장 참조 경고는 남는다. 저장/불러오기나 전투 성능을 이번 결과로 검증했다고 보지는 않는다. 디스크 공간 확보를 위해 경로·일반 폴더·실행 중 호스트 부재를 확인한 과거 Stage K 격리 검사 TextureCache만 정리했으며 저장과 로그는 보존했다. 배포 DLL/PDB는 최종 검사 호스트와 동일하다.
