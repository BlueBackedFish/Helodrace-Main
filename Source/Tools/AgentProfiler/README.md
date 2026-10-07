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

`engine-compare VANILLA_ROOT CANDIDATE_ROOT`는 [R1 실행기](../../Benchmarks/TacticalEngineAudit/README.md)의 audit와 capture를 함께 검증한다. 같은 DLL·지도·장비·편제·준비·수집 조건을 요구하며 엔진별 시작 단계 차이는 허용한다. 일반 `compare`/`benchmark-compare`는 엔진을 섞지 않는다. New의 `vanilla-fallback`은 새 전술 AI 합격으로 판정하지 않는다.

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

현재 대상 스레드는 게임 메인 스레드 하나다. 다른 스레드 호출은 제외하고 `dropped`를 증가시킨다. 대상 128개, 중첩 128단계, 수집 시간 1~300초로 제한한다. `complete=false`나 dropped가 있는 결과는 정상 비교 대상으로 쓰지 않는다. 방법 목록·메타데이터·집계 외에 호출별 문자열/무제한 이벤트 로그는 저장하지 않는다. 계측 중 게임 객체를 다른 스레드에서 읽지 않는다.

메서드 진입에서는 준비된 MethodBase→정수 ID 사전 조회와 고정 배열을 사용한다. 메서드 이름/Reflection/JSON 생성은 등록과 종료 단계에서만 수행한다. 결과 직렬화는 종료 시 메인 스레드에서 동기 수행하고, 그 비용은 수집 기간 밖에 둔다. 독립 모드 배포, 다중 스레드 수집, 호출 간선 트레이스, 네이티브 샘플러와 MCP 서버는 아직 구현하지 않았다.

`compare`는 인원·시나리오·속도·게임/런타임/OS 버전·활성 모드·CPU 수집 모드·계측 메서드 집합이 다르거나 틱이 없거나 불완전하면 거부한다. 빌드 차이는 출력하며 동일 조건의 메서드별 ms/tick 차이를 보여준다. 같은 시나리오 번호만으로 지도/전투 단계/시스템 부하가 같다고 보증할 수 없으므로 원래 감사 결과도 함께 확인한다. 일시 정지 수집의 ms/tick은 null로 표시한다.

수집기는 성능 개선이 필요한 위치를 좁히는 도구다. 세부 패치가 특히 짧은 메서드의 비용을 높일 수 있으므로 coarse와 비활성 대조군을 함께 확인하고, 계측 시간을 추정치로 일괄 차감하지 않는다.

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
