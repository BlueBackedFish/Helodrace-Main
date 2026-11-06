# 바닐라·헤로드 연구 연결 목록

정의: [ResearchLinks_Helod.xml](../Defs/Research/ResearchLinks_Helod.xml)

`Helodrace.ResearchLinkDef`로 18개 관계를 연결한다. 바닐라 경로 또는 헤로드
경로 중 하나를 완료하면 반대쪽 연구도 완료된다. 표의 **+는 같은 경로 안에서
모두 완료해야 하는 AND 조건**이고, 두 경로 사이는 OR 조건이다. 이 연결은
모든 정착지에 적용되며, 헤로드 연구 탭의 노드 좌표는 바꾸지 않는다.

| 연결 ID | 바닐라 연구 | 헤로드 연구 |
| --- | --- | --- |
| `HD_Link_FrontierFoundations` | 가구 `ComplexFurniture` + 단조 `Smithing` + 섬세한 의류 `ComplexClothing` | 개척정신 `HelodFrontierSpirit` |
| `HD_Link_Electricity` | 전기 `Electricity` | 진공관의 시대 `HelodHighElectronics` |
| `HD_Link_Machining` | 기계 가공 `Machining` | 수치제어 `HelodNumericalControl` |
| `HD_Link_AirConditioning` | 냉방 장치 `AirConditioning` | 열을 훔치는 장치 `HelodHeatPump` + 진공관의 시대 `HelodHighElectronics` |
| `HD_Link_Microelectronics` | 미소전자공학 `MicroelectronicsBasics` | 실리콘 혁명 `HelodTransistor` |
| `HD_Link_TubeTelevision` | 음극선관 TV `TubeTelevision` | 전선의 속박을 끊고 `HelodWireless` |
| `HD_Link_DeepDrilling` | 심층 굴착 `DeepDrilling` | 깊은 곳 까지 `HelodHighOilDevelopment` + 실리콘 혁명 `HelodTransistor` |
| `HD_Link_DrugProduction` | 약물 제조 `DrugProduction` | 자연의 정수 `HelodIngredientExtraction` |
| `HD_Link_MedicineProduction` | 약품 생산 `MedicineProduction` | 분자 조립 라인 `HelodTotalSynthesisMedicine` + 실리콘 혁명 `HelodTransistor` |
| `HD_Link_ToxGas` (Biotech) | 독소 가스 `ToxGas` | 독가스의 바람 `HelodChokingAgent` + 수치제어 `HelodNumericalControl` |
| `HD_Link_SterileMaterials` | 항균 자재 `SterileMaterials` | 죽음을 미루는 기술 `HelodFirstAid` + 진공관의 시대 `HelodHighElectronics` |
| `HD_Link_Gunsmithing` | 총기 제작 `Gunsmithing` | 완성된 소총 `HelodBoltAction` + 수치제어 `HelodNumericalControl` |
| `HD_Link_BlowbackOperation` | 노리쇠 장치 `BlowbackOperation` | 한 번 당기면 한 발 `HelodSamiAutomaticPistol` + 수치제어 `HelodNumericalControl` |
| `HD_Link_GasOperation` | 가스압 장치 `GasOperation` | 소총수의 세기 `HelodBasicModernRifle` + 탄막 지대 `HelodHighMachineGun` + 수치제어 `HelodNumericalControl` |
| `HD_Link_PrecisionRifling` | 정교한 강선 `PrecisionRifling` | 서브 MOA 정밀도 `HelodSniperRifle` + 사격과 기동 `HelodLowAutomaticRifle` + 수치제어 `HelodNumericalControl` + 실리콘 혁명 `HelodTransistor` |
| `HD_Link_Mortars` | 박격포 `Mortars` | 분해식 포대 `HelodLowLightArtillery` + 수치제어 `HelodNumericalControl` |
| `HD_Link_FlakArmor` | 방탄 장비 `FlakArmor` | 첫 번째 방호층 `HelodFlakJacket` + 수치제어 `HelodNumericalControl` |
| `HD_Link_MultibarrelWeapons` | 다중 총열 `MultibarrelWeapons` | 강철의 장벽 `HelodAntiAirMachineGun` + 실리콘 혁명 `HelodTransistor` |

바닐라 전기는 진공관의 시대를 완료한 시점, 기계 가공은 수치제어를 완료한
시점, 미소전자공학은 실리콘 혁명을 완료한 시점에 대응시킨다. 각 후속 연구도
바닐라 선행 관계에 맞는 이 세 기준을 충족해야 연결된다. 예를 들어 초기
볼트액션 소총만으로는 총기 제작을 완료하지 않으며, 수치제어까지 필요하다.

소형 전동기 연구는 수치제어에 통합했다. 수치제어 비용은 두 연구의 합계인
1,000이며, 소형 전동기에 연결됐던 파워커터·착암기·못총과 대공 기관포의
선행 연구도 수치제어로 통일했다. 별도의 구형 연구 노드와 번역은 남기지 않는다.

## 선행 연구와 연쇄 완료

이 연결은 선행 조건만 우회하는 기능이 아니라 **완료 상태를 양방향으로 공유하는
기능**이다. 기존 코드가 호출하는 RimWorld 1.6의 `ResearchManager.FinishProject`는
완료할 대상의 미완료 선행 연구도 재귀적으로 완료한다. 그 결과 다른 연결의
조건도 만족하면 연쇄 완료가 일어난다.

- 바닐라 전기를 완료하면 진공관의 시대와 그 선행 연구가 완료된다. 기계 가공은
  수치제어가 완료되기 전에는 연결되지 않는다.
- 열펌프나 기초 소독 기술만으로는 냉방 장치·항균 자재를 통해 바닐라 전기가
  먼저 완료되지 않는다. 진공관의 시대까지 완료해야 한다.
- 분자 조립 라인만으로는 약품 생산이 완료되지 않는다. 실리콘 혁명까지 완료한
  뒤 연결되므로, 합성 의약 기술이 미소전자공학을 먼저 완료시키지 않는다.
- 정교한 강선에는 수치제어와 실리콘 혁명이 모두 필요하다. 기계 가공의 후속
  연결이 미소전자공학을 앞당기지 않는다.
- 한 연구가 완료될 때 동기화하며, 새 게임 시작과 저장 불러오기 때도 이미
  완료된 연구를 동기화한다. 이 목록을 추가한 기존 저장에도 적용된다.
- 헤로드 서부 개척 시나리오의 시작 연구 6개만으로는 추가 산업·군사 단계가
  자동 완료되지 않는다.
- 연구 탭 사이에 시각적인 연결선을 추가하지는 않는다. 대응 관계는 이 목록을
  기준으로 확인한다.

## 연결하지 않은 항목

| 바닐라 연구 | 제외한 이유 |
| --- | --- |
| 축전지 `Batteries` | 병 속의 번개는 일차 전지이며 충전식 전력 저장 기술과 구분한다. |
| 바이오연료 정제 `BiofuelRefining` | 생물 자원으로 연료를 만드는 기술과 원유의 분별 증류를 구분한다. |
| 부품 조립 `Fabrication`, 고급 부품 조립 `AdvancedFabrication` | 공작기계 자동화와 고급 전자부품·우주 기술 제작 설비를 동일시하지 않는다. |
| 방어막·동면·강화갑옷·우주선 및 DLC 전용 연구 | 현재 종족 연구에 직접 대응하는 기술이 없다. |

산업 자동화·복합가공, 후기 제식소총, 유선 유도탄, 현대 방탄 장비도 이번
목록의 직접 연결 대상에 포함하지 않는다.

## 정의 형식

각 측의 목록은 모두 완료해야 한다. 한쪽이 완료되면 반대쪽의 목록 전체를
완료하며, 연결을 더 추가하려면 같은 형식의 `ResearchLinkDef`를 등록한다.

```xml
<Helodrace.ResearchLinkDef>
  <defName>HD_Link_Machining</defName>
  <leftProjects><li>Machining</li></leftProjects>
  <rightProjects>
    <li>HelodNumericalControl</li>
  </rightProjects>
</Helodrace.ResearchLinkDef>
```

## 검증

저장소 루트에서 실행한다. 테스트는 실제 연결 코드와 Core·종족 XML을 읽고,
게임의 선행 연구 재귀 완료와 완료 후 콜백을 모사한다. 그룹의 부분 완료,
양방향·연쇄 완료, 시작·불러오기 동기화, 예외 후 복구, 무관한 상위 연구가
완료되지 않는 것을 검사한다. 진공관·수치제어·트랜지스터의 완료 단계별로
바닐라 후속 연구가 앞당겨지지 않는 것과 소형 전동기 통합도 검사한다.
인게임 검증은 별도다.

```powershell
dotnet run --project Source/Tests/LinkedResearch/LinkedResearch.Tests.csproj
```

게임 경로가 다르면 `RIMWORLD_PATH` 환경 변수 또는 실행 인자로 게임 폴더를 지정한다.

2026-11-03 I 그룹에서 연결 관계를 유지하면서 연구 예산을 재배분했다. 수치제어 기본량은 1,800, 시나리오는 엔진의 부족민 기술 계수를 사용한다. [바닐라·헤로드 예산 비교와 검증](I_Research_Balance.md).
