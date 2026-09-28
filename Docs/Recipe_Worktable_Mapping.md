# 조합법-작업대 배치표

이 문서는 P1 M-06/M-07의 기준표다. 수치 밸런스가 아니라 **어떤 공정을 어느 작업대에서 수행하는지**만 고정한다. `Source/Verify-RecipePlacement.ps1`가 아래 핵심 매핑, 파생 조합법의 연구 조건, 중복·미등록 작업대 참조를 검사한다.

## 핵심 공정

| 시대 | 공정 | 제품군/조합법 | 작업대 |
|---|---|---|---|
| 서부 | 수동 선삭 | 저급 총열, 산탄총 총열 | `HD_TreadleLathe` |
| 서부 | 범용 선삭 | 축, 범용 기계 부품(기본·5개·10개) | `HD_TreadleLathe`, `HD_LineShaftTurretLathe` |
| 서부 | 동력 선삭·밀링 | 서부 총열 | `HD_LineShaftTurretLathe`, `HD_LineShaftMillingMachine` |
| 서부 | 밀링 | 범용 기계 부품(기본·5개·10개) | `HD_LineShaftMillingMachine` |
| 서부 | 유압 프레스 | 프레스 부품(기본·5개·10개) | `HD_LineShaftHydraulicPress` |
| 서부 | 압연 | 균일 압연 강판 | `HD_LineShaftRollingMachine` |
| 대전기 | 프레스 성형 | M1 철모 | `HD_LineShaftHydraulicPress` |
| 서부~현대 | 최종 조립·탄약 조립 | 총기, 전기 부품, 포탄·로켓 등 | `HD_BasicWorkbench` |
| 서부 | 원유 분별·정제 | 연료유, 등유, 나프타, 화학연료 | `HD_DistillationTower`, `HD_BatchStill` |
| 대전기 | 제강 | 용선, 탄소강 | `HD_BlastFurnace`, `HD_Converter` |
| 현대 | 고압·화학 합성 | 질산암모늄, TNT, RDX, C4 | `HD_HaberBoschHighPressureReactor`, `HD_StirredTankReactor` |

## 배치 원칙

- 기본·대량 조합법은 같은 제품군이면 `recipeUsers`와 연구 선행 조건이 같아야 한다.
- 성형 부품과 M1 철모는 프레스, 압연 강판은 압연기, 밀링 부품은 밀링 머신에 둔다.
- 두 공정에서 실제로 만들 수 있는 품목만 복수 작업대를 허용한다. 현재 허용 대상은 범용 선삭 부품, 축, 서부 총열이다.
- `HD_BasicWorkbench`는 최종 조립 및 저기술 대체 공정에 사용한다. 전용 기계 공정을 가진 기본·대량 파생 조합법을 이 작업대에 따로 흩어 놓지 않는다.

## 검증

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Source\Verify-RecipePlacement.ps1
```

검사는 다음을 실패로 처리한다.

- 중복 `ThingDef` 또는 `RecipeDef`
- `recipeUsers`가 없는 고립된 `RecipeDef`
- 존재하지 않거나 작업대가 아닌 헤로드 `recipeUsers`
- 같은 제품군의 기본·5개·10개 조합법 간 작업대 또는 연구 조건 불일치
- 위 표의 핵심 공정 매핑 변경
- M1 철모가 유압 프레스가 아닌 곳에 배치됨
