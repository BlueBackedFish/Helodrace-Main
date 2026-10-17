# 모듈형 총기 파츠 스탯 점검 목록

> 후속 밸런스 조정: 이 목록은 수치 입력 전의 기록이다. 현재는 장착 가능한 파츠·탄종 선택 항목 156종과 본체 10종에 중앙 밸런스 패치가 적용된다. 최신 값과 조준 그룹 규칙은 [총기 밸런스 문서](ModularWeapon_Balance.md), 실제 입력값은 [밸런스 표](../Patches/ModularWeapon_Balance.xml)를 따른다.

2026-10-17 기준, `15a1c3f0` 상태의 XML 정의·상속·스탯 계산 코드를 정적으로 대조했다. 총기 본체 10종은 제외하고 파츠 151종을 조사했다. 게임 실행 중의 특정 무기 장착 상태를 재현한 결과는 아니다. 이 문서는 점검 목록이며 게임 정의와 코드는 변경하지 않았다.

## 판정 기준과 결론

- 고유 성능 수치 있음: `internalStats`, `statOffsets`, `statFactors` 중 하나 이상을 지정한 파츠 **20종**.
- 고유 성능 수치 없음: 세 항목이 모두 없는 파츠 **131종**. 이 중 **1종은 마운트 없는 구형 M1911 하부 슬라이드**다.
- “수치 없음”은 “아무 효과 없음”이 아니다. 무게와 장착 위치는 항상 합산한다. 목록 중 **120종은 부모의 공통 무게 0.2kg**를 사용한다. 따라서 개머리판·총열·소형 어댑터마다의 실제 무게 차이도 아직 입력되지 않은 경우가 많다.
- 고유 수치가 없는 목록에는 **탄약 13종**, **용량이 설정된 탄창/탄통 9종**, **Flux 전방 예비탄창 3종**, **M600 조명 1종**이 포함된다. 이들의 해당 기능은 별도 경로로 적용된다.
- 현재 코드 대조에서 “지정된 고유 수치가 모든 장착 상태에서 영구적으로 무시되는 파츠”는 확정하지 못했다. 먼저 보완할 부분은 계산 누락보다 XML 성능값 미입력이다.

근거: [공통 파츠 무게](../Defs/ModernWar/Items/ModularWeapons_Development.xml), [무게·성능 합산](../Source/Helodrace/ModernWar/ModularWeapons/ModularWeaponConvertedStats.cs), [수치·용량·투사체 적용](../Source/Helodrace/ModernWar/ModularWeapons/CompModularWeaponNode.cs).

## 우선 보완 대상

| 분류 | 수 | 빠진 고유 수치/확인 사항 |
|---|---:|---|
| 총열 | 15 | 무게·관성·조작성·정밀도 등 총열별 수치. 단, 총열 기하 길이는 총구 효과와 일부 탄종의 파편화에 이미 사용된다. |
| 개머리판 | 10 | 반동/총구상승·조작성·조준 획득 차이. Flux Raider 개머리판은 이미 수치가 있어 제외했다. |
| 핸드가드·확장부 | 14 | 무게·관성·조작성 차이. 이 수에는 MK14 상부 레일 등 HandguardExt도 포함한다. |
| 총구장치·소음기 | 10 | 반동·총구상승·조작성 차이. 총구 시각 효과는 별도 적용된다. SRD9 소음기는 이미 수치가 있어 제외했다. |
| MP5·P320 아이언사이트 | 5 | 조준선 정의만 있으며 조준 획득·정밀도·식별거리 보너스가 없다. |
| 피스톨 그립 | 4 | A2, MK14 EBR, M249, 1911 GI의 조작성 차이. MP5 트리거 하우징과 P320 그립 모듈은 각 항목에 별도 기재했다. |

추가 데이터 불일치: `HD_ModularPart_Muzzle_SEI2000V`는 이름/설명이 muzzle brake지만 `muzzleEffectKind=FlashHider`다. 이는 성능 수치 미입력과 별개의 총구 효과 분류 문제다.

## 고유 성능 수치 없는 131종 전체

아래 표의 “기존 동작/판정”은 이미 연결된 기능을 뜻한다. 반동 감소·조준속도 향상 등의 고유 보너스가 존재한다는 뜻은 아니다.

### AR-15 / M4 — 19종

정의: [ModularWeapons_Development.xml](../Defs/ModernWar/Items/ModularWeapons_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Bolt_AR15_Part` | AR-15 bolt carrier group | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_AR15_Part` | AR-15 trigger | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_UpperReceiver_M4A1_Part` | M4A1 upper receiver | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_AR15103_Part` | 10.3 inch AR-15 barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_GasBlock_MK12LP` | MK12 low-profile gas block | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_AR15300BLK103_Part` | 10.3 inch .300 BLK AR-15 barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_GasBlock_300BLKLP` | .300 BLK low-profile gas block | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_AR15145_Part` | 14.5 inch AR-15 barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Muzzle_HACNT4_Part` | HAC NT4 muzzle device | 총구 효과(MuzzleBrake) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Suppressor_BrightStrike4Prong` | BrightStrike four-prong suppressor | 총구 효과(Suppressor) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Muzzle_BrightStrikeSF3P762_Part` | BrightStrike SF3P 7.62 flash hider | 총구 효과(FlashHider) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Handguard_HACRISFDE` | HAC RIS FDE handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_HACURXM4UP` | HAC URX M4 upper handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_HandguardExt_HACURXM4DOWN` | HAC URX M4 lower handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Light_BrightStrikeM600_Part` | BrightStrike M600 weapon light | 조명 기능 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_PistolGrip_IronFangA2_Part` | IronFang A2 pistol grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_BufferTube_Colt` | Colt buffer tube | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_FieldFormMOEFDE` | FieldForm MOE FDE stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_M4_Part` | M4 collapsible stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |

### M14 EBR — 4종

정의: [ModularWeapons_M14EBR_Development.xml](../Defs/ModernWar/Items/ModularWeapons_M14EBR_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Handguard_MK14EBR` | MK14 EBR chassis handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_HandguardExt_MK14EBRTopRail` | MK14 EBR receiver top rail | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_PistolGrip_MK14EBR` | MK14 EBR pistol grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_MK14EBR` | MK14 EBR adjustable stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |

### M14 / SOCOM 16 — 19종

정의: [ModularWeapons_M14_Development.xml](../Defs/ModernWar/Items/ModularWeapons_M14_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Barrel_M1422_Part` | M14 22 inch barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_M1418_Part` | M14 18 inch barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_M1A16_Part` | M1A SOCOM 16 barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Bolt_M14_Part` | M14 bolt | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_OperatingRod_M14_Part` | M14 operating rod | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_TriggerGroup_M14_Part` | M14 trigger group | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_M14_Part` | M14 trigger | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_GasTube_M14_Part` | M14 gas system | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_GasTube_MK14EBR` | MK14 EBR gas system | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_GasTube_M1ASocom16_Part` | M1A SOCOM 16 gas system | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_M14Wood` | M14 wooden stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_StockExt_M14WoodTopCover` | M14 wooden stock top cover | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_Socom16_Part` | SOCOM 16 stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Muzzle_M14_Part` | M14 flash hider | 총구 효과(FlashHider) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Muzzle_SEI2000V` | SEI 2000V muzzle brake | 보정기 표기이나 FlashHider 지정. 반동·총구상승 보정 수치 없음 |
| `HD_ModularPart_Muzzle_Socom16_Part` | SOCOM 16 muzzle brake | 총구 효과(MuzzleBrake) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Muzzle_Socom16Thread` | SOCOM 16 muzzle thread adapter | 총구 효과(Bare) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Magazine_M1420_Part` | 20-round M14 magazine | 용량 20발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Ammunition_762M80_Part` | 7.62x51mm M80 ammunition | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

### M16A4 및 공용 부품 — 10종

정의: [ModularWeapons_M16A4_Development.xml](../Defs/ModernWar/Items/ModularWeapons_M16A4_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_UpperReceiver_M16A4_Part` | M16A4 upper receiver | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_AR1520_Part` | 20 inch AR-15 barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_HACURXUP` | HAC URX upper handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_HandguardExt_HACURXDOWN` | HAC URX lower handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Magazine_STANAG` | STANAG magazine | 용량 30발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_MagazineExt_StanagGrip` | STANAG magazine pull grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_MagazineWrap_StanagTapeBlue` | blue STANAG magazine tape | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_BufferTube_ColtA2_Part` | Colt A2 rifle buffer tube | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_IronFangA2_Part` | IronFang A2 stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_OpticMount_LumiconTA51_Part` | Lumicon TA51 rail mount | 장착·형상 역할 있음. 고유 성능 수치 없음 |

### M16A1 / M16A2 — 8종

정의: [ModularWeapons_M16Legacy_Development.xml](../Defs/ModernWar/Items/ModularWeapons_M16Legacy_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_UpperReceiver_M16A1_Part` | M16A1 upper receiver | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_M16A1_Part` | M16A1 triangular handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_UpperReceiver_M16A2_Part` | M16A2 upper receiver | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_M16A2UP` | M16A2 upper handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_HandguardExt_M16A2DOWN` | M16A2 lower handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_AR1520Pencil` | 20 inch AR-15 pencil barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Muzzle_M16A1_Part` | M16A1 flash suppressor | 총구 효과(FlashHider) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_Magazine_STANAG20_Part` | 20-round STANAG magazine | 용량 20발 적용. 고유 조작성 수치 없음 |

### M1911 — 9종

정의: [ModularWeapons_M1911_Development.xml](../Defs/ModernWar/Items/ModularWeapons_M1911_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Slide_M1911A1_UP` | M1911A1 slide | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Slide_M1911A1_DOWN` | Slide M1911A1 DOWN | 미사용 구형 하부 슬라이드. 마운트가 없어 일반 장착 대상 아님 |
| `HD_ModularPart_Barrel_M1911A1_Part` | Barrel M1911A1 | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Hammer_M1911A1_Part` | Hammer M1911A1 | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Safety_M1911A1Grip` | Safety M1911A1Grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_M1911A1_Part` | Trigger M1911A1 | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_PistolGrip_1911GI` | PistolGrip 1911GI | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Magazine_1911GI7_Part` | Magazine 1911GI7 | 용량 7발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Ammunition_45ACPM1911_Part` | .45 ACP M1911 ammunition | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

### M203A2 — 2종

정의: [ModularWeapons_M203A2.xml](../Defs/ModernWar/Items/ModularWeapons_M203A2.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_UnderbarrelBarrel_M203A2_Part` | M203A2 launcher barrel | M203 몸체의 총열·방아쇠 유무 검사에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_UnderbarrelTrigger_M203A2_Part` | M203A2 launcher trigger | M203 몸체의 총열·방아쇠 유무 검사에 사용. 고유 성능 수치 없음 |

### M249 — 13종

정의: [ModularWeapons_M249.xml](../Defs/ModernWar/Items/ModularWeapons_M249.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Barrel_M249E2_Part` | M249 E2 barrel assembly | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_M249E3_Part` | M249 E3 barrel assembly | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_M249E2_Part` | M249 E2 handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_M249E3_Part` | M249 E3 railed handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_M249E2_Part` | M249 E2 fixed stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_M249E3_Part` | M249 E3 stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_TopCover_M249E2_Part` | M249 E2 feed cover | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_TopCover_M249E3_Part` | M249 E3 railed feed cover | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_ChargingHandle_M249_Part` | M249 charging handle | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_PistolGrip_M249_Part` | M249 pistol grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_M249_Part` | M249 trigger | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Magazine_M249200_Part` | M249 200-round belt box | 용량 200발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Ammunition_M249_M855A1_Part` | M249 M855A1 belt ammunition | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

### MP5 — 17종

정의: [ModularWeapons_MP5_Development.xml](../Defs/ModernWar/Items/ModularWeapons_MP5_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_CockingTube_MP5_Part` | MP5 cocking tube | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_CockingTube_MP5SD` | MP5SD cocking tube | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_MP5_Part` | MP5 8.9 inch barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_MP5SD` | MP5SD ported barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_MP5Default` | MP5 slim handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Handguard_MP5SD` | MP5SD handguard | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Suppressor_MP5SD` | MP5SD integral suppressor | 총구 효과(Suppressor) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_FrontSight_MP5_Part` | MP5 front sight | 조준선 판정 있음. 조준 획득·정밀도·식별거리 보너스 없음 |
| `HD_ModularPart_FrontSight_MP5SD` | MP5SD front sight | 조준선 판정 있음. 조준 획득·정밀도·식별거리 보너스 없음 |
| `HD_ModularPart_RearSight_MP5_Part` | MP5 drum rear sight | 조준선 판정 있음. 조준 획득·정밀도·식별거리 보너스 없음 |
| `HD_ModularPart_OpticRail_MP5_Part` | MP5 upper Picatinny rail | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Bolt_MP5_Part` | MP5 bolt group | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_MP5_Part` | MP5 trigger | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_TriggerHousing_MP5A3_Part` | MP5A3 trigger housing and pistol grip | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Magazine_MP530_Part` | 30-round MP5 magazine | 용량 30발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Stock_MP5A2_Part` | MP5A2 fixed stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Stock_MP5A3_Part` | MP5A3 retractable stock | 장착·형상 역할 있음. 고유 성능 수치 없음 |

### M17 / Flux 색상 변형 — 3종

정의: [ModularWeapons_P320Variants.xml](../Defs/ModernWar/Items/ModularWeapons_P320Variants.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Receiver_M17_Part` | M17 tan grip module | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Receiver_FluxRaiderKitTan` | Flux Raider tan P320 chassis | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Slide_M17_Part` | M17 tan 4.7-inch slide | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |

### P320 / Flux Raider — 17종

정의: [ModularWeapons_P320_Development.xml](../Defs/ModernWar/Items/ModularWeapons_P320_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Receiver_P320_Part` | P320 full-size grip module | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Receiver_FluxRaiderKit` | Flux Raider P320 chassis | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Slide_P32047_Part` | P320 4.7-inch slide | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_P32047_Part` | P320 4.7-inch barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Barrel_P32047Threaded` | P320 4.7-inch threaded barrel | 구경/장착 기능 적용. 총구 기하가 있으면 총열 길이 계산에 사용. 고유 성능 수치 없음 |
| `HD_ModularPart_Trigger_P320Flat` | P320 flat trigger | 장착·필수/기능성 판정 등 구조 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_IronSight_P320Rear` | P320 rear iron sight | 조준선 판정 있음. 조준 획득·정밀도·식별거리 보너스 없음 |
| `HD_ModularPart_IronSight_P320Front` | P320 front iron sight | 조준선 판정 있음. 조준 획득·정밀도·식별거리 보너스 없음 |
| `HD_ModularPart_Mount_T2Low` | T2 low Picatinny mount | 장착·형상 역할 있음. 고유 성능 수치 없음 |
| `HD_ModularPart_Muzzle_9mmThreadProtector` | 9mm thread protector | 총구 효과(Bare) 적용. 고유 반동·조작성 수치 없음 |
| `HD_ModularPart_FrontMagazine_P32017_Part` | 17-round Flux Raider spare magazine | 전방 예비탄창/탄약 파우치 기능 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_FrontMagazine_P32021_Part` | 21-round Flux Raider spare magazine | 전방 예비탄창/탄약 파우치 기능 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_FrontMagazine_P32030_Part` | 30-round Flux Raider spare magazine | 전방 예비탄창/탄약 파우치 기능 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Magazine_P32017_Part` | 17-round P320 magazine | 용량 17발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Magazine_P32021_Part` | 21-round P320 magazine | 용량 21발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Magazine_P32030_Part` | 30-round P320 magazine | 용량 30발 적용. 고유 조작성 수치 없음 |
| `HD_ModularPart_Ammunition_919M882_P320_Part` | 9x19mm M882 P320 ammunition | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

### 권총 액세서리 — 1종

정의: [ModularWeapons_PistolAccessories.xml](../Defs/ModernWar/Items/ModularWeapons_PistolAccessories.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_ModularPart_Mount_RMRLow` | RMR low Picatinny mount | 장착·형상 역할 있음. 고유 성능 수치 없음 |

### 신규 5.56mm 탄종 — 5종

정의: [RifleAmmunition_556.xml](../Defs/ModernWar/Items/RifleAmmunition_556.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_Ammunition_556M193_Ammo` | 5.56x45mm M193 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_556M855_Ammo` | 5.56x45mm M855 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_556M995_Ammo` | 5.56x45mm M995 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_556MK262_Ammo` | 5.56x45mm MK262 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_556MK318_Ammo` | 5.56x45mm MK318 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

### 공용 탄종 — 4종

정의: [RifleAmmunition_Development.xml](../Defs/ModernWar/Items/RifleAmmunition_Development.xml)

| DefName | 표시 이름 | 기존 동작/판정 |
|---|---|---|
| `HD_Ammunition_556M855A1_Ammo` | 5.56x45mm M855A1 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_300BLKTACTX` | .300 BLK TACTX cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_919M882_Ammo` | 9x19mm M882 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |
| `HD_Ammunition_76251M80A1_Ammo` | 7.62x51mm M80A1 cartridge | 투사체 교체로 피해·관통·저지력·탄속 적용. 고유 조작성 수치 없음 |

## 수치가 있어도 장착 조건에 따라 보너스가 제한되는 파츠

이는 고유 수치 미입력과 다른 범주이며, 조건부 적용 로직이 의도적으로 존재한다.

| 대상 | 성능 보너스 제한 조건 |
|---|---|
| VOTech EXPS, G33, Lumicon TA11, PointSight T2, Lumicon RMR, AR-15 가늠쇠 가스블록 | 활성 조준 그룹에 선택된 부품만 적용. 하부 레일 장착은 0, 측면은 50%. 조준선 불일치·가림·배치도 효율에 영향을 준다. |
| Dalton Defense VFG, HAC VFG | 상부 레일은 보너스 0, 측면은 45%. 여러 개를 장착해도 지원 손 하나에 해당하는 1개만 보너스를 받는다. 상부 장착에는 별도 조작성·조준 페널티도 있다. |
| AN/PEQ-15, IZLID Ultra, DBAL-PL | 레이저 보너스는 하나만 선택된다. 상부 85%, 하부 65%의 장착 가중치가 있다. |
| HAC URX ShortLow / Low / Mid 레일커버 | 조작성 이득 순으로 첫째 100%, 둘째 50%, 셋째 25%, 넷째 이후 0%. |

가중치가 0이어도 무게·무게중심과 자체 관성은 제외되지 않는다. 탄창 용량·투사체 선택·총구 효과도 같은 가중치로 무효화하지 않는다.

근거: [장착 방향·중복 가중치](../Source/Helodrace/ModernWar/ModularWeapons/ModularWeaponPartPerformance.cs), [활성 조준선 선택](../Source/Helodrace/ModernWar/ModularWeapons/ModularWeaponSights.cs), [총열 길이·총구 효과](../Source/Helodrace/ModernWar/ModularWeapons/ModularWeaponGasSystem.cs), [총열 길이를 사용하는 파편화](../Source/Helodrace/Core/BulletFragmentation.cs).

## 고유 성능 수치가 설정된 20종

- `HD_ModularPart_Optic_VOTechEXPS` — VOTech EXPS sight
- `HD_ModularPart_Magnifier_VOTechG33_Part` — VOTech G33 magnifier
- `HD_ModularPart_Laser_ANPEQ15_Part` — AN/PEQ-15 laser module
- `HD_ModularPart_Laser_IZLIDUltra` — IZLID Ultra laser module
- `HD_ModularPart_Grip_DaltonDefenseVFG` — Dalton Defense vertical foregrip
- `HD_ModularPart_RailPanel_HACURXShortLow` — HAC URX short rail panel
- `HD_ModularPart_GasBlock_AR15_Part` — AR-15 front sight gas block
- `HD_ModularPart_Scope_LumiconTA11_Part` — Lumicon TA11 scope
- `HD_ModularPart_Grip_HACVFG` — HAC vertical foregrip
- `HD_ModularPart_RailPanel_HACURXLow` — HAC URX lower rail panel
- `HD_ModularPart_RailPanel_HACURXMid` — HAC URX side rail panel
- `HD_ModularPart_Underbarrel_M203A2_Part` — M203A2 underbarrel launcher
- `HD_ModularPart_OpticMount_DPPPlate` — P320 DPP optic plate
- `HD_ModularPart_Stock_FluxRaiderKit` — Flux Raider retractable stock
- `HD_ModularPart_Optic_PointSightT2_Part` — PointSight T2 reflex sight
- `HD_ModularPart_Laser_DBALPL` — DBAL-PL pistol laser
- `HD_ModularPart_Suppressor_HelveticSRD9_Part` — Helvetic SRD9 suppressor
- `HD_ModularPart_Optic_LumiconRMR` — Lumicon RMR reflex sight
- `HD_ModularPart_Light_X300_Part` — X300 pistol weapon light
- `HD_ModularPart_Light_TLR7X` — TLR-7 X compact pistol light
