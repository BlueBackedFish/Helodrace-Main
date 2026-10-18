# 모듈형 총기 스탯 1차 구현

2026-10-18. 정확도·지지·취급·회복의 계산 경로, 파츠 XML, 편집기와 정보 표시를 함께 교체했다. 기본 생성 10종의 정상 품질 정확도와 웜업·쿨다운은 개편 직전 기준으로 교정했다. 모딩 선택의 차이를 만드는 단계이며 전체 총기의 순정 성능을 일괄 상향한 작업은 아니다.

## 적용한 역할

- `mechanicalPrecision`: 총열·탄약·총몸의 기계 정밀도. 거리별 상한에만 기여한다.
- `aimStability`: 트리거·그립·지지 구조의 유지 안정성. 거리별 유지 효율에 기여한다.
- `aimSpeed`: 기존 두 조작성 항목을 통합한 준비성. 웜업에만 직접 기여한다.
- `stock`: 고정식/신축식 개머리판의 물리 길이와 견착 지지 프로필. 기존 개머리판의 정적 반동 가산과 중복 적용하지 않는다.
- 반동·들림·지지로 회복 부담을 계산한다. 쿨다운에 기계 순환 배율을 다시 곱하지 않는다. RPM과 점사 내 간격은 작동계에서 계산한다.
- 광학은 0~1의 거리별 효율이다. 최고 효율 그룹 하나를 사용하며, 표적 거리에서 선택되는 그룹의 조준 준비 비용을 사용한다. 하부 유탄 등 별도 무기 그룹의 조준선·원시 성능은 주 총기에 합산하지 않는다. 그 무게는 계속 들고 있어야 한다.

`ergonomics`, `targetAcquisition`, `aimingPrecision` 및 그 UI/API는 제거했다. 가림용 `aimRadius`는 `sightClearanceRadius`로 바꿨다. 구형 필드나 Flux의 과거 저장 오프셋을 읽는 호환 분기는 추가하지 않았고, Flux의 0 오프셋을 강제로 인출 위치로 바꾸던 구형 이관은 제거했다.

## 물리 입력

파츠의 `internalStats.massPositionMeters`는 총기 기준점에 대한 실제 전후 질량 위치다. 이전 그래픽 피벗 기준 오프셋과 달리 렌더 노드 위치를 더하지 않는다. 총열은 길이의 중간, 개머리판은 정의한 위치에서 상태별 길이의 중간을 사용한다. 1차 물리 값은 플랫폼별 대표 위치의 근사치이며 정밀 실측 모델은 아니다.

전장은 본체 `receiverLengthMeters` + 실제 총열 길이 + 개머리판 길이 + 장치 `lengthExtensionMeters`로 계산한다. 질량·무게중심·관성·전장이 준비 부담에 들어간다. 같은 그래픽을 다른 팔레트나 피벗으로 표현하거나 반동 애니메이션을 재생해도 이 물리 입력은 바뀌지 않는다. 일반 파츠의 그래픽 배치 변경은 이 물리 좌표의 편집을 대신하지 않는다.

본체는 `naturalSupport`로 권총 양손 파지와 소총 미견착의 기준을 구분한다. 최종 지지는 자연 파지와 장착 개머리판 중 높은 값을 쓴다. 같은 개머리판의 질량은 수축/인출 모두 동일하다.

### 신축식 프로필

| 파츠 | 수축 오프셋 | 인출 오프셋 | 수축/인출 길이 m | 수축/인출 지지 |
|---|---:|---:|---:|---:|
| M4 / FieldForm MOE | 0.0375 | -0.0375 | 0.18 / 0.26 | 0.45 / 0.95 |
| MK14 EBR | 0.025 | -0.025 | 0.21 / 0.29 | 0.50 / 0.96 |
| Flux Raider | 0.145 | -0.145 | 0.06 / 0.26 | 0.20 / 0.90 |
| MP5A3 | 0.10 | 0 | 0.12 / 0.26 | 0.45 / 0.90 |

MP5의 연결도 단일 개머리판 레일로 확장했다. MP5A2 고정식은 같은 연결에 장착해도 이동으로 성능이 변하지 않는다. 중간 인출량은 두 끝점 사이를 선형 보간하며, 끝점 순서의 부호는 고정하지 않는다. 이 단계에서는 정상 견착점이 정의한 인출 끝점이다.

```xml
<stock>
  <adjustable>true</adjustable>
  <retractedOffset>0.0375</retractedOffset>
  <extendedOffset>-0.0375</extendedOffset>
  <retractedSupport>0.45</retractedSupport>
  <extendedSupport>0.95</extendedSupport>
  <retractedLengthMeters>0.18</retractedLengthMeters>
  <extendedLengthMeters>0.26</extendedLengthMeters>
</stock>
```

### 동일 구성의 수축 → 인출 예시

가스 1.0, 기본 품질, 사수·파우치 보정 전이다. 정확도는 최종 명중 확률이 아닌 장비 측 원거리 값이다.

| 구성 | 웜업 초 | 쿨다운 초 | 원거리 정확도 |
|---|---:|---:|---:|
| M4 기본 프리셋 | 1.057 → 1.083 | 1.711 → 1.589 | 48.5 → 52.7% |
| MP5 기본 프리셋 | 0.523 → 0.550 | 1.604 → 1.500 | 20.2 → 21.9% |
| MK14 기본 프리셋 | 1.392 → 1.428 | 1.708 → 1.593 | 33.6 → 36.3% |
| P320 Raider 킷 | 0.257 → 0.277 | 0.923 → 0.888 | 24.4 → 25.5% |

7개 신축식 포함 프리셋 모두 웜업 차이가 바닐라 `SecondsToTicks`의 60틱 반올림 후에도 남도록 검증한다. 기계식 점사 간격은 별도로 올림한다. 일반 M4 기본 생성 위치는 두 끝점의 중간이므로 순정 웜업/쿨다운은 별도 표의 1.07/1.65초다.

## 본체 기준값과 표시

본체 `internalStats`의 `precisionTouchCeiling`, `precisionShortCeiling`, `precisionMediumCeiling`, `precisionLongCeiling`은 정밀도 60점 기준의 거리별 기계 상한이다. 실제 정밀도 점수에 따라 조정한 뒤 0.97 이하로 제한하고, 조준 효율과 유지 효율을 곱해 한 번만 바닐라 정확도에 연결한다. 정상적인 품질·사수 보정은 별도 경로에서 유지한다.

`warmupReferenceCost`와 `recoveryReferenceCost`는 총기별 순정 기준의 정규화 비용이다. 기본 Def 시간은 유지하면서 순정 최종 초 값을 맞추고, 다른 모딩은 그 기준 대비 변화한다. 광학 배율·`aimSpeed`가 쿨다운/RPM을 함께 올리는 지름길은 없다.

모딩창에 원시 세 항목, 견착 지지·인출량·취급 전장을 표시한다. 환산 모드의 웜업은 배율 대신 초 단위다. 정확도 툴팁과 일반 정보창에는 기계 상한 × 조준 효율 × 유지 효율을 표시한다. 편집기·XML 내보내기·Undo는 새 원시/물리/기준 비용 및 개머리판 프로필 전체를 보존한다.

## 검증과 후속 범위

- 실제 모드 DLL 빌드(경고/오류 0)와 패치·상속 적용 데이터 테스트 4,592개: 순정 10종, 프리셋 21종, 원시 스키마, 7개 인출/수축 구성, 양방향 오프셋, 팔레트·피벗·애니메이션 좌표 독립성, 광학 중복, 실제 쿨다운/점사 tick 경로, 내보내기·Undo. 설치된 바닐라 `Verb_LaunchProjectile.WarmupTime`이 장비 배율을 적용하는 지점을 확인해 그 뒤에 거리별 그룹 비용과 하한을 적용한다.
- 머즐 11종의 미장착 대비 제어·회복 이득, 7.62×51 8종과 듀플렉스, 총열의 직접 피해/AP 독립성 및 파편화를 재검증한다.
- 아직 게임 내 전투·UI 수동 조작이나 CE 환경 플레이 테스트는 하지 않았다. CE 출력은 기계 spread와 유지 sway를 분리하고 바닐라 정확도를 중복 적용하지 않지만 CE 자체 전투 판정은 별도 확인 대상이다.
- 접이식 상태 전환 조작/발사 허용 판정, 사수 체격별 최적 인출 길이, LPVO 배율 전환 및 사통장치의 전원/비용은 후속 구현이다. 현재 신축식·고정식 구현과 혼동하지 않는다.

재현 명령:

```powershell
dotnet run --project Source/Tests/ModularWeaponBalance/ModularWeaponBalance.Tests.csproj --no-restore -- .
dotnet run --project Source/Tests/ModularWeaponBalance/ModularWeaponBalance.Tests.csproj --no-restore -- . --snapshot
dotnet run --project Source/Tests/ModularWeaponBalance/ModularWeaponBalance.Tests.csproj --no-restore -- . --stock-snapshot
```
