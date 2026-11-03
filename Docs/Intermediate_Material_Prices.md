# 산업 중간재 가격과 전자 부품

2026-11-03. 사용자 정정에 따라 품목 수 제한을 제거했다. 기준은 실제 1.7.4995 rev309의 바닐라 산업 부품 `ComponentIndustrial` **32 은**이다. 기존 모드의 산업 재료 22종은 모두 32보다 낮게 조정하고, 새 전자 부품 `HD_ElectronicComponent`는 32로 맞췄다.

## 가격표

| 품목 | DefName | 이전 기준가 | 변경 기준가 | 인하율 |
|---|---|---:|---:|---:|
| 기초 전기 부품 | `HD_BasicElectricalComponents` | 18 | 6 | 66.67% |
| 기초 전자 부품 | `HD_BasicElectronicComponents` | 32 | 12 | 62.5% |
| 기초 의료용품 | `HD_BasicMedicalSupplies` | 24 | 10 | 58.33% |
| 중질유 | `HD_CrudeOil_Low` | 1.5 | 0.6 | 60% |
| 디젤 | `HD_Diesel` | 4 | 1.6 | 60% |
| 등유 | `HD_Kerosene` | 3.5 | 1.4 | 60% |
| 나프타 | `HD_Naphtha` | 5 | 2 | 60% |
| 방향족 기초유분 | `HD_AromaticBaseOil` | 7 | 2.8 | 60% |
| 숯 | `HD_Charcoal` | 2 | 0.8 | 60% |
| 주철 | `HD_PigIron` | 8 | 2.4 | 70% |
| 탄소강 | `HD_CarbonSteel` | 12 | 3.2 | 73.33% |
| 샤프트 | `HD_Shaft` | 18 | 6 | 66.67% |
| 프레스 부품 | `HD_PressComponent` | 8 | 3.2 | 60% |
| 범용 기계 부품 | `HD_GeneralMachinePart` | 14 | 6 | 57.14% |
| 균일압연강판 | `HD_UniformRolledSteelPlate` | 20 | 8 | 60% |
| 일차 전지 | `HD_PrimaryCell` | 3.5 | 1.4 | 60% |
| 질산암모늄 | `HD_AmmoniumNitrate` | 4 | 1.6 | 60% |
| TNT | `HD_TNT` | 12 | 4.8 | 60% |
| RDX | `HD_RDX` | 18 | 7.2 | 60% |
| C-4 | `HD_C4_Charge` | 24 | 9.6 | 60% |
| M60 도화선 점화기 | `HD_M60Igniter` | 95 | 20 | 78.95% |
| M81 점화기 | `HD_M81Igniter` | 140 | 28 | 80% |
| 전자 부품(신규) | `HD_ElectronicComponent` | 신규 | 32 | 바닐라 부품과 동일 |

범위는 `Materials_WildWest.xml`, `Materials_GreatWar.xml`, `Materials_ColdWar.xml`에 정의된 산업 원료·제련재·기계/전기/전자/의료 중간재·연료/화학 제품·점화기 22종이다. 금융 상품, 식품·장난감, 완성 무기·탄약·교체용 모듈 부품은 각 완제품 체계로 관리한다. 2026-11-04 알루미나·알루미늄 합금과 실리콘 3종을 같은 기준으로 추가했다.

제작량과 작업량은 기존 공정 규격을 따른다. 기준가격은 제작 투입재의 시장가 합계를 그대로 보상하는 값으로 쓰지 않는다. 실제 거래 가격에는 기본 게임의 협상·상단 매입/매각 계수가 적용된다.

2026-11-04 신규 재료 기준가: 알루미나(`HD_Alumina`) 3, 실리콘(`HD_Silicon`) 6, 알루미늄 합금(`HD_AluminiumAlloy`) 8 은. 기존 22종과 합쳐 25종의 산업 중간재를 32 미만으로 유지한다. [생산과 소비 연결](Texture_Owned_Items.md).

## 새 전자 부품 연결

- `HD_BasicElectronicComponents`는 진공관식 기초 전자 부품이며 기준가 12다. 새 이미지는 별도의 반도체 전자 부품 `HD_ElectronicComponent`에 연결했다. 두 스택 이미지 `_a`, `_b`를 사용한다.
- 실리콘 혁명(`HelodTransistor`) 이후 반도체 팹에서 기존 반도체 제조 공정으로 전자 부품을 만든다. 전자 부품 1개를 산업용 부품 1개로 조립하는 300 작업 공정을 같은 팹에 연결했다. 동일 기준가에 추가 작업이 들어가므로 조립만으로 상품 가치가 늘어나지 않는다.
- 1970년대 유압 프레스·압연기·머시닝 센터와 2000년대 복합 가공기가 새 부품을 사용한다. 네 설비 모두 연구 선행 경로에 실리콘 혁명이 포함된다. 팹 자체는 기초 전자 부품으로 건설하므로 자기 생산물을 요구하는 순환이 없다.
- 공업 회사 상단, 민간 정착지와 군수 정착지에서 새 전자 부품을 판매·매입한다. 기초 생산 계획대는 기존 여섯 기초 부품의 목표 재고를 관리한다.

## 검증

`Source/Tests/IntermediatePrices/Verify-Definitions.ps1`는 세 산업 재료 파일의 전체 품목을 가격표와 대조하고 네 소비 설비의 연구 선행 경로를 검사한다. `Source/Verify-RecipePlacement.ps1`는 86개 명시 조합법·533개 구체 ThingDef와 전체 작업대 연결을 통과했다.

실제 게임 `.codex-staging/PriceStageH/actual-01/Evidence/checks.txt`: **49개 PASS, COMPLETE, FAIL 없음**. 모든 중간재의 실제 시장가, 바닐라 부품 가격 32/200 보존, 새 부품의 실제 매입·매각 가격 일치, 구매·판매에 의한 물품 이동, 두 스택 그래픽, 무동력 제작 차단, 실제 전력망에서 6개 생산과 정확한 재료 소비, 1:1 산업용 부품 조립, 실제 새 전자 부품 6개의 건설 프레임 투입·소비, 저장/로드 후 남은 3개와 설비 보존을 확인했다. 건설 검사는 자재를 실제 프레임에 넣은 뒤 기본 `CompleteConstruction`을 호출하며, 운반·건설 노동 AI 전체를 재검증한 것으로 세지 않는다.

| 설비 | 이전 재료 소계 | 변경 재료 소계 | 현재 엔진 시장가 |
|---|---:|---:|---:|
| 대형 증기기관 | 1440 | 416 | 875 |
| 무전기 | 204 | 124 | 134.8 |
| 초기 반도체 팹 | 1074 | 626 | 650 |
| 1970년대 유압 프레스 | 2598 | 1222 | 1255 |

이전 재료 소계는 가격표의 이전 값으로 같은 투입 수량을 계산한 값이며, 이전 엔진 시장가 실측과 구분한다. 최종 게임 ERROR는 작업 전부터 있던 Workshop 항목 3781143823의 폴더 부재뿐이다.

가격 조정 후 `.codex-staging/PriceStageH/trade-regression/Evidence/checks.txt`에서 H 그룹 99개 PASS·COMPLETE를 확인했다. 시작 자금 장바구니, 두 정착지와 잡상단 거래, 낮아진 상품 가격에서의 최소 거래액 약정 이행·취소·공격·저장 정산을 다시 통과했다. `.codex-staging/PriceStageH/planner-regression/Evidence/checks.txt`에서는 자동 생산 계획대 25개 PASS·COMPLETE로 여섯 목표 재고와 중복 방지·자재 예산·미완성품·두 저장/로드를 다시 검증했다. 세 실행 모두 새 ERROR나 저장 참조 경고가 없다.
