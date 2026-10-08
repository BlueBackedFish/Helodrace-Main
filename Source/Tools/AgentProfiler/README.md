# 에이전트용 메서드 프로파일러

Helodrace에 선택적으로 연결되는 개발용 수집기와 .NET 10 CLI다. Dubs/RimDoctor 코드를 복사하거나 해당 모드를 요구하지 않는다. 현재 수집기는 게임 DLL에 포함되고 `-hdMethodProfile=절대경로`가 있을 때만 Harmony 계측을 설치한다. 일반 실행에는 이 계측 패치와 파일 폴링을 설치하지 않는다.

## 빌드와 격리 실행

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore -p:EnableSourceLink=false
dotnet build Source/Tools/AgentProfiler/AgentProfiler.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/RaidMovementAudit/Run-GameAudit.ps1 -Cases '16,17,15' -WarmupTicks 120 -SampleTicks 900 -MethodProfile
```

50명·100명·인원 제거 후 0명 순서로 실행한다. 실행기는 기존 게임이 열려 있으면 중단하며, CreatorTemp의 새 저장·설정 폴더를 사용한다. 저장소 DLL/PDB를 설치된 모드에 배포하므로 먼저 빌드해야 한다. 기존 게임 로그/저장을 재사용하지 않는다. 각 구간의 준비 틱 이후에 수집을 시작하며 `measurements.ndjson`과 `profiles/capture-*.json`을 남긴다.

`-ProfileElapsedOnly`는 OS CPU 조회를 끈다. 기본값 `coarse`는 연결 간선·음성 LOS·연막 등 짧은 내부 메서드를 제외해 계측 비용을 줄인다. 세부 원인을 좁힐 때 `-ProfilePreset detailed`를 사용한다. `-MethodProfile` 없이 같은 실행기를 돌리면 신규 수집기 자체가 비활성인 대조군이다. 기존 개발자 단계 계측은 양쪽 모두 켜져 있다.

추가 메서드는 실행 시 선택한다. 메서드 이름에 해당하는 구체적 선언의 오버로드를 모두 등록하고, 전체 서명은 capabilities에서 확인한다. 지원 어셈블리는 Helodrace와 게임 Assembly-CSharp이며 제네릭/추상 메서드와 수집기 자체는 제외한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/RaidMovementAudit/Run-GameAudit.ps1 -Cases '16,17,15' -MethodProfile -ProfileElapsedOnly -ProfileTargets 'Helodrace.MapComponent_RaidTacticalExecution::ExecuteUnit;Helodrace.MapComponent_RaidTacticalExecution::UpdateStep'
```

일반 개발 플레이에서도 `-hdMethodProfile=C:\...\profiles`로 활성화할 수 있다. 선택 옵션은 `-hdMethodProfilePreset=coarse`와 `-hdMethodProfileTargets=타입::메서드;타입::메서드`다. 이미 실행 중인 게임에 외부 주입하는 방식이 아니며, 실행 전 인자가 필요하다.

## CLI

R1에서는 Helodrace Map/GameComponent의 구체적인 Tick/Update 콜백(감사·수집기 제외), Map PreTick/PostTick/Update, MapComponentUtility/GameComponentUtility 호출기가 기본 대상이다. 지도 분석·조직 유지도 자동 등록된다. schema 4의 `mainThreadWindowCpuMs`·`processWindowCpuMs`는 전체 측정 창의 실제 메인/프로세스 CPU다. 렌더링·기준 배치·다른 시스템도 포함하며 개별 메서드 CPU와 혼동하지 않는다.

`engine-compare VANILLA_ROOT CANDIDATE_ROOT`는 [R1 실행기](../../Benchmarks/TacticalEngineAudit/README.md)의 audit와 capture를 함께 검증한다. 같은 DLL·지도·장비·편제·준비·수집 조건을 요구하며 엔진별 시작 단계 차이는 허용한다. 일반 `compare`/`benchmark-compare`는 엔진을 섞지 않는다. New의 `vanilla-fallback`은 새 전술 AI 합격으로 판정하지 않는다. R2의 구현된 New는 fixture의 모든 분대 완료·실내 자리 도착·실제 구조에 맞는 계획을 요구한다. 같은 조건 3회 이상 대조에서 전체 틱/전체 메인 창 CPU 비율 모두 ≤2일 때만 CPU 게이트를 통과한다. fixture v9의 제어된 생성 조건은 [R2 실행 안내](../../Benchmarks/TacticalEngineAudit/README.md)에 있다.

아래 `$root`는 게임이 생성한 profiles 폴더, `$capture`는 수집 JSON 경로다.

```powershell
$cli = 'Source/Tools/AgentProfiler/bin/Debug/net10.0/AgentProfiler.dll'
dotnet $cli capabilities $root
dotnet $cli search $root RefreshFrames
dotnet $cli start $root 10 cpu
dotnet $cli status $root
dotnet $cli stop $root
dotnet $cli hotspots $capture inclusive 25
dotnet $cli hotspots $capture self 25
dotnet $cli hotspots $capture cpu 5
dotnet $cli hotspots $capture calls 25
dotnet $cli compare $before $after
dotnet $cli engine-compare $vanillaRoot $candidateRoot
```

출력은 JSON이고 오류 시 종료 코드는 1이다. `start`의 `cpu`를 생략하면 경과 시간만 수집한다. 명령은 단일 대기 파일을 사용하고 기존 대기 명령은 덮어쓰지 않는다. 15초 응답 제한이 지나도 명령이 나중에 실행될 수 있으므로 무조건 재전송하지 말고 게임 상태/대기 파일을 확인한다. 상태 파일에는 `latestCapture`가 있다.

CLI 제어 시험은 `Run-ControlSmoke.ps1 -ProfileRoot ...`로 수행한다. 자동 구간 수집과 CLI 수집을 동시에 시작하지 않는다. 감사 실행 중 CLI를 시험하려면 `Run-GameAudit.ps1 -MethodProfile -ProfileManual`을 사용한다. 프로파일러는 한 번에 한 세션만 허용한다.

## 값의 의미와 제한

성능 기준은 Core `Verse.GenRadial.NumCellsInRadius(float)`의 고정 배치다. 반지름 `8+(i%16)`으로 10,000회 호출한 비용을 100% 단위로 삼는다. 준비 호출 후 수집 시작/종료 및 수집 중 약 1초마다 계측하며, 모든 샘플과 중앙값을 보존한다. 이 구간은 대상 메서드 계측에서 제외한다. 기준 메서드 자체의 추가 계측은 허용하지 않는다. 지도/폰/난수를 읽지 않으며 Core 메서드의 기존 Harmony 패치 소유자도 기록한다.

`referencePercentPerCall = (대상 누적 경과 시간 / 호출 수) / 기준 배치 중앙값 × 100`, `referencePercentPerTick = (대상 누적 경과 시간 / 게임 틱 수) / 기준 배치 중앙값 × 100`이다. 100%는 Core 10,000회 배치 한 번의 비용이며 CPU 점유율이 아니다. 두 값은 원본 수집 JSON과 CLI에 기록하고, `compare`는 틱당 비율의 전후 값과 퍼센트포인트 차이를 우선 출력한다. CPU 시간과 경과 시간은 서로 섞어 비율을 만들지 않는다. 같은 스케일로 느려지는 환경 변화는 줄일 수 있지만 GC/작업 종류별 캐시 특성/스케줄링 간섭을 완전히 보정하지는 않는다. 기준 입력·반복 수·패치가 달라지거나 기준이 없으면 비교를 거부한다.

- `inclusiveMs`: 선택 메서드 본문과 하위 호출의 경과 시간 합계. 상하위 값을 더하면 중복된다.
- `trackedSelfMs`: 선택된 중첩 하위 메서드의 경과 시간을 뺀 값. 미계측 하위 작업과 계측 비용을 포함하며 진짜 self CPU가 아니다.
- `threadCpuMs`: Windows `GetThreadTimes`의 user+kernel CPU 차이. 기본 대상은 전체 게임 틱 `DoSingleTick`뿐이다. 짧은 메서드는 Windows 시간 해상도로 잘못 귀속될 수 있으므로 CPU 값으로 순위를 매기지 않는다. CPU 미계측 메서드의 0은 무비용을 의미하지 않는다.
- `calls`, `exceptions`, `maxMs`: 완료된 호출만 집계한다. 재귀는 호출마다 별도 스택 슬롯을 사용하며 Harmony finalizer에서 예외를 원래대로 돌려준다.
- 반환된 iterator/Task의 후속 작업은 생성 메서드 시간에 포함되지 않는다. 자체 compiler iterator는 가능한 경우 `MoveNext`도 등록하지만 공용 LINQ 전체를 자동 계측하지 않는다.

현재 대상 스레드는 게임 메인 스레드 하나다. 다른 스레드 호출은 제외하고 `dropped`를 증가시킨다. 대상 128개, 중첩 128단계, 일반 수집 시간은 1~300초로 제한한다. 명시적인 `hdTacticalEngineAudit` 격리 시험에 한해 최대 1,800초를 허용해 대규모 고정 틱 창이 300초에서 잘리지 않게 한다. `complete=false`나 dropped가 있는 결과는 정상 비교 대상으로 쓰지 않는다. 무제한 이벤트 로그는 저장하지 않는다. 계측 중 게임 객체를 다른 스레드에서 읽지 않는다.

메서드 진입에서는 준비된 MethodBase→정수 ID 사전 조회와 고정 배열을 사용한다. 메서드 이름/Reflection/JSON 생성은 등록과 종료 단계에서만 수행한다. 결과 직렬화는 종료 시 메인 스레드에서 동기 수행하고, 그 비용은 수집 기간 밖에 둔다. 독립 모드 배포, 다중 스레드 수집, 네이티브 샘플러와 MCP 서버는 아직 구현하지 않았다.

`compare`는 인원·시나리오·속도·게임/런타임/OS 버전·활성 모드·CPU 수집 모드·계측 메서드 집합이 다르거나 틱이 없거나 불완전하면 거부한다. 빌드 차이는 출력하며 동일 조건의 메서드별 ms/tick 차이를 보여준다. 같은 시나리오 번호만으로 지도/전투 단계/시스템 부하가 같다고 보증할 수 없으므로 원래 감사 결과도 함께 확인한다. 일시 정지 수집의 ms/tick은 null로 표시한다.

수집기는 성능 개선이 필요한 위치를 좁히는 도구다. 세부 패치가 특히 짧은 메서드의 비용을 높일 수 있으므로 coarse와 비활성 대조군을 함께 확인하고, 계측 시간을 추정치로 일괄 차감하지 않는다.

## 틱 스파이크 추적 (schema 5)

`spikes` 프리셋은 전체 틱·맵/게임 컴포넌트 경계에 바닐라 `TickList.Tick`, `Pawn.TickInterval`, 작업 시작/종료/검색, `ThinkNode_JobGiver`, 교전 JobGiver, `PatherTick`, PathFinder 작업 완료/요청과 New 전술 판단/복귀/명령을 더한다. 기존 coarse의 Legacy 통신·이동 내부 목록은 제외한다. 실제 등록된 오버로드는 capabilities를 확인한다. 이는 긴 바닐라 작업 검색과 TickList 지연을 폰 및 전술 명령까지 연결하기 위한 진단 모드다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/TacticalEngineAudit/Run-EngineAudit.ps1 -Engine new -Workload sapper-wall -Population 204 -WarmupTicks 1200 -SampleTicks 600 -ProfilePreset spikes
dotnet $cli spikes $capture
dotnet $cli spikes $capture 12345
dotnet $cli start $root 10 cpu spikes threshold=5
```

일반 플레이는 `-hdMethodProfilePreset=spikes`로 켠다. `-hdMethodProfileSpikes=true/false`로 기록만 별도 선택할 수 있고 `-hdMethodProfileSpikeThresholdMs=5`로 느린 틱 기준을 바꾼다(0.1~1000ms). CLI start의 spikes는 이미 설치된 대상의 기록만 켜며 새 계측 패치를 추가하지 않는다. 생략하면 시작 옵션을 따른다. 기존 coarse/detailed 기본 기록은 꺼져 있다.

기준 이상 `DoSingleTick` 중 가장 긴 8개만 보존하며, 각각 선택된 완료 호출 512개까지 기록한다. 매 틱 GC 컬렉션 카운터를 읽고 고정 배열에 구조체를 기록한다. 종료 시에만 JSON을 만든다. 마지막 슬롯은 루트 틱을 위해 예약한다. `spikeCandidates`는 기준 이상 틱 전체 수, `callsSeen`은 해당 틱의 선택된 완료 호출 수, `detailsDropped`는 512개 한도로 잘린 세부 호출 수다. 이는 집계 손실인 `dropped`와 다르며 집계와 분위수는 계속 전체 호출을 처리한다. 기록은 처음 완료된 호출부터 채우므로 긴 틱의 후반부는 잘릴 수 있다. 전체 메서드의 긴 호출 16개는 별도로 유지하며 추적 모드에서 동일한 문맥을 붙인다.

각 호출에는 `callId/parentCallId/rootCallId/depth`, 캡처 시작 기준 `startMs`, 경과·선택 하위 제외 시간, 틱·렌더 프레임, 가능한 폰/맵 ID·분대 ID·진입 당시 단계·작업 DefName을 저장한다. Pawn 인스턴스, 트래커의 pawn 필드, pawn/member/command 매개변수를 typed Harmony prefix로 연결한다. 선택된 부모의 문맥은 내부 호출에 상속한다. New의 기존 byPawn 사전만 읽으며 조직 생성·계획 재계산은 하지 않는다. 알 수 없는 ID는 -1, 문자열은 null이다. 루트 진입이 게임 틱 증가보다 빠르므로 루트의 다음 틱 번호를 자식 전체에 통일한다. 기록된 간선은 **선택된 호출 간 관계**이며 미계측 내부 호출을 복원하지 않는다.

CLI는 메서드 이름과 Core 대비 호출 비용을 붙이고, 해당 틱의 폰 호출 중 선택 하위를 뺀 비용 상위 12개도 보여준다. `gc0/1/2`는 그 틱 중 컬렉션 횟수 차이로 원인 확정값이 아니다. 개별 호출은 elapsed이며 루트의 `threadCpuMs`만 기존 Windows의 거친 CPU 계수다. 스케줄링 대기·경로 워커 대기와 CPU 연산을 구분할 때 두 값을 함께 본다. 추적 on/off 및 임계값이 다른 결과는 compare/aggregate/engine-compare에서 같은 조건으로 묶지 않는다.

## 고정 조건 반복 감사와 호출 분포

`Run-RepeatedBenchmarks.ps1`은 같은 시드로 2~10회 격리 실행하고 `aggregate.json`을 생성한다. 준비 600틱, 측정 1200틱이 기본이다. `-High`는 실제 생성기를 사용해 13인 분대(화력조 3개)를 생성한다. 요청 50/100/200/400명은 HIGH에서 52/104/208/403명으로 올림하며 실제 인원과 장착된 무전기 보유 인원을 따로 기록한다.

기록에는 초기 지도 구조 SHA256, 시드, 시작/종료 전술 단계, 준비/측정 틱, 분대 수가 포함된다. CLI compare는 시작 단계나 지도·편제·준비 조건이 다르면 비교를 거부한다. aggregate는 같은 빌드·지도·시작 단계·계측 대상·Core 기준의 실행만 묶고 각 실행의 Core 대비 비율 중앙값과 범위를 기록한다. 종료 단계가 달라질 수 있으므로 감사의 이동·생존·완료 정보를 같이 검토해야 한다.

호출 분포는 메서드별 마지막 2048회만 보관하며 p50/p95/p99는 nearest-rank 방식이다. 5ms 이상 호출 중 가장 긴 16개도 보관한다. 중첩 호출은 서로 시간이 겹친다. 반복 집계의 p95/p99는 각 실행 분위수의 중앙값이며 전체 호출을 합친 분위수가 아니다. 프로파일링이 비활성화된 일반 세션에는 이 기록을 만들지 않는다.

반복 비교는 `benchmark-compare BEFORE_ROOT AFTER_ROOT`로 수행한다. 조건이 다른 그룹은 unmatched로 기록하며 일치하는 시작 단계만 비교한다. 호출 p95/p99에도 Core 대비 비율을 함께 출력한다. 완료된 같은 조건을 재사용하려면 반복 실행기의 `-Resume`을 사용한다. 고정 전장 버전 2는 지도 전체의 지형·건물·지붕을 정리해 물리 지도 해시를 동일하게 만든다.

최적화 근거로 사용하기 전에는 감사 결과도 검사한다. `Compare-ReviewedBenchmarks.ps1`은 각 실행의 `measurements.ndjson`과 인접한 `profiles/capture-*.json`을 연결해 수집 예외/dropped, 불완전 감사, 메타데이터 불일치, 사상자, 95% 미만 이동 기록을 제외한다. 원본을 변경하지 않고 CreatorTemp의 새 폴더에 선택 기록, `review.json`, 조건을 다시 맞춘 `comparison.json`을 만든다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/AgentProfiler/Compare-ReviewedBenchmarks.ps1 -BeforeRoot $before -AfterRoot $after
```

95% 이동은 정지한 실행을 개선으로 오인하지 않기 위한 최소 검사다. 적절한 대형, 집결 도착, 목표 클리어, 동일한 전술 작업량을 보증하지 않는다. 종료 단계와 긴 호출은 계속 원본 감사와 함께 검토한다. 분위수 0인 미호출 메서드도 개선으로 해석하지 않는다.

## 폰 내부 스파이크 진단 (schema 6)

`pawn-spikes`는 `spikes`에 폰 내부 트래커와 작업 실행을 추가한다. 설치된 게임의 Pawn.Tick/TickInterval, ThingWithComps.Tick/TickInterval, JobTrackerTick/JobTrackerTickInterval의 원본 IL에서 직접 호출되는 Tick 메서드만 등록한다. DriverTick/DriverTickInterval, 건강·정신·욕구·장비·유전자 트래커 등을 포함한다. Reflection/IL 분석은 시작 시 한 번만 수행하며 프로퍼티 getter나 게임 전체 메서드를 계측하지 않는다. 상시/주기 작업 판단과 경로 시작·재생성 메서드도 추가한다. 구체적인 등록 서명은 capabilities를 확인한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/TacticalEngineAudit/Run-EngineAudit.ps1 -Engine new -Workload sapper-wall -Population 60 -WarmupTicks 600 -SampleTicks 180 -ProfilePreset pawn-spikes
dotnet $cli spikes $capture
dotnet $cli pawn $capture 845
dotnet $cli pawn $capture 845 1584
```

`pawn`은 보존된 느린 틱에서 해당 ID의 호출을 작업·전술 단계·분대별로 묶고, 호출 수/평균/최대/누적 경과 시간/선택 하위 제외 시간/Core 대비 값을 반환한다. 시간순 호출 ID와 부모 ID도 유지한다. 전체 수집 기간의 폰별 총비용이 아니며, 기록이 없다는 이유로 무비용으로 판단하면 안 된다. 가장 긴 호출 목록에만 남은 호출은 별도로 출력해 이중 집계하지 않는다.

많은 트래커 호출 때문에 512개 세부 기록이 잘리면, 다음 수집을 특정 폰으로 제한한다.

```powershell
dotnet $cli start $root 10 cpu spikes pawn=845 threshold=5
```

일반 플레이 실행 인자는 `-hdMethodProfilePreset=pawn-spikes -hdMethodProfileSpikePawnId=845`다. ID는 새 게임/다른 습격에서 바뀔 수 있으므로 현재 캡처에서 확인한다. 필터는 **느린 틱의 세부 기록만** 제한하며 전체 메서드 집계·분위수·루트 틱 CPU·긴 호출 16개는 계속 수집한다. `callsSeen`은 루트를 포함한 필터 통과 호출 수, `callsFiltered`는 의도적으로 제외한 호출 수, `detailsDropped`는 통과 호출 중 512개 상한으로 잘린 수다. 필터 모드에서는 다른 폰/전역 부모가 누락될 수 있다. 느린 틱 상위 8개는 여전히 전체 틱 시간으로 선정한다.

격리 감사 실행기에서는 `-SpikePawnId 845`를 사용한다. 재현된 같은 폰 ID가 맞는지 원본 기록으로 확인한다.

필터 ID가 다른 캡처는 비교/반복 집계/엔진 비교의 같은 조건으로 묶지 않는다. 진단 프리셋은 계측 비용이 크므로 평소 CPU 게이트 판정 대신 원인 분해에 짧게 사용한다. 각 트래커 값은 elapsed이며 개별 메서드의 정밀 CPU 샘플링으로 해석하지 않는다.

## 상세 기록이 잘려도 남는 느린 틱 집계 (schema 7)

`spikes CAPTURE [TICK]`의 각 틱에는 `wholeTickMethods`가 추가된다. 상세 호출 512개 한도 및 폰 필터와 별개로, 그 틱 안에서 완료된 **선택 메서드 전체 호출**을 집계한다. 틱 후반에 호출된 메서드도 호출 수·예외 수·누적 경과 시간·선택 하위 제외 시간·평균·최대·Core 배치 대비 비용으로 확인할 수 있다. 기존 상세 타임라인과 폰 진단의 기록 한도는 그대로다.

목록은 `trackedSelfMs` 내림차순이다. `trackedSelfPercentOfTick`은 해당 메서드의 선택 하위 제외 시간이 전체 틱 경과 시간에서 차지하는 비율이다. 이것은 CPU 점유율이나 진짜 self CPU가 아니다. 미계측 작업·대기·계측 비용은 이를 감싸는 선택 메서드에 포함된다. `inclusiveMs`를 합산하면 중복된다. `referencePercentPerTick`은 **해당 느린 틱 하나**의 누적 비용/Core 배치 중앙값이며 전체 측정 기간의 평균이 아니다.

원본 `tickSpikes[].methods`는 메서드 ID와 합계를 보존한다. `methodsComplete`는 해당 틱 안에서 계측 대상 호출을 깊이 제한/스레드 제한 등으로 잃지 않았다는 뜻이다. `detailsComplete=false` 또는 `callsFiltered>0`인 틱에서도 `methodsComplete=true`일 수 있다. 미계측 메서드까지 측정했다는 뜻은 아니다. 집계는 여전히 가장 긴 틱 8개에만 보존된다.

추적 모드에서만 시작 시 고정 배열을 할당하고, 매 호출은 배열 항목만 갱신한다. 최대 128개 메서드 기준 추가 데이터 배열은 약 45KiB다. 틱마다 현재 집계를 비우고 보존 순위에 들어갈 때만 복사하며, JSON은 수집 종료 후 생성한다. 추적 schema가 다른 수집은 compare/반복 집계/엔진 비교에서 같은 계측 조건으로 묶지 않는다. 일반 coarse/detailed의 추적 비활성 실행에는 이 배열과 집계가 없다.

## 좁은 내부 진단과 초기 호출 (schema 8)

`needs-spikes`, `jobs-spikes`, `path-spikes`는 `spikes`의 기본 틱·컴포넌트·폰·작업 경계에 아래 대상만 추가한다. `pawn-spikes`의 모든 트래커를 동시에 등록하지 않는다. 게임 시작 시 선택하며 CLI `start`에서 프리셋을 바꾸거나 새 패치를 설치하지 않는다.

| 프리셋 | 추가로 확인할 범위 |
|---|---|
| `needs-spikes` | 욕구 트래커와 게임/Helodrace의 구체적인 `NeedInterval` 구현. 음식·휴식·기분 등을 개별적으로 구분 |
| `jobs-spikes` | 다음 작업 판단, 전투 JobGiver, 사격 위치 검색과 이 메서드들이 원본 IL에서 직접 호출하는 메서드 한 단계 |
| `path-spikes` | 경로 요청·시작·갱신·완료 대기와 원본 IL의 직접 호출 한 단계. Unity 워커의 실행 시간은 수집하지 않음 |

Reflection/IL 분석은 등록 때 한 번만 수행한다. 직접 호출 확장은 같은 클래스·Verse.AI·JobGiver 구현으로 제한해 공용 난수/수학 보조 함수를 따라가지 않는다. getter·추상/제네릭 메서드·Unity/프레임워크 호출은 추가하지 않으며, 전체 대상 128개 상한을 넘으면 명시적으로 초기화를 거부한다. `capabilities`에 실제 등록된 전체 서명이 나온다. 욕구 프리셋은 다른 모드의 자체 Need 구현을 자동 등록하지 않는다. 필요한 구체적인 게임/Helodrace 메서드는 `-ProfileTargets`로 추가할 수 있다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/TacticalEngineAudit/Run-EngineAudit.ps1 -Engine new -Workload sapper-wall -Population 60 -WarmupTicks 120 -SampleTicks 180 -ProfilePreset needs-spikes -SpikeThresholdMs 0.1
dotnet $cli initial $capture
dotnet $cli initial $capture NeedInterval
dotnet $cli spikes $capture
```

추적이 켜지면 메서드마다 **수집 중 최초 8회 진입**의 완료 결과를 별도 고정 배열에 보존한다. 느린 틱 상위 8개·상세 호출 512개·폰 필터와 독립적이며, 빠른 호출이나 틱 밖의 호출도 남는다. 일반 추적 비활성 수집에는 이 배열을 할당하지 않는다. 각 상세 호출의 `invocation`은 메서드별 진입 순서다. 재귀 호출도 진입 순서대로 번호를 부여한다. 미완료 호출은 종료 시 배출을 기다리며 정상 완료된 결과만 기록한다.

`initial CAPTURE [METHOD_TEXT]`는 초기 호출의 시점·폰/작업/단계·경과 시간, 초기 평균/최대/Core 비율, 초기 표본을 제외한 전체 호출의 평균과 빈도를 반환한다. `largestRetainedLaterCallMs`는 이후 호출 전체의 최대가 아니라 보존된 느린 호출/틱에서 찾은 최대다. 기록이 없으면 null이다. `hotspots`에는 `meanMs`와 `callsPerTick`도 나온다.

**수집 초반 호출은 JIT/콜드 캐시 판정이 아니다.** 준비 틱 전에 이미 같은 메서드가 실행됐을 수 있다. 초기 단발 비용인지 반복 비용인지 조사할 실마리이며, 준비 0/준비 후 수집과 게임 문맥을 함께 검토한다. 메서드별 값은 경과 시간이고 실제 CPU를 의미하지 않는다. 추적 스키마가 다른 실행의 성능 수치를 직접 비교하지 않는다.

메서드별 `foreignThreadCalls`와 `depthLimitCalls`는 각각 다른 스레드/중첩 상한으로 제외된 호출 수다. `hotspots`의 `excludedMethods`에는 완료 호출이 하나도 없는 제외 메서드도 나온다. 이유별 통계를 추가해도 `dropped`와 불완전 캡처 비교 거부 규칙은 유지한다. 워커 호출은 CPU/문맥 기록에 포함하지 않고 카운터만 원자적으로 증가시킨다.
