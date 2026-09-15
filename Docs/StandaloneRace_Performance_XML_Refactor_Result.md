# Standalone 종족 성능·XML 리팩토링 결과

## 상태

코드/XML 변경과 자동 검증은 완료했다. **Unity Profiler 실측 및 실제 게임 시나리오 검증은 미실시**이므로 지침의 전체 완료 기준을 충족했다고 보지는 않는다. FPS·프레임 시간·GC Alloc 개선 수치는 아직 없다.

## 기준선과 기존 작업 보존

작업 시작 시 `git status --short`를 확인·기록했고, 기존 standalone/compatibility 검증이 모두 통과한 뒤 리팩토링을 시작했다. 기존 DLL에 AlienRace 참조가 없음을 확인했다.

시작부터 변경되어 있던 Hair.xml, Hair1~4 아트, 모듈러 무기 XML 3개, ModularWeaponIconCache.cs 및 관련 문서/obj 작업은 되돌리지 않았다. 앞선 귀/머리 수정과 기존 검증 스크립트를 유지하며 확장했다. 최종 DLL은 현재 소스 전체를 빌드한 결과다. 기존 미커밋 작업과 겹치므로 이번에 자동으로 일괄 커밋하지 않았다.

## 런타임 변경

- `HelodRace.RebuildRuntimeCaches()`가 종족 Def, 설정, 정착민 PawnKind, 머리·헤어·유전자·종족형·의류 정책을 구축한다. `Initialize()`에서 호출하며, 초기화 전에는 안전하게 원래 동작으로 빠진다.
- `IsHelod`는 캐시된 Def의 참조 비교다. 렌더링/생성 중 반복 `DefDatabase`·`GetModExtension` 조회를 제거했다.
- 헤어와 양쪽 종족형 후보는 기존 순서로 보존한 목록에서 선택한다. 검색용 HashSet을 무작위 선택 목록으로 사용하지 않는다.
- 귀/꼬리의 `BodyPartRecord` 후보만 Def 로딩 후 캐시한다. 렌더 시에는 `PartIsMissing`을 호출해 현재 상태를 확인한다. 침대·부패·머리 반전·모자 조건과 오프셋 값은 유지했다.
- 귀 덮개 의류는 Def HashSet 조회로 통일했다. 렌더링과 Hediff 동기화가 같은 XML 정책을 사용한다.
- 종족형 Dictionary 필터의 매 호출 `Keys.ToList()` 대신 스레드별 임시 목록을 재사용하고 참조를 비운다.
- 의류 레이어/오프셋 패치도 종족과 유틸리티 의류를 Def 캐시로 판별한다. Hair node 재귀 탐색은 Profiler 근거가 없으므로 캐시하지 않았다.
- Pawn이나 렌더 노드를 장기 보유하는 캐시는 새로 만들지 않았다. Def 로딩 이후 변경을 지원해야 할 때만 명시적으로 `RebuildRuntimeCaches()`를 호출한다. 수술/식재료 초기화는 캐시 재구축과 분리했다.

## XML 변경

- `GeneralRace.xml`을 `HelodRace.xml`과 `HelodRaceSettings.xml`로 분리했다.
- 종족의 `HelodRaceExtension`은 `HD_HelodRaceSettings`를 참조한다. 정책 목록은 해당 설정 Def 한 곳에만 둔다. 별도 Restrictions 파일로 같은 목록을 복제하지 않았다.
- 이전 귀 덮개 14개를 그대로 XML로 옮겼다. `Source/Tests/StandaloneRaceEarPolicyBaseline.json`은 검증 전용 기준선이며 모드가 읽는 정책이 아니다.
- 머리 13종, 의류 그래픽 39개, 전용 의류 39개, 추가 허용 57개(합집합 96개), 금지 유전자 2개, 분류 7개, 종족형 5개를 유지했다. 목록 순서와 DLC 조건도 검증한다.
- 배율 0.8, 남성 확률 0.0000001, 난민/노예/방랑자 확률 0.15, 사교 싸움 피해 상한 6을 유지했다.
- 빈 inspectorTabs는 바닐라 상속 구현상 부모 목록을 비우는 요소가 아니므로 제거했다. `body`와 `canBecomeShambler`의 동일한 중복 값은 HD_PawnBase 쪽에만 남겼다.
- 렌더 트리/텍스처/머리 위치/신체 타입 선택, atlas 및 렌더 캐시 구조는 변경하지 않았다. XML 파일 분리 자체를 전투 성능 개선으로 간주하지 않는다.

## 호환 모듈

- CE/Facial의 `ThingDef[defName="Helod"]` 선택자는 유지했다.
- Facial: comps가 없으면 생성하고, 컴포넌트 데이터는 각각 한 번만 정의한다. 일부 컴포넌트가 이미 들어온 경우도 안전하도록 **단일 일괄 Add 대신 컴포넌트별 조건부 Add**를 사용했다. 재적용 시 중복되지 않는다.
- CE: 확장 → 컴포넌트 → 스탯 → 근접 도구 순서를 유지했다. 추가 작업은 존재 확인 후 실행하며, 이동속도 보정 제거도 조건부로 실행해 재적용 시 실패하지 않는다. 방어구/무기 수치는 바꾸지 않았다.
- `Source/Tests/CompatibilityPatchBaseline.json`의 변경 전 패치와 비교하여, 최초 적용 후 전체 Def XML이 동일함을 확인했다(주석 제외). 따라서 초기화 결과를 바꾸는 중복 제거가 아니다.

## 자동 검증 결과

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore -v minimal
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-StandaloneRace.ps1 -PatchSmokeTest
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-RaceCompatibility.ps1
```

- 일반 빌드 성공: 경고 0, 오류 0. 기존 obj나 Assemblies를 삭제하지 않았다.
- XML 271개 파싱, DLL의 HAR 참조 없음, 기존 31개 Patch_Helod 패치 설치 검증 통과.
- 정책 숫자/목록/순서/DLC 조건/의류 경로 보존 검사 통과.
- 캐시 구축·재구축, 초기화 전 호출, 빈 귀 덮개 정책, 헤어 후보 순서, 기존 신생아 상속/특성/유전자/머리 오프셋/귀 식별 회귀 검사 통과.
- 실제 게임 DLL의 PatchOperation으로 CE 단독, Facial 단독, CE→Facial, Facial→CE 및 각각 재적용 검사 통과. 초기 결과는 기준선과 같으며 재적용 후에도 XML이 변하지 않는다.
- 네 동반 모듈(CE, Facial, blancasdrugs, LobosArsenal)의 활성 HAR 참조 없음 검사 통과.

## 남은 실측/실게임 검사

Profiler를 연결한 게임 실행 환경에서 결과를 수집하지 못했다. 아래 항목은 **미실시**이며 자동 테스트로 대체됐다고 주장하지 않는다.

1. 동일 모드 구성/해상도/줌/게임 속도에서 헬로드 10~20명 장면을 전후 비교한다. 워밍업 후 OffsetFor, CanDrawNow, Apparel LayerFor, GeneratePawn의 호출 비용, GC Alloc, 프레임 시간을 기록한다.
2. 성인/아동/아기, 네 방향·초상화·침대·부패·귀/꼬리 손실·모자 착탈을 확인한다.
3. 새 pawn/난민/노예/방랑자, 출산, 성장 특성, 수술을 확인한다.
4. CE 단독·Facial 단독·둘 다 활성인 경우를 나누어 CE 기즈모와 얼굴 렌더링을 확인한다.
5. 저장 후 재로드한다. 이번 작업에서 세이브 변환이나 설치된 모드 설정 변경은 하지 않았다.

게임 폴더에 적용할 때 DLL과 두 새 XML을 함께 반영해야 한다. **이전 `Defs/Helod/Race/GeneralRace.xml`이 배포 폴더에 남지 않도록 동기화**한다(동일 Helod Def 중복 방지). 두 호환 모듈 XML도 별도로 반영한다. 이 작업에서는 설치된 모드 폴더를 덮어쓰지 않았다.
