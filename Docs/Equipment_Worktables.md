# 무기·의류 작업대와 필요 설비

2026-11-05. 무기는 무기 제작대(`HD_GunSmithTable`), 방탄복·방탄판·방탄모·방패 및 모듈형 방어구 부속은 의류 작업대(`HD_ClothingBench`)로 모았다. 원래 기초 작업대에서 만들던 제작법은 그대로 병행한다. 이전 실제 엔진 목록은 [before.json](Data/Worktables/before.json), 변경한 정확한 제작법은 [routing.json](Data/Worktables/routing.json)이다.

무기 62개와 방어구/부속 27개, 총 89개 실제 제작법을 정리했다. 그중 기존 기초 작업대 제작법 59개를 유지했다. 기계 가공대·바닐라 대장간·재봉대 등에 흩어진 해당 제작 경로는 전용 작업대로 통일한다. 일반 의류·지원 장구의 기존 제작 경로와 기존 자원·산업 공정은 유지한다. 원래 제작 작업대가 없는 구형 목 보호대는 활성화하지 않는다.

상속에 덧붙이기만 하던 총기 작업대 패치를 제거하고 작성된 제작법마다 `recipeUsers Inherit="False"`로 최종 목록을 명시했다. MP5·P320과 근접 무기, 착용 칼/슬레지해머/테이저 및 조준 장치도 포함한다. 별도 총기 조립 RecipeDef와 자동 생성 recipeMaker를 함께 처리했다. 재료·방호·사격·작업량·연구 요구는 기존 값을 유지한다. 진압 방패처럼 명시 작업 유형이 있던 이동 제품은 의류 작업대의 Tailoring 작업 제공자와 맞췄다.

## 방어구 설비

방탄복·방탄판·방탄모·방패와 금속 조끼, 보호대/바이저 17종에는 가동 중인 프레스 성형 Lv.1 설비가 필요하다. 샤프트식(`HD_LineShaftHydraulicPress`, Lv.1)과 전동식(`HD_HydraulicPress1970Bench`, Lv.2) 중 하나를 작업대에서 6칸 이내에 연결한다. 두 설비는 1개 작업대에 연결할 수 있는 기존 facility 규칙을 사용한다. 기초 작업대와 의류 작업대 모두 연결을 지원한다. 일반 옷·벨트·파우치·전자 부속은 추가 방어구 프레스 조건을 요구하지 않는다.

샤프트식은 기계 동력과 최소 RPM, 전동식은 전력, 공통으로 스위치·고장·연결 상태를 확인한다. 작업 시작 여부, Bill 활성화, 진행 중 작업 실패 조건에서 같은 판정을 사용한다. 실제 제작 틱에는 연결된 프레스의 `UsedThisTick`을 호출해 샤프트 부하 450W 또는 전동식 1800W의 기존 작업 부하와 발열 처리를 사용한다. 동력을 잃거나 설비를 제거하면 진행 작업이 중단되고, 복구하면 재료를 다시 사용해 작업을 재개할 수 있다. 제품에 미완성 아이템이 없는 경우의 작업량 보존을 새로 보장하는 기능은 아니다.

## 설비 분야와 레벨

2026-11-05. 제작법이 특정 설비 Def를 지정하지 않고 **설비 분야와 최소 레벨**을 요구한다. 설비 ThingDef는 `Helodrace.FacilityCapabilityExtension`의 `capabilities`에 분야별 레벨을 갖고, 제작법(또는 제품 ThingDef)은 `Helodrace.FacilityRequirementExtension`의 `requirements`에 조건을 둔다. 작업대에 연결·활성화된 설비 중 해당 분야 레벨이 요구 이상인 설비가 있으면 충족된다. 여러 조건은 서로 다른 설비가 나눠 충족할 수 있고, 복합 설비 하나가 모두 충족할 수도 있다. 분야는 `Helodrace.FacilityFieldDef`(`Defs/Buildings/FacilityFields.xml`)이며, `requiresOperational` 분야는 동력·스위치·고장 상태를, `drivesFacilityWork` 분야는 작업 틱마다 해당 설비의 `UsedThisTick`을 호출한다.

| 설비 | 제공 분야 |
|---|---|
| 발판식 선반 `HD_TreadleLathe` | 선삭 Lv.1 |
| 라인 샤프트 터릿 선반 `HD_LineShaftTurretLathe` | 선삭 Lv.2 |
| NC 선반 `HD_CNCLathe` | 선삭 Lv.3 |
| 복합 가공기 `HD_MultiTaskMachine2000Bench` | 선삭 Lv.4, 밀링 Lv.4 |
| 라인 샤프트 밀링 머신 `HD_LineShaftMillingMachine` | 밀링 Lv.1 |
| NC 밀링기 `HD_CNCMillingMachine` | 밀링 Lv.2 |
| 머시닝 센터 `HD_MachiningCenter1970Bench` | 밀링 Lv.3 |
| 라인 샤프트 유압 프레스 `HD_LineShaftHydraulicPress` | 프레스 성형 Lv.1 (가동 필요, 작업 부하) |
| 전동 유압 프레스 `HD_HydraulicPress1970Bench` | 프레스 성형 Lv.2 (가동 필요, 작업 부하) |
| 전로 `HD_Converter` | 전로 정련 Lv.1 |
| 교반식 탱크 반응기 `HD_StirredTankReactor` | 화학 반응 Lv.1 (가동 필요) |

| 제작 대상 | 요구 |
|---|---|
| 후장식 총기 연구까지의 초기 총기 | 선삭 Lv.1 |
| 그 이후 총기 | 선삭 Lv.2 + 밀링 Lv.1 |
| 방어구 17종 | 프레스 성형 Lv.1 |
| 탄소강 정련 3종 | 전로 정련 Lv.1 |
| 나일론·아라미드 방사 | 화학 반응 Lv.1 |

총기 조건은 기존과 같이 연구 선행 관계로 자동 판정하며, 제작법이나 제품에 `FacilityRequirementExtension`을 두면 그 값이 우선한다. 같은 작업에서 공작기계 표시명의 연대 표기를 빼고 CNC를 NC로 바꿨다(초기 CNC 선반→NC 선반, 초기 CNC 밀링기→NC 밀링기, 1970년대 머시닝 센터→머시닝 센터, 1970년대 유압 프레스→전동 유압 프레스, 1970년대 유압 압연기→유압 압연기, 2000년대 복합 가공기→복합 가공기). DefName은 유지했다.

실제 RimWorld 1.7 `.codex-staging/FacilityFields/run-01` (`-Worktables -Fibers`): **589 PASS, COMPLETE**, 엔진 ERROR는 기존 Workshop 폴더 부재 1건.

## 아이템 정보

아이템 정보창에 **필요 설비**를 별도 항목으로 추가했다. 실제 제작법이 사용하는 판정에서 분야·레벨 조건(예: 선삭 Lv.2 + 밀링 Lv.1) 또는 설비 없음을 읽는다. 설비 건물의 정보창에는 **설비 분야** 항목으로 제공 분야와 레벨을 표시한다. 설명 보고서에는 제작법과 작업대별 조건을 함께 표시한다. 한글과 영어를 지원한다. 기존 작업대 본문에 섞인 선반·밀링 요구 문장은 아이템의 별도 항목을 안내하도록 정리했다.

정보 항목은 맵의 연결 상태나 번역 문자열을 캐시하지 않고 제작법의 정체성만 보관한다. 내부 계산과 표시 문자열은 .NET 및 Verse/RimWorld 계약을 사용한다. Unity 타입·리소스 API를 새로 추가하지 않았다.

## 제작법 목록

| 제작법 | 작업대 | 연결 프레스 |
|---|---|---|
| `HD_Make_Gun_ColtArmyModel1860_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_ColtSAA_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_Henry_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_KragJorgensen_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1897_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1903A4_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1903Pedersen_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1911_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1918A2HAR_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1919A6LMG_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1A1Carbine_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1A1Harrington_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1D_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M1Garand_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M2Carbine_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M3A1_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M60E3_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_M9A1_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_SprMOne_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_SprMThree_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_SprMThreeHigh_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_SWModelThree_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_WinM1887_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `HD_Make_Gun_WinMNinetyFive_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Apparel_ANPEQ1C` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Apparel_FASTMT` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_GreatWarM1Helmet` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_GW_Sledgehammer` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Apparel_IBTVAssault` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_IZLIDUltra` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Apparel_LA16uPEQ` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Apparel_M1952AFlakJacket` | `HD_BasicWorkbench`, `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_MOLLEBattleBelt` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_Apparel_ShieldDefenTechIIIA` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_ShieldIronHideIV` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_ShieldIronHideRiot` | `HD_ClothingBench` | 필요 |
| `Make_HD_Apparel_ZaperX26_Device` | `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_ArmorPlate_ESAPI` | `HD_ClothingBench` | 필요 |
| `Make_HD_ArmorPlate_ESBI` | `HD_ClothingBench` | 필요 |
| `Make_HD_ArmorPlate_RAMPART4800_Plate` | `HD_ClothingBench` | 필요 |
| `Make_HD_ArmorPlate_SAPI` | `HD_ClothingBench` | 필요 |
| `Make_HD_ArmorPlate_SSAPI` | `HD_ClothingBench` | 필요 |
| `Make_HD_ExpandableBaton` | `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M111_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M14_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M15_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M67_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M7A2_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M8_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_M84_Item` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_MKII` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Grenade_MKIII` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_Glock19Gen5_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_LANCE_LRA7_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M14_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M16A1_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M16A2_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M16A3_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M16A4_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M249_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M2FlameThrower_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M47_Dragon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M4A1_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M79_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_M8FlareGun_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_MP5_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_Nailgun_Weapon` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_Gun_P320_Weapon` | `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_M1902Saber` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_MetalBaseballBat` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_ModularPart_AdminPouch` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_GPNVG18_Part` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_IBTVBackProtector` | `HD_ClothingBench` | 필요 |
| `Make_HD_ModularPart_IBTVCamelBak` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_IBTVLowerBody` | `HD_ClothingBench` | 필요 |
| `Make_HD_ModularPart_IBTVShoulderGuards` | `HD_ClothingBench` | 필요 |
| `Make_HD_ModularPart_IFAK` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_MediL` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_MOLLEDropLeg` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_MultiHitVisor` | `HD_ClothingBench` | 필요 |
| `Make_HD_ModularPart_RifleMagazinePouch` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_UCPKneePads` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_ModularPart_WalkieTalkie` | `HD_ClothingBench` | 기존 조건 |
| `Make_HD_mSixZeroSaber` | `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_PowerCutter` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_PowerforgeJackhammer` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_USMCFightingKnife` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |
| `Make_HD_WildWestMetalVest` | `HD_ClothingBench` | 필요 |
| `Make_HD_WoodenBaseballBat` | `HD_BasicWorkbench`, `HD_GunSmithTable` | 기존 조건 |

## 검증

실제 RimWorld 1.7.4995 rev309 `.codex-staging/Worktables/native-03`: **589 PASS, COMPLETE**. [검사 원문](Data/Worktables/native-checks.txt), [범위·빌드 해시](Data/Worktables/verification.json).

89개 제작법의 실제 작업대 목록·중복 부재·작업대 메뉴 목록과 제품별 별도 정보 항목을 검사했다. 이전 엔진 목록에 있던 모든 기초 작업대 제작법의 잔존도 확인했다. SAPI·IBTV·FAST MT·M1 철모·진압 방패·M1952A·진압봉·P320·MP5의 실물 제작을 총 10회 수행했다(M1 철모는 두 프레스에서 각각 제작). 기초 작업대 M1952A도 실제 제작했다. 모든 작업의 재료 소비·산출을 대조했다.

전동식 프레스의 실제 1800W 작업 부하, 샤프트식의 실제 모터·샤프트 네트워크와 450W 작업 부하를 확인했다. 스위치·전력/기계 동력 상실·철거가 제작 접근을 차단하고 진행 중 SAPI 작업을 중단했으며, 복구 후 같은 Bill이 완성됐다. 일반 의류는 프레스가 꺼져 있어도 제작 가능하다. 처음에는 원래 제작 경로가 없는 목 보호대가 이동 목록에 섞여 검사에서 걸렸고, 실제 제작 가능한 항목에서 제외했다.

별도로 89개 작성 경로·프레스·번역 키, 방어구 재료 19종·방탄모/방패 5종, 전용 중간재 70개 정의, 레시피/작업대 연결을 확인했다. 기존 93개 명시 RecipeDef와 544개 구체 ThingDef 수는 유지됐다. 배포 DLL/PDB는 최종 게임 검사 호스트와 동일하다.

엔진 ERROR는 기존 Workshop 3781143823 폴더 부재 1건이다. 영어 번역·디스폰 검사 폰의 자동저장 참조·native 종료 할당 경고는 남는다. 수집기 연결 전에 호스트가 이미 종료되어 숫자 종료 코드는 확보하지 못했다. 이번 검사는 저장/불러오기 또는 실제 마우스로 정보창을 여는 UI 검증을 포함하지 않는다. 정보 항목은 실제 엔진의 `SpecialDisplayStats` 출력으로 검증했다.
