# 전술 엔진 대조 실행 — R1

같은 DLL/콘텐츠에서 `vanilla`, `legacy`, `new`를 선택한다. 기본 플레이는 `legacy`다. 선택은 게임 시작 시 고정하며 실행 중 전환하지 않는다.

**R1의 `new`는 새 실행기가 없어서 바닐라 Lord로 움직인다.** `effectiveEngine=vanilla-fallback`, `newEngineImplemented=false`이며 2배 이하라도 새 AI 합격으로 처리하지 않는다. 최소 돌파·진입은 R2다. 기존 전술 계획/캐시/노드 개발자 도구는 Legacy용이다.

## 실행

먼저 게임 DLL과 CLI를 빌드한다. 실행기는 DLL/PDB를 설치된 모드에 복사하고 해시를 확인한다. 열려 있는 RimWorld가 있으면 시작하지 않는다. 별도의 CreatorTemp 저장/설정/로그 폴더에서 실행하고 완료하면 자체 종료한다. 기존 저장과 모드 설정은 변경하지 않는다.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore -p:EnableSourceLink=false
dotnet build Source/Tools/AgentProfiler/AgentProfiler.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/TacticalEngineAudit/Run-EngineAudit.ps1 -Engine vanilla -Workload open-approach -Population 12 -WarmupTicks 180 -SampleTicks 1200
```

반복은 PowerShell에서 아래처럼 호출한다. 순서는 회차마다 회전하고 매번 새 게임/프로세스로 실행한다. 기존 결과 폴더를 재사용하지 않는다.

```powershell
& Source/Benchmarks/TacticalEngineAudit/Run-EngineMatrix.ps1 -Repeats 3 -Population 12 -Workloads open-approach -WarmupTicks 180 -SampleTicks 1200
& Source/Benchmarks/TacticalEngineAudit/Run-EngineMatrix.ps1 -Repeats 3 -Population 13 -High -Workloads sapper-wall -WarmupTicks 600 -SampleTicks 1800
```

`-NoMethodProfile`은 계측 없는 기능 대조, `-Population 0`은 전술 대상 없는 유휴 대조다. 단일 실행기의 `-ProfileTargets '실제선언타입::메서드;실제선언타입::메서드'`로 추가 선택한다. 기본 추가 대상은 `Verse.AI.Pawn_JobTracker.StartJob`과 `Verse.PathFinder.CreateRequest`다.

## 조건과 격리

- 실제 조직 생성기로 LOW 12인 대전기 소총 분대, HIGH 13인 분대(3화력조)를 만든다. 완편 단위로 올림하므로 요청 수와 실제 수가 다를 수 있다.
- 시드·편제·지도·장비·목표·Lord 종류를 고정한다. 지도 해시와 폰의 나이/기술/질환/장비/모듈 무장/방탄복 파츠 해시를 저장한다.
- `open-approach`는 기존 구멍이 있는 외벽/일반 Lord, `sapper-wall`은 닫힌 외벽/sapper Lord다. 성능 그룹을 섞지 않는다.
- `sapper-wall`은 채굴 가능한 첫 폰의 채굴 기술을 20으로 고정하고 해당 폰카인드의 바닐라 공병 자격을 격리 게임 안에서만 부여한다. HIGH 기본 폰카인드는 자격이 없기 때문이다. 모든 엔진에 동일 적용하고 폰 해시/자격 수로 확인하며 실제 편제 XML은 바꾸지 않는다.
- 이름 있는 침대에 배정된 식민자를 목표로 두고 정지·비무장·피해 면역으로 고정한다. 실제 교전·방 정리 완료 시험을 대체하지 않는다.
- Vanilla/New는 11개 구형 Map/GameComponent를 실행 목록에서 제거하고 25개 구형 Harmony 패치 클래스는 설치하지 않는다. 캐시 FinalizeInit 전 필터링하고 제거한 MapComponent를 정리한다. 편제·장비 효과·조직 등록/표시는 공통 유지한다.
- 초기/종료 실제 컴포넌트 목록과 설치 패치를 audit에 저장한다. 프로파일에서 구형 콜백 호출도 0인지 교차 확인한다.

## 측정과 비교

`audit.json`에는 생존/이동 인원, 실내 진입 누적 인원, 목표 6칸 이내 접근 누적 인원, 최초 진입/목표 접근/마지막 진행 틱이 들어간다. `moved`는 종료 위치가 시작과 다른 인원이며 방 클리어 완료가 아니다. Legacy 시작/종료 단계도 capture의 benchmark에 저장한다.

전체 틱 CPU, 측정 창 전체 메인 CPU, 프로세스 CPU를 각각 기록한다. 창 전체에는 렌더링·다른 시스템·주기적인 Core 배치도 포함된다. 종료 후 격리 확인/직렬화는 창 밖에 둔다. 프로세스−메인 차이는 개별 워커 CPU 계측이 아니다.

컴포넌트/내부 메서드는 Stopwatch 경과 시간이며 실제 OS CPU 귀속은 전체 틱에만 있다. 부모/자식 inclusive를 더하지 않는다. 호출 분포는 마지막 2048회다. Core 10,000회 배치 비율을 CPU 점유율로 해석하지 않는다.

```powershell
$cli = 'Source/Tools/AgentProfiler/bin/Debug/net10.0/AgentProfiler.dll'
dotnet $cli engine-compare "$matrix/open-approach/vanilla" "$matrix/open-approach/new"
dotnet $cli engine-compare "$matrix/open-approach/vanilla" "$matrix/open-approach/legacy"
```

`engine-compare`는 완료된 fixture v5 audit/schema 4 capture를 함께 읽는다. 같은 DLL·지도·폰/장비·시나리오·인원·편제·준비·수집 대상·Core 작업만 묶고 엔진별 시작 단계 차이는 허용한다. 일반 `compare`는 같은 엔진/시작 단계만 비교한다. 95% 미만 이동, CPU/틱 누락, 오류, dropped, 격리 실패는 거부한다. Legacy 진입 미완료도 숨기지 않는다.

CPU ms/틱 중앙값/범위, 실행별 진행과 Core 기준 메서드 비용을 출력한다. 구현된 New의 최소 3회 대조 및 실제 진입이 있어야 고정 창 CPU 판정 대상이다. `newAiFixedWindowCpuGatePassed`도 기능 완료나 전체 CQB/야전 합격을 증명하지 않는다. R2 이후 완료 시험과 함께 판정한다.

오류/타임아웃의 원본 로그와 audit를 보존한다. 관측 타임아웃만으로 재시작하지 않고 해당 PID와 상태/로그부터 확인한다.
