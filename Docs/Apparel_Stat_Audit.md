# 의상 스탯 전수 조사

저장소의 실제 착용 의상을 Core 부모 정의와 함께 조사했습니다. 상속값, 소재 배율, 명시적 0, 미설정 기본값을 구분합니다. 다른 모드의 패치나 실제 아이템의 품질·소재·장착 부품에 따른 최종 수치는 포함하지 않습니다. 이 조사 스크립트는 의상 정의나 스탯 값을 변경하지 않습니다.

단열 미설정 항목은 게임 기본값 0이며 정보창에서 숨겨집니다. 소재 배율이 있으면 직접 단열값이 없어도 소재가 단열을 제공합니다. ArmorRating 기본값도 0입니다. MarketValue 등의 최종 값은 게임의 별도 계산이 있으므로 정의 누락을 곧바로 기능 누락으로 판단하지 않습니다.

통풍은 HD_ArmorVentilation 커스텀 지표입니다. CompModularArmor가 있는 장비에만 적용되며 기본값과 장착 부품 합계로 계산합니다. 일반 의상에 Comp가 없는 것은 이 시스템에서 통풍을 제공하지 않는다는 뜻입니다.

총 40종 중 추위·더위 단열이 모두 0인 항목은 19종입니다. Belt와 HD_Pouch를 제외한 의복·방어구·모자 21종 중에서는 0종입니다. 나머지에는 소재에 따른 단열이 있으므로 제작 소재에 따라 실제 값이 달라집니다.

## 소재 배율이 작동하지 않는 의상

아래 의상은 StuffEffectMultiplier 값이 있지만 stuffCategories가 없고 Cloth 등의 고정 costList로 제작됩니다. 게임 StatPart_Stuff는 StuffDef가 없으면 소재 단열·방어력 기여를 0으로 계산합니다. 배율만 넣어서는 기능이 생기지 않습니다.

| 의상 | 비활성 배율 |
| --- | --- |
| HD_Apparel_GreatWarStormFrontInner | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |
| HD_Apparel_GreatWarStormFrontOuter | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |
| HD_Apparel_GreatWarTrenchArmyInner | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |
| HD_Apparel_GreatWarTrenchArmyOuter | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |
| HD_Apparel_WildWestFormalShirt | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |
| HD_Apparel_WildWestFrockCoat | StuffEffectMultiplierArmor, StuffEffectMultiplierInsulation_Cold, StuffEffectMultiplierInsulation_Heat |

## 단열 / 통풍 전체 목록

| defName | 의상 | 추위 단열 | 더위 단열 | 통풍 |
| --- | --- | --- | --- | --- |
| HD_Apparel_M1952AFlakJacket | M1952A flak jacket | 기본 1 | 기본 0.5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarCBRNPouch | CBRN protection pouch | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarGasMaskPouch | gas mask pouch | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarM1Helmet | m1 Helmet | 기본 1 | 기본 0.5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarMedibag | medic bag | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarStormFrontChestRig | great war storm-front chest rig | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarStormFrontInner | great war storm-front uniform liner | 기본 6.3 | 기본 3.6 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarStormFrontOuter | great war storm-front uniform outer layer | 기본 9.9 | 기본 2.7 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarTrenchArmyInner | great war trench uniform liner | 기본 6.3 | 기본 3.6 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_GreatWarTrenchArmyOuter | great war trench armor coat | 기본 9.9 | 기본 2.7 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_M1MortarBarrel | M1 mortar barrel | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_M1MortarBaseplate | M1 mortar baseplate | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_M1MortarMount | M1 mortar bipod | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_M6RocketBag | weapon system test harness | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_SCR300_Radio | SCR-300 backpack radio | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_USMCFightingKnife | KA-BUG USMC fighting knife | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_ACSUpper | army combat shirt | 기본 3.6 | 기본 4.5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_FASTMT | FAST MT helmet | 기본 1 | 기본 0.5 | 기본 5 / 기본부품 포함 5 |
| HD_Apparel_IBTVAssault | IBTV assault body armor | 기본 2 | 기본 0.5 | 기본 7 / 기본부품 포함 4.65 |
| HD_Apparel_MOLLEBattleBelt | MOLLE battle belt | 미설정 → 0 | 미설정 → 0 | 기본 9 / 기본부품 포함 9 |
| HD_Apparel_ShieldDefenTechIIIA | DefenTech IIIA light shield | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_ShieldIronHideIV | Ironhide IV medium shield | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_UCPBlouse | UCP combat blouse | 기본 6.3 | 기본 3.6 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_UCPPants | UCP combat pants | 기본 3.6 | 기본 1.44 | 기본 9 / 기본부품 포함 9 |
| HD_Apparel_ZaperX26_Device | ZAPER X26 | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_MilitaryTablet | military tablet | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_ANPEQ1C | AN/PEQ-1C SOFLAM | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_IZLIDUltra | IZLID Ultra | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_LA16uPEQ | LA-16u/PEQ | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_CarvalyHat | Carvaly hat | 기본 2 | 기본 2 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_CarvalyUniform | Carvaly uniform | 기본 10 | 기본 5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_CowboyHat | Cowboy hat | 기본 2 | 기본 2 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_WildWestFemaleCasual | Casual dress | 소재 배율 0.3 (기본 0) | 소재 배율 0.3 (기본 0) | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_WildWestFormalShirt | formal shirt | 기본 4.5 | 기본 4.5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_WildWestFrockCoat | frock coat | 기본 9.9 | 기본 4.5 | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_WildWestMaleCasual | Casual jacket | 소재 배율 0.3 (기본 0) | 소재 배율 0.3 (기본 0) | 대상 아님 (커스텀 Comp 없음) |
| HD_Apparel_WildWestTopHat | top hat | 기본 2 | 기본 1 | 대상 아님 (커스텀 Comp 없음) |
| HD_Bugle | bugle | 미설정 → 0 | 미설정 → 0 | 대상 아님 (커스텀 Comp 없음) |
| HD_WildWestLeatherVest | leather vest | 소재 배율 0.2 (기본 0) | 소재 배율 0.2 (기본 0) | 대상 아님 (커스텀 Comp 없음) |
| HD_WildWestMetalVest | metal vest | 기본 1 | 기본 0.5 | 대상 아님 (커스텀 Comp 없음) |

## 통풍 정보창 표시 문제

ModularArmorStats.xml의 세 커스텀 StatDef는 showIfUndefined=false인데 의상의 statBases에는 해당 항목이 없습니다. StatWorker_ModularArmorMetric는 값 계산만 재정의하고 ShouldShowFor를 재정의하지 않습니다. 설치된 게임의 StatWorker.ShouldShowFor는 StatBases에 정의되지 않은 항목을 먼저 숨깁니다. 따라서 커스텀 화면에는 값이 있어도 일반 정보창에서는 통풍·인체공학·하중 분산이 표시되지 않습니다.

| 의상 | 통풍의 착용자 스탯 변환 | 정보창에서 누락된 커스텀 스탯 |
| --- | --- | --- |
| HD_Apparel_FASTMT | ComfyTemperatureMax (기준 5, 배율 0.5) | HD_ArmorErgonomics, HD_ArmorVentilation, HD_ArmorLoadDistribution |
| HD_Apparel_IBTVAssault | ComfyTemperatureMax (기준 5, 배율 1) | HD_ArmorErgonomics, HD_ArmorVentilation, HD_ArmorLoadDistribution |
| HD_Apparel_MOLLEBattleBelt | 없음 | HD_ArmorErgonomics, HD_ArmorVentilation, HD_ArmorLoadDistribution |
| HD_Apparel_UCPPants | 없음 | HD_ArmorErgonomics, HD_ArmorVentilation, HD_ArmorLoadDistribution |

## 기본 스탯 / 방어력 정의 누락

아래는 상속과 소재 배율을 확인한 뒤에도 개별 정의가 없는 항목입니다. 방어력이 없는 보조 장비나 제작 불가능 장비는 의도된 설정일 수 있습니다.

| 의상 | 기본 스탯 미정의 | 기본값만 사용하는 방어력 |
| --- | --- | --- |
| HD_Apparel_GreatWarCBRNPouch |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_GreatWarGasMaskPouch |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_GreatWarM1Helmet | MarketValue |  |
| HD_Apparel_GreatWarMedibag |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_GreatWarStormFrontChestRig |  | ArmorRating_Heat |
| HD_Apparel_GreatWarStormFrontInner |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_GreatWarStormFrontOuter |  | ArmorRating_Heat |
| HD_Apparel_GreatWarTrenchArmyInner |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_GreatWarTrenchArmyOuter |  | ArmorRating_Heat |
| HD_Apparel_M1MortarBarrel |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_M1MortarBaseplate |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_M1MortarMount |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_M6RocketBag |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_SCR300_Radio |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_USMCFightingKnife | MarketValue | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_MOLLEBattleBelt |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_ShieldDefenTechIIIA |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_ShieldIronHideIV |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_ZaperX26_Device |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_MilitaryTablet | WorkToMake | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_ANPEQ1C |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_IZLIDUltra |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_LA16uPEQ |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_CarvalyHat | MarketValue |  |
| HD_Apparel_CarvalyUniform | MarketValue |  |
| HD_Apparel_CowboyHat | MarketValue |  |
| HD_Apparel_WildWestFemaleCasual | MarketValue |  |
| HD_Apparel_WildWestFormalShirt | MarketValue | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_WildWestFrockCoat | MarketValue | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Apparel_WildWestMaleCasual | MarketValue |  |
| HD_Apparel_WildWestTopHat | MarketValue | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_Bugle |  | ArmorRating_Sharp, ArmorRating_Blunt, ArmorRating_Heat |
| HD_WildWestLeatherVest | MarketValue |  |
| HD_WildWestMetalVest | MarketValue |  |
