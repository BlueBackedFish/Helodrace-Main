# 바닐라·헬로드 연구 연결 목록

정의: [ResearchLinks_Helod.xml](../Defs/Research/ResearchLinks_Helod.xml)

`Helodrace.ResearchLinkDef`로 15개 관계를 연결한다. 바닐라 경로 또는 헬로드
경로 중 하나를 완료하면 반대쪽 연구도 완료된다. 표의 **+는 같은 경로 안에서
모두 완료해야 하는 AND 조건**이고, 두 경로 사이는 OR 조건이다. 이 연결은
모든 정착지에 적용되며, 헬로드 연구 탭의 노드 좌표는 바꾸지 않는다.

| 연결 ID | 바닐라 연구 | 헬로드 연구 |
| --- | --- | --- |
| `HD_Link_FrontierFoundations` | 가구 `ComplexFurniture` + 단조 `Smithing` + 섬세한 의류 `ComplexClothing` | 개척정신 `HelodFrontierSpirit` |
| `HD_Link_Electricity` | 전기 `Electricity` | 옴의 법칙 응용 `HelodBasicElectronics` + 운동-전기 시너지 `HelodBasicMotor` |
| `HD_Link_Machining` | 기계 가공 `Machining` | 천분의 일 인치를 향하여 `HelodMetalCuttingMachine` + 압력의 예술 `HelodMetalFormingMachine` |
| `HD_Link_AirConditioning` | 냉방 장치 `AirConditioning` | 열을 훔치는 장치 `HelodHeatPump` |
| `HD_Link_Microelectronics` | 미소전자공학 `MicroelectronicsBasics` | 실리콘 혁명 `HelodTransistor` |
| `HD_Link_DrugProduction` | 약물 제조 `DrugProduction` | 자연의 정수 `HelodIngredientExtraction` |
| `HD_Link_MedicineProduction` | 약품 생산 `MedicineProduction` | 분자 조립 라인 `HelodTotalSynthesisMedicine` |
| `HD_Link_SterileMaterials` | 항균 자재 `SterileMaterials` | 죽음을 미루는 기술 `HelodFirstAid` |
| `HD_Link_Gunsmithing` | 총기 제작 `Gunsmithing` | 완성된 소총 `HelodBoltAction` |
| `HD_Link_BlowbackOperation` | 노리쇠 장치 `BlowbackOperation` | 한 번 당기면 한 발 `HelodSamiAutomaticPistol` |
| `HD_Link_GasOperation` | 가스압 장치 `GasOperation` | 소총수의 세기 `HelodBasicModernRifle` + 탄막 지대 `HelodHighMachineGun` |
| `HD_Link_PrecisionRifling` | 정교한 강선 `PrecisionRifling` | 서브 MOA 정밀도 `HelodSniperRifle` + 사격과 기동 `HelodLowAutomaticRifle` |
| `HD_Link_Mortars` | 박격포 `Mortars` | 분해식 포대 `HelodLowLightArtillery` |
| `HD_Link_FlakArmor` | 방탄 장비 `FlakArmor` | 첫 번째 방호층 `HelodFlakJacket` |
| `HD_Link_MultibarrelWeapons` | 다중 총열 `MultibarrelWeapons` | 강철의 장벽 `HelodAntiAirMachineGun` |

전력은 회로와 발전·전동 설비를 합친 경로, 가공은 절삭과 성형을 합친 경로로
대응시킨다. 화기는 제작 단계와 장비 범위를 기준으로 묶는다. 정교한 강선은
저격총과 초기 돌격소총을 모두 포함하며, 저격총 연구 하나만으로는 충족되지 않는다.

## 선행 연구와 연쇄 완료

이 연결은 선행 조건만 우회하는 기능이 아니라 **완료 상태를 양방향으로 공유하는
기능**이다. 기존 코드가 호출하는 RimWorld 1.6의 `ResearchManager.FinishProject`는
완료할 대상의 미완료 선행 연구도 재귀적으로 완료한다. 그 결과 다른 연결의
조건도 만족하면 연쇄 완료가 일어난다.

- 바닐라 전기를 완료하면 회로·전동기 경로가 완료된다. 전동기의 선행 가공
  연구들이 완료되면서 바닐라 기계 가공도 완료된다.
- 분자 조립 라인을 완료하면 바닐라 약품 생산이 완료된다. 약품 생산의 선행인
  미소전자공학도 완료되므로 실리콘 혁명까지 연결된다.
- 한 연구가 완료될 때 동기화하며, 새 게임 시작과 저장 불러오기 때도 이미
  완료된 연구를 동기화한다. 이 목록을 추가한 기존 저장에도 적용된다.
- 헬로드 서부 개척 시나리오의 시작 연구 6개만으로는 추가 산업·군사 단계가
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
    <li>HelodMetalCuttingMachine</li>
    <li>HelodMetalFormingMachine</li>
  </rightProjects>
</Helodrace.ResearchLinkDef>
```

## 검증

저장소 루트에서 실행한다. 테스트는 실제 연결 코드와 Core·종족 XML을 읽고,
게임의 선행 연구 재귀 완료와 완료 후 콜백을 모사한다. 그룹의 부분 완료,
양방향·연쇄 완료, 시작·불러오기 동기화, 예외 후 복구, 무관한 상위 연구가
완료되지 않는 것을 검사한다. 인게임 검증은 별도다.

```powershell
dotnet run --project Source/Tests/LinkedResearch/LinkedResearch.Tests.csproj
```

게임 경로가 다르면 `RIMWORLD_PATH` 환경 변수 또는 실행 인자로 게임 폴더를 지정한다.
