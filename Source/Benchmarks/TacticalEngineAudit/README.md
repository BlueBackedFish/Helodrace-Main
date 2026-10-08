# 전술 엔진 대조 실행 — R1/R2

같은 DLL/콘텐츠에서 `vanilla`, `legacy`, `new`를 선택한다. 기본 플레이는 `legacy`이고 선택은 프로세스 시작 시 고정한다. 현재 `new`는 R2의 분대 지휘·지속 작업·연결 스택·해머 돌파·필수 개구부 통과를 실행한다. R1의 `vanilla-fallback` 기록은 새 AI 합격 증거가 아니다.

## 실행

게임 DLL과 CLI를 먼저 빌드한다. 실행기는 DLL/PDB 및 신규 JobDefs를 설치된 모드에 복사하고 DLL 해시를 확인한다. 기존 RimWorld 프로세스가 있으면 시작하지 않는다. CreatorTemp의 새 저장·설정·로그 폴더에서 실행하고 자체 종료한다.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore
dotnet build Source/Tools/AgentProfiler/AgentProfiler.csproj --no-restore
& Source/Benchmarks/TacticalEngineAudit/Run-EngineAudit.ps1 -Engine new -Workload sapper-wall -Population 12 -WarmupTicks 0 -SampleTicks 3000
& Source/Benchmarks/TacticalEngineAudit/Run-R2Functional.ps1
```

반복 시험은 매번 새 프로세스로 실행하며 엔진 순서를 회차마다 회전한다. 기존 결과 폴더를 덮어쓰지 않는다. R2는 초기 접근·스택·돌파 비용을 포함하려고 warmup을 0으로 둔다.

```powershell
$targets = 'Verse.AI.Pawn_JobTracker::StartJob;Verse.PathFinder::CreateRequest;Helodrace.Tactics.MapComponent_TacticalCommands::Advance;Helodrace.Tactics.TacticalLocalPlanner::Find;Helodrace.Tactics.MapComponent_TacticalCommands::Issue;Helodrace.Tactics.MapComponent_TacticalCommands::ReuseOpening'
& Source/Benchmarks/TacticalEngineAudit/Run-EngineMatrix.ps1 -Engines @('vanilla','new') -Repeats 3 -Population 50 -Workloads @('open-approach','sapper-wall') -WarmupTicks 0 -SampleTicks 6000 -Seed hd-r2-20261007 -ProfileTargets $targets
```

`-NoMethodProfile`은 창 시작/끝에서만 OS 메인/프로세스 CPU를 읽는 비계측 대조다. 메서드별·틱별 CPU와 Core 배치는 없다. `-Population 0`은 유휴 대조다. `-ProfileTargets '실제선언타입::메서드;...'`는 해당 구체적 메서드의 오버로드들을 추가한다. Map/GameComponent Tick/Update와 전체 호출기는 기본 대상이다.

## 고정 조건과 격리

- 실제 조직 생성기로 LOW 12인 대전기 소총 분대, HIGH 13인/3화력조 분대를 만든다. 요청 50/200은 LOW 실제 60/204명이다. 생산 편제·장비 XML은 바꾸지 않는다.
- fixture v9는 **격리 시험의 동기 분대 생성 중에만** 관계·무작위 배경·무작위 특성을 제외하고 이름을 고정한다. 나이는 인덱스별 18~35세, 성별은 Female, 계통은 실제 Helod 5종을 DefName 순서로 순환한다. 실제 조직 생성기·무장/장비와 기술 생성은 유지한다. 지문이 달랐던 v7 및 관계만 제한한 v8은 최종 대조에서 제외한다. 일반 게임 생성에는 이 제약이 적용되지 않는다. 원본 지문을 수정하거나 다른 폰을 같은 조건으로 강제 묶지 않는다.
- 폰 지문은 종류·공병 자격·나이·성별·배경·특성·유전자·기술/열정·질환·장비·모듈 무장/방탄복 파츠를 포함한다. 지도 전체 지형·건물·지붕 해시도 기록한다.
- `open-approach`는 기존 구멍이 있는 외벽/일반 Lord, `sapper-wall`은 닫힌 외벽/sapper Lord, `sapper-door`는 일반 문이 있는 외벽/sapper Lord다. 성능 그룹을 섞지 않는다.
- 공병 시험은 첫 채굴 가능 폰의 기술을 20으로 고정하고 그 폰카인드에 바닐라 공병 자격을 격리 프로세스 안에서 부여한다. 모든 엔진에 같은 조건이다.
- 목표는 이름 있는 침대의 비무장·정지 소유자다. **모든 엔진의 공격자도 시험 동안 피해를 막아 고정 인원을 유지한다.** 이후 바닐라 전투에서 우발적인 수류탄 사망으로 인원이 달라지는 것을 방지한다. 따라서 이 결과는 피해·회피·실제 방 정리 성능의 증거가 아니다. `casualty` 사례는 직접 `Kill`로 공병 사망을 강제한다.
- Vanilla/New는 구형 컴포넌트 11개와 구형 패치 클래스 25개를 설치하지 않는다. New에만 신규 컴포넌트 2개와 습격 등록 패치 1개를 설치한다. 시작/종료 목록과 구형 콜백 0회 여부를 교차 확인한다.

## 기능 사례

`Run-R2Functional.ps1 -Cases @('field','door','narrow','rocks','interrupt','casualty','crowd200')`는 실제 게임에서 독립 사례를 실행한다. 야외 일반 이동/명시 목표, 문 고장, 16칸 방, 벽 앞 장애물, 실내 통과 후 작업 중단, HIGH 공병 사망, LOW 204명 진입을 검사한다. `checks.json`은 사례마다 저장한다.

`newFunctionalComplete`는 모든 분대 완료·생존 멤버 실내 자리 도착·연결 대형·실제 구조에 맞는 계획을 요구한다. 실제 개구부 통과/후미 통과를 별도 기록한다. 직접 야외 이동은 개구부 통과를 요구하지 않는다. 문은 `newDoorFaults=1`, 중단은 활성 새 임무 동안 외부 복귀 없음, 공병 사망은 12명 생존 완료를 추가 확인한다. `moved`나 stock AI의 뒤늦은 실내 접근만으로 성공하지 않는다.

## R3 관측·지원 약식 시험

`-Case contact -Population 12 -SampleTicks 2400 -WarmupTicks 0`은 16칸 방을 만든 뒤 관측 단계에 고정 적을 개구부 시야 안에 배치한다. `narrow`는 기존 위치를 유지하므로 시야 밖 적으로 절약 규칙을 해제하지 않는 대조다. 일반 큰 방의 관측·지원은 `-Case normal`로 확인한다. 동적 적 추격이나 다방 정리 시험은 아니다.

`newObservations/newObservationContacts/newSupportThrows/newSupportReturns/newSupportWaits/newUnsafeEntries`를 남긴다. `newUnsafeEntries=0`과 실제 투척·복귀 횟수 및 전원 내부 고유 자리 완료를 함께 확인한다. `newLastJobFailure`는 마지막 실패/중단 종류이며 exceptions와 구분한다. New 컴포넌트는 2개, R3 현재 훅은 습격 등록 및 준비/관측 표현·사격의 6개다. Vanilla/Legacy에는 New 훅을 설치하지 않는다. 단계 진행 기록은 [R3 문서](../../../Docs/전술/R3%20CQB%20구현과%20검증.md)에 있다.

## CPU 비교

R3 장비 회귀: `-Case recovery`는 유일한 해머 공병을 돌파 작업 발행 뒤 죽이고 시체의 장비 회수를 확인한다. `-Case cutter-recovery`는 해머를 제거하고 절단기 하나를 지급한 뒤 같은 작업 승계/회수를 확인한다. `-Case cutter`는 사망 이벤트 없이 절단기를 사용한다. fixture 장비 변경은 모든 엔진에서 폰 지문 생성 전에 적용한다. 사망 이벤트는 New 전용 기능 검사이며 바닐라 CPU 대조로 사용하지 않는다. `newToolRecoveriesStarted/newToolRecoveriesCompleted/newCutterJobsStarted`와 생존자 전원 진입/기능 완료를 함께 확인한다.

```powershell
$cli = 'Source/Tools/AgentProfiler/bin/Debug/net10.0/AgentProfiler.dll'
dotnet $cli engine-compare "$matrix/open-approach/vanilla" "$matrix/open-approach/new"
dotnet $cli engine-compare "$matrix/sapper-wall/vanilla" "$matrix/sapper-wall/new"
```

`engine-compare`는 fixture v5~v13 원본을 읽되 같은 버전·DLL·지도·폰 지문·편제·준비/측정 틱·수집 대상·Core 작업만 묶는다. 시작 단계는 엔진마다 달라도 된다. CPU/틱 누락, 부분 창, 오류, dropped, 격리 실패, 95% 미만 이동은 거부한다. v11 이상은 환경 통제 기록과 비시험 폰 없음도 요구한다.

새 엔진 합격에는 같은 조건의 Vanilla/New 최소 3회씩과 실제 진입 진행, 모든 새 분대의 기능 완료가 필요하다. 실제 전체 틱 CPU와 **틱 밖을 포함한 측정 창 메인 CPU** 중앙값 비율 모두 ≤2여야 한다. 프로세스 CPU도 별도 보존한다. unmatched를 없애기 위해 메타데이터를 다시 쓰지 않는다. v7 이상의 기능 완료 검사를 적용하며 v9는 제어된 생성 조건과 확대된 폰 지문 때문에 이전 버전과 묶지 않는다.

메서드는 Stopwatch 경과 시간과 Core 10,000회 배치 대비 비율이다. OS 실제 CPU는 전체 틱/창에만 기록한다. 부모/자식 inclusive를 더하지 않으며 호출 분포는 마지막 2,048회다. 프로세스−메인 차이는 특정 워커 CPU가 아니다. 명시적인 전술 엔진 격리 감사는 최대 1,800초를 허용하고 일반 수집은 300초 제한을 유지한다.

오류·타임아웃 원본은 보존한다. 실행기 타임아웃은 30분이며 살아 있는 프로세스를 임의 재시작하지 않는다. 최종 기능/CPU 결과는 [R2 보고서](../../../Docs/전술/R2%20최소%20돌파%20진입%20구현과%20검증.md)에 정리한다.


## R3 연속 방 진입 약식 시험

`-Case multiroom -Population 12 -WarmupTicks 0 -SampleTicks 4800`으로 외벽과 내부 문 두 곳을 포함한 세 방을 시험한다. 이름 있는 침대는 두 칸 크기를 고려해 칸막이에서 떨어뜨려 두며, 생성 시 칸막이가 지워지지 않았는지 검사한다. 목표 구역과 세 대표 구역, 중복 없는 진입 이력, 고유 자리·연결 스택, 실제 안전 대기를 검사한다. 완료 뒤 바닐라 AI로 반환하므로 마지막 폰 위치만으로 진입 완료 여부를 판단하지 않는다.

fixture v12는 우발 사건·야생동물을 차단하고 비시험 폰을 기록한다. `environmentControlled=false` 또는 `unexpectedPawns`가 비어 있지 않으면 CPU 대조로 사용하지 않는다. v9~v12 또는 DLL/지도/장비/수집 창이 다른 원본을 같은 조건으로 비교하지 않는다. R3 약식 한 분대 시험은 전체 CPU 2배 게이트가 아니다.

fixture v13의 `-Case unexpected-hole`은 두 번째 구역 관측 중 다른 내부 벽 한 칸을 제거한다. `newUnexpectedOpeningReused`는 해당 개구부를 열린 통로로 재사용한 진입 이력을 요구한다. `-Case inside-goal`은 첫 구역 안에서 분대가 시작하고 그 구역의 이름 있는 침대를 명시 목표로 준다. `newDirectObjectiveCleared`는 첫 진입이 Direct이고 목표 확보 및 첫 구역 안의 자리 배치를 요구한다. 두 사례 모두 세 구역 확보와 전원 완료를 함께 확인하며 바닐라 CPU 대조의 대체물이 아니다.
