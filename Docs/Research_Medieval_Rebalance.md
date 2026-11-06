# 연구 비용 재조정: 중세 기준 기술 보정

2026-11-06. [이전 예산 설계](I_Research_Balance.md)를 대체한다. 생성·검증 스크립트는 [research_rebalance.py](../Source/Analysis/ResearchMedieval/research_rebalance.py), 실제 게임 검사용 정책은 [research_policy.json](Data/ResearchMedieval/research_policy.json)이다.

## 바뀐 원칙

1. **헤로드 시나리오의 기술 보정을 중세 정착지 기준으로 바꾼다.** 실제 세력은 Industrial을 유지하고, 연구 비용 계수만 엔진의 `CostFactor(Medieval)`을 쓴다. 엔진은 기술 차이를 Industrial에서 끊으므로 중세 이하 연구는 ×1, **산업 이후 연구는 모두 ×1.5**다(이전 부족민 기준은 ×2). 다른 시나리오에는 적용하지 않는다.
2. **헤로드 연구 기본량을 시대별로 나눈다.** 이전에는 66개가 대부분 600~1,200에 몰려 있어서 초반 필수 기반(제철·증기 기관·라인 샤프트)이 후반 전문 장비만큼 비쌌다. 개척 시대 400~700, 후기 개척 600~1,100, 1차 대전 800~1,800, 냉전 900~2,500, 현대 1,400~3,200으로 바꿨다. 시작 완료 연구 3개는 그대로다. 기본량 합계는 68,650 → 76,100이지만 계수가 줄어 전체 작업량은 줄어든다.
3. **헤로드와 연결된 바닐라 연구 17개는 연결로 얻는 헤로드 지식만큼 비용을 둔다.** 한쪽을 완료하면 반대쪽과 그 선행 연구까지 완료되므로, 바닐라 쪽이 더 싸면 헤로드 트리를 건너뛰는 지름길이 된다. 그래서 이 14개는 개별로 깎지 않고 헤로드 비용을 따라 다시 계산했다. 17개 모두 두 경로의 누적 작업량이 같다. 이후 음극선관 TV·심층 굴착·독소 가스(Biotech) 연결 3개를 추가했다([연결 목록](LinkedResearch.md)).
4. **연결되지 않은 바닐라 연구는 바닐라 비용으로 되돌린다.** 이전에 헤로드 산업 예산에 맞춰 올렸던 24개(배터리·태양광·지열·부품 제작·고급 부품 제작·파워 아머 등)를 원래 값으로 복원했다. 느려지는 정도는 기술 보정 ×1.5만으로 맞춘다.

## 누적 작업량

시작 완료 연구를 뺀 실효 작업량이다. 소요일은 속도 100%로 24시간 연속 연구할 때의 값(하루 495점)이며 식사·수면·이동은 포함하지 않는다.

| 목표 | 이전 작업량 | 변경 작업량 | 소요일 이전 → 변경 |
|---|---:|---:|---:|
| 증기 기관 (`HelodSteamEngine`) | 4,400 | 1,950 | 8.9 → 3.9 |
| 전신 (`HelodTelegraph`) | 2,400 | 1,350 | 4.8 → 2.7 |
| 유정 개발 (`HelodBasicOilDevelopment`) | 6,200 | 3,000 | 12.5 → 6.1 |
| 라인 샤프트 (`HelodLineShaft`) | 8,400 | 3,600 | 17.0 → 7.3 |
| 볼트액션 소총 (`HelodBoltAction`) | 19,700 | 10,800 | 39.8 → 21.8 |
| 석유화학 (`HelodBasicPetrochemistry`) | 19,400 | 10,350 | 39.2 → 20.9 |
| 진공관 / 바닐라 전기 (`HelodHighElectronics`) | 22,200 | 12,300 | 44.8 → 24.8 |
| 수치제어 / 기계 가공 (`HelodNumericalControl`) | 25,800 | 15,000 | 52.1 → 30.3 |
| 트랜지스터 / 미소전자공학 (`HelodTransistor`) | 28,600 | 17,100 | 57.8 → 34.5 |
| 산업 자동화 (`HelodIndustrialAutomation`) | 36,600 | 23,550 | 73.9 → 47.6 |
| 복합 가공 (`HelodMultiTaskMachining`) | 42,200 | 28,350 | 85.3 → 57.3 |
| 레이저 표적지시 (`HelodLaserTargetDesignators`) | 70,700 | 52,350 | 142.8 → 105.8 |
| 헤로드 전체 66개 | 135,100 | 112,500 | 272.9 → 227.3 |

| 헤로드 연구 | 기본량 이전 → 변경 | 작업량 이전 → 변경 |
|---|---:|---:|
| `HelodFrontierSpirit` | 100 → 100 | 200 → 150 |
| `HelodMechanicalEngineering` | 500 → 500 | 1,000 → 750 |
| `HelodExperimentalAnalysis` | 500 → 500 | 1,000 → 750 |
| `HelodIronMaking` | 1000 → 600 | 2,000 → 900 |
| `HelodSteamEngine` | 1200 → 700 | 2,400 → 1,050 |
| `HelodBasicMachineTool` | 1000 → 500 | 2,000 → 750 |
| `HelodPrimaryCell` | 600 → 400 | 1,200 → 600 |
| `HelodIngredientExtraction` | 700 → 500 | 1,400 → 750 |
| `HelodTelegraph` | 600 → 500 | 1,200 → 750 |
| `HelodLineShaft` | 1000 → 600 | 2,000 → 900 |
| `HelodBreechLoadingGun` | 700 → 500 | 1,400 → 750 |
| `HelodBasicOilDevelopment` | 900 → 700 | 1,800 → 1,050 |
| `HelodMetalCuttingMachine` | 1000 → 800 | 2,000 → 1,200 |
| `HelodMetalFormingMachine` | 1000 → 800 | 2,000 → 1,200 |
| `HelodLeverAction` | 600 → 600 | 1,200 → 900 |
| `HelodHeatPump` | 700 → 700 | 1,400 → 1,050 |
| `HelodLargeSteamEngine` | 900 → 900 | 1,800 → 1,350 |
| `HelodBasicEngine` | 1400 → 1100 | 2,800 → 1,650 |
| `HelodHighOilDevelopment` | 1100 → 1000 | 2,200 → 1,500 |
| `HelodBasicPetrochemistry` | 1200 → 1100 | 2,400 → 1,650 |
| `HelodBasicElectronics` | 1000 → 800 | 2,000 → 1,200 |
| `HelodBasicMotor` | 1200 → 1000 | 2,400 → 1,500 |
| `HelodSmokeLessGunpowder` | 1000 → 900 | 2,000 → 1,350 |
| `HelodBoltAction` | 650 → 700 | 1,300 → 1,050 |
| `HelodTotalSynthesisMedicine` | 1200 → 1100 | 2,400 → 1,650 |
| `HelodFirstAid` | 600 → 600 | 1,200 → 900 |
| `HelodChokingAgent` | 650 → 700 | 1,300 → 1,050 |
| `HelodHighElectronics` | 1500 → 1500 | 3,000 → 2,250 |
| `HelodWireless` | 1000 → 1000 | 2,000 → 1,500 |
| `HelodHaberBosch` | 1600 → 1500 | 3,200 → 2,250 |
| `HelodHighExplosive` | 1000 → 1100 | 2,000 → 1,650 |
| `HelodBasicMachineGun` | 800 → 900 | 1,600 → 1,350 |
| `HelodHighMachineGun` | 800 → 1000 | 1,600 → 1,500 |
| `HelodSamiAutomaticPistol` | 650 → 800 | 1,300 → 1,200 |
| `HelodBasicModernRifle` | 850 → 1100 | 1,700 → 1,650 |
| `HelodGreatWarTech` | 1200 → 1300 | 2,400 → 1,950 |
| `HelodLowGranade` | 600 → 800 | 1,200 → 1,200 |
| `HelodLowLightArtillery` | 900 → 1100 | 1,800 → 1,650 |
| `HelodLowHeavyArtillery` | 1200 → 1400 | 2,400 → 2,100 |
| `HelodBlisterAgent` | 700 → 900 | 1,400 → 1,350 |
| `HelodSniperRifle` | 700 → 900 | 1,400 → 1,350 |
| `HelodRespectForPatrioticMartyrs` | 600 → 800 | 1,200 → 1,200 |
| `HelodNumericalControl` | 1800 → 1800 | 3,600 → 2,700 |
| `HelodTransistor` | 2200 → 2200 | 4,400 → 3,300 |
| `HelodLowAutomaticRifle` | 900 → 1400 | 1,800 → 2,100 |
| `HelodAntiAirMachineGun` | 1200 → 1600 | 2,400 → 2,400 |
| `HelodMidAutomaticRifle` | 1000 → 1500 | 2,000 → 2,250 |
| `HelodWireGuidedMissile` | 1600 → 2000 | 3,200 → 3,000 |
| `HelodColdWarTech` | 1500 → 1800 | 3,000 → 2,700 |
| `HelodLessLethalClose` | 600 → 900 | 1,200 → 1,350 |
| `HelodLessLethalLowRanged` | 650 → 1000 | 1,300 → 1,500 |
| `HelodLessLethalChemical` | 700 → 1100 | 1,400 → 1,650 |
| `HelodSpecialOperationsWeapons` | 1100 → 1500 | 2,200 → 2,250 |
| `HelodFlakJacket` | 900 → 1200 | 1,800 → 1,800 |
| `HelodLowHeadSet` | 700 → 1000 | 1,400 → 1,500 |
| `HelodLowVelocityGrenades` | 800 → 1100 | 1,600 → 1,650 |
| `HelodIndustrialAutomation` | 2200 → 2500 | 4,400 → 3,750 |
| `HelodHighAutomaticRifle` | 1100 → 1800 | 2,200 → 2,700 |
| `HelodPASGT` | 1200 → 1800 | 2,400 → 2,700 |
| `HelodModernDiversionaryGrenades` | 1000 → 1400 | 2,000 → 2,100 |
| `HelodModernOffensiveGrenades` | 1200 → 1500 | 2,400 → 2,250 |
| `HelodAdvancedWeaponOptics` | 1600 → 2000 | 3,200 → 3,000 |
| `HelodLaserTargetDesignators` | 1800 → 2400 | 3,600 → 3,600 |
| `HelodAdvancedAmmunition` | 1400 → 2000 | 2,800 → 3,000 |
| `HelodRemoteWeaponControl` | 1600 → 2400 | 3,200 → 3,600 |
| `HelodMultiTaskMachining` | 2800 → 3200 | 5,600 → 4,800 |

| 바닐라 연구 | 바닐라 | 헤로드 예산 이전 → 변경 | 작업량 이전 → 변경 | 구분 |
|---|---:|---:|---:|---|
| `AdvancedFabrication` | 4000 | 6000 → 4000 | 12,000 → 6,000 | 바닐라 복원 |
| `AirConditioning` | 500 | 700 → 700 | 1,400 → 1,050 | 연결 |
| `Autodoors` | 600 | 800 → 600 | 1,600 → 900 | 바닐라 복원 |
| `Batteries` | 400 | 700 → 400 | 1,400 → 600 | 바닐라 복원 |
| `BiofuelRefining` | 700 | 1000 → 700 | 2,000 → 1,050 | 바닐라 복원 |
| `Bionics` | 2000 | 3000 → 2000 | 6,000 → 3,000 | 바닐라 복원 |
| `BlowbackOperation` | 500 | 1450 → 1700 | 2,900 → 2,550 | 연결 |
| `ChargedShot` | 3000 | 4000 → 3000 | 8,000 → 4,500 | 바닐라 복원 |
| `DeepDrilling` | 1000 | 1800 → 1000 | 3,600 → 1,500 | 바닐라 복원 |
| `DeepDrilling` | 1000 | 1000 → 1700 | 2,000 → 2,550 | 연결 (추가) |
| `DrugProduction` | 500 | 700 → 500 | 1,400 → 750 | 바닐라 복원 |
| `Electricity` | 1600 | 11100 → 8200 | 22,200 → 12,300 | 연결 |
| `Fabrication` | 4000 | 5000 → 4000 | 10,000 → 6,000 | 바닐라 복원 |
| `FlakArmor` | 1200 | 8600 → 7900 | 17,200 → 11,850 | 연결 |
| `GasOperation` | 1000 | 1650 → 2100 | 3,300 → 3,150 | 연결 |
| `GeothermalPower` | 3200 | 4000 → 3200 | 8,000 → 4,800 | 바닐라 복원 |
| `GroundPenetratingScanner` | 1000 | 2000 → 1000 | 4,000 → 1,500 | 바닐라 복원 |
| `GunTurrets` | 500 | 800 → 500 | 1,600 → 750 | 바닐라 복원 |
| `Gunsmithing` | 500 | 3650 → 3200 | 7,300 → 4,800 | 연결 |
| `HeavyTurrets` | 1600 | 2200 → 1600 | 4,400 → 2,400 | 바닐라 복원 |
| `HospitalBed` | 1200 | 1600 → 1200 | 3,200 → 1,800 | 바닐라 복원 |
| `Hydroponics` | 700 | 1000 → 700 | 2,000 → 1,050 | 바닐라 복원 |
| `IEDs` | 500 | 800 → 500 | 1,600 → 750 | 바닐라 복원 |
| `LongRangeMineralScanner` | 2000 | 3000 → 2000 | 6,000 → 3,000 | 바닐라 복원 |
| `Machining` | 1000 | 1800 → 1800 | 3,600 → 2,700 | 연결 |
| `MedicineProduction` | 1500 | 4700 → 4000 | 9,400 → 6,000 | 연결 |
| `MicroelectronicsBasics` | 3000 | 3200 → 3200 | 6,400 → 4,800 | 연결 |
| `MoisturePump` | 1200 | 1500 → 1200 | 3,000 → 1,800 | 바닐라 복원 |
| `Mortars` | 2000 | 8200 → 7700 | 16,400 → 11,550 | 연결 |
| `MultiAnalyzer` | 4000 | 5000 → 4000 | 10,000 → 6,000 | 바닐라 복원 |
| `MultibarrelWeapons` | 2600 | 17050 → 17000 | 34,100 → 25,500 | 연결 |
| `PoweredArmor` | 6000 | 8000 → 6000 | 16,000 → 9,000 | 바닐라 복원 |
| `PrecisionRifling` | 1400 | 9500 → 9700 | 19,000 → 14,550 | 연결 |
| `ReconArmor` | 6000 | 7000 → 6000 | 14,000 → 9,000 | 바닐라 복원 |
| `ShieldBelt` | 1000 | 1500 → 1000 | 3,000 → 1,500 | 바닐라 복원 |
| `SolarPanels` | 600 | 1000 → 600 | 2,000 → 900 | 바닐라 복원 |
| `SterileMaterials` | 600 | 6000 → 5100 | 12,000 → 7,650 | 연결 |
| `TransportPod` | 1000 | 1500 → 1000 | 3,000 → 1,500 | 바닐라 복원 |
| `ToxGas` (Biotech) | 600 | 600 → 5200 | 1,200 → 7,800 | 연결 (추가) |
| `WatermillGenerator` | 700 | 1000 → 700 | 2,000 → 1,050 | 바닐라 복원 |

## 검증

- `python Source/Analysis/ResearchMedieval/research_rebalance.py --check`: 헤로드 66개 기본량, 연결 바닐라 예산 15개(음극선관 TV는 바닐라 비용과 같아 예산 없음), 17개 연결 경로의 누적 작업량 일치.
- `dotnet run --project Source/Tests/LinkedResearch/LinkedResearch.Tests.csproj`: 1,451개 연동 검사 통과(Biotech 연구 포함).
- `Source/Tests/StageI/Start-Checks.ps1`: 실제 1.7 게임 449 PASS, COMPLETE. 다른 시나리오의 바닐라 비용 유지, 헤로드 시나리오의 기본량 81개 적용, 연결되지 않은 Core 연구 63개의 바닐라 비용, 산업 이후 연구 전체의 ×1.5 계수, 정상 연구 진행과 완료, 저장/불러오기 후 1회 적용을 확인했다. 새 ERROR는 없다.

`Source/Analysis/StageI/research_balance.py`와 `Docs/Data/StageI`는 이전 설계의 기록이며 현재 값과 맞지 않는다.
