# Standalone Helod 성능·XML 리팩토링 작업 지침

## 목적

HAR 제거 이후의 Helod 독립 종족 구현에서 런타임 비용과 XML 유지보수 비용을 줄인다.

이번 작업의 우선순위는 다음과 같다.

1. 렌더링·pawn 생성 핫패스의 반복 조회와 불필요한 할당 제거
2. C#과 XML에 중복된 종족 정책을 하나의 데이터 소스로 통합
3. `GeneralRace.xml`과 호환 모듈 패치의 구조 개선
4. 기존 Helod 동작과 CE/Facial 호환성을 유지

XML 파일을 나누는 것 자체는 전투 중 FPS를 개선하지 않는다. XML 개선은 주로 시작 시 로딩·패치 구조와 유지보수성을 위한 작업이며, 성능 개선은 C# 런타임 캐싱과 렌더 경로 최적화를 통해 달성한다.

## 작업 범위

주요 대상 파일:

- `Source/Helodrace/Helod/HelodRace.cs`
- `Source/Helodrace/Helod/HelodPawnGeneration.cs`
- `Source/Helodrace/Helod/HelodRendering.cs`
- `Source/Helodrace/Helod/HelodEvents.cs`
- `Source/Helodrace/Helod/CoveredEarsApparel.cs`
- `Defs/Helod/Race/GeneralRace.xml`
- `Defs/Helod/Appearance/PawnRenderTree.xml`
- `../Helodrace-CombatExtended/Patches/CombatExtended_HelodRaceAndArmor.xml`
- `../Helodrace-Facial/Patches/Helodrace/FacialAnimationComps_Helod.xml`

## 변경 금지 사항

- HAR 의존성을 다시 추가하지 않는다.
- 기존 세이브 마이그레이션 기능을 이번 작업에 임의로 추가하지 않는다.
- `M1918A2 HAR`처럼 이름만 HAR인 무기 Def를 변경하지 않는다.
- `Helodrace-Main` 외부 모듈의 무기·방어구 수치나 게임플레이를 임의로 변경하지 않는다.
- 현재 작업 폴더의 관련 없는 변경을 되돌리거나 덮어쓰지 않는다.
- 측정 없이 atlas/cache 구조를 바꾸거나 FPS 개선을 주장하지 않는다.
- XML 중복 제거 과정에서 DefName, 텍스처 경로, 확률, 목록 순서를 임의로 바꾸지 않는다.

## 0단계: 기준선 확보

- [ ] 작업 전 `git status --short`를 기록한다.
- [ ] 기존 변경 파일은 보존하고, 이번 작업의 변경 파일을 별도로 확인한다.
- [ ] 다음 정적 검증을 먼저 실행한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-StandaloneRace.ps1 -PatchSmokeTest
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-RaceCompatibility.ps1
```

- [ ] 현재 `Assemblies/Helodrace.dll`의 AlienRace 참조가 없는지 확인한다.
- [ ] 가능하면 Unity Profiler에서 Helod pawn 10~20마리 화면을 기준으로 다음 항목을 기록한다.
  - `PawnRenderNodeWorker.OffsetFor`
  - `PawnRenderNodeWorker.CanDrawNow`
  - `PawnRenderNodeWorker_Apparel_Body.LayerFor`
  - `PawnGenerator.GeneratePawn`
  - GC Alloc 및 프레임 시간

기준선 검증이 실패하면 리팩토링을 시작하지 말고 실패 원인을 먼저 기록한다.

## 1단계: 런타임 설정 캐시

### 목표

렌더링과 pawn 생성 중 반복되는 `DefDatabase` 조회, `GetModExtension`, 문자열 비교, `List.Contains`를 초기화 시점의 캐시 조회로 바꾼다.

### 구현 방향

`HelodRace`에 다음 캐시를 둔다.

- `ThingDef RaceDef`
- `HelodRaceExtension Settings`
- `HashSet<HeadTypeDef> HeadTypes`
- `HashSet<ThingDef> AllowedApparel`
- `HashSet<ThingDef> ExclusiveApparel`
- `HashSet<ThingDef> EarCoveringApparel`
- `HashSet<GeneDef> ForbiddenGenes`
- `HashSet<EndogeneCategory> ForbiddenCategories`
- `List<HairDef> HelodHairs`
- `List<XenotypeDef> HelodXenotypes`
- `List<XenotypeDef> NonHelodXenotypes`
- `float DrawScale`

`Initialize()`에서 한 번만 구성하고, 다음 호출은 캐시만 사용한다.

- `IsHelod(Pawn)`는 `pawn?.def == RaceDef` 참조 비교를 사용한다.
- `ApparelPath()`는 캐시된 race Def와 `Dictionary<ThingDef, string>`을 사용한다.
- `CanWear()`는 HashSet을 사용한다.
- `RandomHairFor()`는 모든 `HairDef`를 다시 검색하지 않는다.
- factionless Xenotype 처리에서 전체 Xenotype 목록을 매번 LINQ로 필터링하지 않는다.

### 주의

- `Initialize()`가 호출되기 전에도 패치가 안전하게 동작하도록 null 방어를 유지한다.
- 초기화 시점 이후 다른 모드가 Def를 추가하는 구조를 전제로 하지 않는다. 그런 모드가 있다면 캐시 재구축 지점을 별도로 설계한다.
- 캐시 도입 후 기존 확률·목록·선택 결과가 바뀌지 않아야 한다.

## 2단계: 핫패스 할당 및 순회 최소화

### 대상

- `PawnRenderNodeWorker_HelodAppendage.CanDrawNow()`
- `PawnRenderNodeWorker_HelodAppendage.ScaleFor()`
- `Patch_HelodMeshScale`
- `Patch_HelodApparelPath`
- `PatchHelodShellApparelLayer`
- `PatchHelodUnspecifiedUtilityOffset`

### 작업

- [ ] 렌더링 메서드에서 `Settings` 프로퍼티를 직접 반복 호출하지 않는다.
- [ ] 단순 존재 확인에 사용되는 LINQ `Any`, `Where`, `ToList`를 필요한 곳에서 일반 루프로 교체한다.
- [ ] 귀·꼬리 존재 확인에 필요한 BodyPart 기준을 초기화하거나 pawn별로 안전하게 캐시한다.
- [ ] 귀 덮개 의류 확인은 캐시된 HashSet으로 처리한다.
- [ ] `FindNode<PawnRenderNode_Hair>()`의 재귀 탐색이 실제로 병목인지 Profiler로 확인한다.
- [ ] 병목일 때만 `PawnRenderTree` 또는 pawn별 hair node 캐시를 추가한다.
- [ ] 캐시가 pawn 삭제·렌더 트리 재생성 후 오래된 참조를 유지하지 않는지 확인한다.

렌더링 패치에서는 기능 변경보다 할당 제거와 빠른 early return을 우선한다. 성능을 위해 귀·꼬리 손실, 침대 상태, 부패 상태, 머리 반전 처리를 생략하면 안 된다.

## 3단계: 종족 정책 데이터 단일화

현재 귀를 가리는 의류 목록은 `CoveredEarsApparel.cs`에 하드코딩되어 있고, 허용 의류·그래픽 경로는 `GeneralRace.xml`에 있다. 같은 의류를 추가할 때 C#과 XML을 동시에 수정해야 하는 문제를 제거한다.

### 권장 변경

`HelodRaceExtension`에 다음 필드를 추가한다.

```csharp
public List<ThingDef> earCoveringApparel = new List<ThingDef>();
```

XML의 `HelodRaceExtension` 안에 다음을 둔다.

```xml
<earCoveringApparel>
  <li>HD_Apparel_CarvalyHat</li>
  <li>HD_Apparel_FASTMT</li>
  <li>Apparel_AdvancedHelmet</li>
</earCoveringApparel>
```

- [ ] `CoveredEarsApparel.cs`의 고정 DefName 목록을 제거한다.
- [ ] 초기화 시 `HashSet<ThingDef>`을 만든다.
- [ ] 기존 귀 덮개 목록과 XML 목록이 정확히 같은지 검증 스크립트에 추가한다.
- [ ] 새 목록이 비어 있을 때 귀 렌더링과 Hediff 동기화가 안전하게 동작하는지 확인한다.

같은 원칙으로 향후 tail DefName, 머리 label처럼 정책에 가까운 값도 필요할 때만 설정 데이터로 이동한다. 모든 문자열을 XML로 옮기는 것은 목표가 아니다.

## 4단계: XML 구조 리팩토링

### 권장 파일 구조

```text
Defs/Helod/Race/HelodRace.xml
Defs/Helod/Race/HelodRaceSettings.xml
Defs/Helod/Appearance/PawnRenderTree.xml
Defs/Helod/Appearance/Heads.xml
Defs/Helod/Appearance/Hair.xml
Defs/Helod/Restrictions/ApparelPolicy.xml
Defs/Helod/Restrictions/GeneticPolicy.xml
```

가장 안전한 방법은 `HelodRaceSettingsDef : Def`를 새로 만들고, 목록과 숫자 설정을 해당 Def에 모으는 것이다. 종족 본체의 `HelodRaceExtension`은 설정 Def를 참조한다.

예시:

```xml
<HelodRaceSettingsDef>
  <defName>HD_HelodRaceSettings</defName>
  <drawScale>0.8</drawScale>
  <maleProbability>0.0000001</maleProbability>
  <refugeeChance>0.15</refugeeChance>
  <slaveChance>0.15</slaveChance>
  <wandererChance>0.15</wandererChance>
  <headTypes>
    <li>HD_HelodHead010101</li>
    <li>HD_HelodHead010102</li>
  </headTypes>
</HelodRaceSettingsDef>
```

종족 Def에는 종족의 정체성과 vanilla race 데이터만 남긴다.

```xml
<ThingDef ParentName="HD_PawnBase">
  <defName>Helod</defName>
  <label>Helod</label>
  <modExtensions>
    <li Class="Helodrace.HelodRaceExtension">
      <settingsDef>HD_HelodRaceSettings</settingsDef>
    </li>
  </modExtensions>
</ThingDef>
```

### XML 정리 규칙

- [ ] `GeneralRace.xml`의 빈 `<inspectorTabs />`는 필요성을 확인한 후 제거한다.
- [ ] `HD_PawnBase`와 `Helod`에 중복된 `<race>` 필드는 실제 상속 결과를 확인한 뒤 하나만 유지한다.
- [ ] 동일한 의류 그래픽 경로·허용 목록·귀 덮개 목록을 서로 다른 파일에 복제하지 않는다.
- [ ] `MayRequire`와 `MayRequireAnyOf` 조건은 유지한다.
- [ ] XML 파일 분리 후 DefName과 ParentName 충돌이 없는지 검사한다.
- [ ] 일반 Def와 PatchOperation을 혼합해 같은 필드를 두 번 덮어쓰지 않는다.

XML 리팩토링은 한 번에 전체 Def를 재작성하지 말고, 먼저 설정 Def를 도입한 뒤 목록을 이동하고 마지막에 본체 파일을 줄인다.

## 5단계: CE/Facial 패치 중복 제거

### Facial Animation

현재 `FacialAnimationComps_Helod.xml`은 `comps`가 있을 때와 없을 때 같은 컴포넌트 블록을 중복 작성한다.

- [ ] 먼저 `comps`가 없으면 빈 `<comps />`를 추가한다.
- [ ] 그 다음 단일 `PatchOperationAdd`로 Facial 컴포넌트를 추가한다.
- [ ] 패치가 두 번 적용되어도 중복되지 않도록 조건 또는 중복 검사 정책을 둔다.

### Combat Extended

- [ ] CE race extension, CE comps, stats, tools의 적용 순서를 유지한다.
- [ ] `ThingDef[defName="Helod"]` 선택자를 유지한다.
- [ ] CE가 먼저 적용되거나 Facial이 먼저 적용되는 두 순서를 모두 검증한다.
- [ ] CE가 실제 게임에서 기즈모를 노출하는지 확인한다.

## 6단계: 검증

정적 검증:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-StandaloneRace.ps1 -PatchSmokeTest
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-RaceCompatibility.ps1
```

빌드:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore -v minimal
```

빌드 산출물이 다른 프로세스에 의해 잠겨 있으면 파일을 강제로 삭제하지 말고 잠금 프로세스를 확인한다. 기존 `obj`와 `Assemblies`를 파괴적으로 정리하지 않는다.

추가해야 할 회귀 검증:

- [ ] 캐시 초기화 후 Helod Def와 설정 Def가 존재한다.
- [ ] 허용·전용·귀 덮개 의류 목록 수가 리팩토링 전과 같다.
- [ ] 머리 목록, 헤어 목록, Xenotype 목록이 같다.
- [ ] 성인·아동·아기 body type이 같다.
- [ ] 네 방향, 초상화, 침대, 부위 손실, 부패 상태에서 외형이 유지된다.
- [ ] 새 pawn 생성, 난민·노예·방랑자, 출산, 성장 특성, 수술을 확인한다.
- [ ] CE와 Facial Animation을 각각 단독으로 켜고, 둘 다 켠 상태에서도 확인한다.
- [ ] 저장 후 재로드한다.

## 완료 기준

- 활성 파일과 컴파일된 DLL에 `AlienRace` 참조가 없다.
- 기존 standalone/compatibility 검증이 모두 통과한다.
- 리팩토링 전후 핵심 Def 목록과 확률이 동일하다.
- 렌더 핫패스에서 반복되는 설정 조회와 불필요한 LINQ 할당이 제거된다.
- 귀 덮개 정책이 XML 한 곳에서 관리된다.
- CE/Facial 패치가 적용 순서와 무관하게 중복 없이 동작한다.
- Unity Profiler 측정값을 근거로 성능 변화가 기록된다.
- 실제 게임 부팅 및 인게임 시나리오 검증 결과가 문서에 기록된다.

## 권장 커밋 분리

작업은 다음 커밋 단위로 나눈다.

1. `perf: cache standalone Helod runtime settings`
2. `perf: reduce Helod render hot-path allocations`
3. `refactor: move covered apparel policy into Def data`
4. `refactor: split Helod race settings from core ThingDef`
5. `refactor: deduplicate compatibility patch operations`
6. `test: extend standalone and compatibility regression checks`

각 커밋마다 기존 검증 스크립트를 실행하고, 동작 변경이 포함되면 그 이유를 커밋 메시지나 문서에 명시한다.
