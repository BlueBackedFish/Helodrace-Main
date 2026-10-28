# 중기관총 사수 명중률

대상은 본체 M1917 중기관총, M2HB 중기관총, CROWS II의 M2HB다. CROWS는 연결 컴퓨터를 실제 조작하는 폰을 사수로 사용하며 직접 조작형은 현재 조작 중인 `CompMannable.ManningPawn`을 사용한다.

사수 계수는 `Clamp01(x + 기존 사수 명중률 × x)`이고, x는 요청한 **0.5**다. x 자체도 0~1 범위로 제한한다. 예를 들어 기존 사수 계수가 80%면 90%, 0%면 50%, 100%면 100%다. 103% 같은 입력도 최종 사수 계수는 100%를 넘지 않는다.

여기서 기존 사수 명중률은 바닐라 `ShotReport.HitFactorFromShooter(사수, 거리)`의 결과다. 실제 폰의 사격 능력·상태·장비 및 거리 감쇠를 한 번 계산한다. 포탑의 추상 사수 대신 실제 사수의 보정된 값을 사용한다. 무기 자체의 거리별 정확도, 엄폐, 날씨, 자세, 표적 크기는 나머지 바닐라 `ShotReport` 항목으로 유지한다. 이 계수는 최종 명중 확률 전체와 다르다.

이전 코드는 스킬을 별도로 곱한 보정값과 기존 포탑 계수 중 큰 값을 보고서에 직접 넣었다. 이 때문에 거리 감쇠를 덮어쓰고 사수 계수가 100%를 넘을 수 있었다. 해당 전역 유인 포탑 패치를 제거하고 세 중기관총의 `HeavyMachineGunAccuracyExtension` 설정에만 새 공식을 적용한다. 다른 포탑·휴대 총기에는 이 보정식을 적용하지 않는다.

각 건물 Def의 `gunnerBaseAccuracy`로 x를 지정한다. 표시와 실제 발사가 공유하는 `HitFactorFromShooter`를 패치하므로 툴팁만 고치는 방식이 아니다. 다른 `ShotReport` 항목을 읽거나 덮어쓰지 않는다.

```powershell
dotnet run --project Source/Tests/Crows/Crows.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Tests/Crows/Verify-GunnerAccuracy.ps1
```

제작 코드의 수식·범위·거리별 차이·사수 부재·대상 범위·CROWS 원격 사수·강제 표적 권한 및 기존 사격 차단을 검증한다. 별도로 실제 게임 어셈블리에서 Harmony 등록과 기본 호출, 실제 `Mathf` 수식, 강제 표적 override와 XML 설정을 확인한다. 실제 게임 맵에서의 명중 분포 테스트는 하지 않았다.
