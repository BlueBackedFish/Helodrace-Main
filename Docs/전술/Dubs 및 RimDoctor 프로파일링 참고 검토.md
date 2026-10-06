# Dubs 및 RimDoctor 프로파일링 참고 검토

작성일: 2026-10-06. 소스 검토이며 두 모드를 실행하거나 계측 결과를 측정한 검증은 아니다.

## 결론

두 도구를 참고해 에이전트용 프로파일러를 만들 수 있다. Dubs Performance Analyzer는 선택한 메서드의 내부 호출을 좁혀 조사하는 방식에, RimDoctor는 컴포넌트별 계측·주기적 집계·외부 테스트 실행에 적합한 참고 자료다.

권장안은 독립적인 개발용 수집 모드를 만드는 것이다. 첫 구현은 RimDoctor와 같은 원시 타임스탬프 기반 계측·주기적 집계 구조를 참고하고, Dubs와 같은 내부 호출 분해 기능을 집중 수집 모드에 추가한다. CLI/JSON 제어를 먼저 제공하고 이후 같은 API를 MCP로 노출한다.

확인한 계측 경로에서 두 도구 모두 `Stopwatch` 기반 경과 시간을 측정한다. 이를 참고하는 것만으로 OS CPU 샘플링이나 Mono 메서드별 실제 CPU 시간이 확보되지는 않는다. 스레드 CPU 카운터와 네이티브 샘플러에 관한 기존 검증 계획은 유지한다.

## 확인한 소스 버전

| 도구 | 저장소와 확인 버전 | 검토 범위 |
|---|---|---|
| Dubs Performance Analyzer | [Dubwise56/Dubs-Performance-Analyzer](https://github.com/Dubwise56/Dubs-Performance-Analyzer), develop `3c53c1b23782b3bc813335ea7378ccb7134dd2ad` | 1.6 소스, 계측·내부 호출·집계·정리·저장 경로 |
| RimDoctor | [itygr/RimDoctor](https://github.com/itygr/RimDoctor), `a312bd11e1cf849878b5de7e2d76dfe57f6054d9` | 런타임 성능 패치·귀속·리포트·외부 테스트 스크립트 |

별도 임시 체크아웃: `C:/Users/Public/Documents/ESTsoft/CreatorTemp/hd-profiler-review-20261006`. 프로젝트나 게임에 외부 소스를 추가하지 않았다.

## Dubs에서 참고할 부분

### 선택 대상과 내부 호출 분해

`InternalMethodUtility.Transpiler`는 선택한 메서드의 IL에서 유효한 `call`/`callvirt`를 찾아 계측 래퍼로 교체한다. 호출 opcode와 인자를 전달하며, 구조체 인스턴스는 참조 인자로 다룬다. `constrained` 등 일부 호출은 필터에서 제외한다. 전체 게임을 깊게 계측하는 대신 의심되는 부모 메서드 안의 호출부터 조사하는 구조가 우리 목적과 맞는다.

우리 도구에서는 `RefreshFrames → Delays → Edge → VoiceTo/RadioTo`처럼 에이전트가 한 단계씩 수집 범위를 좁히게 한다. 호출 위치 ID와 실제 callee를 분리하고, 생략한 호출·지원하지 않는 IL을 결과에 표시한다. 일반적인 메서드 진입/종료 계측을 먼저 구현하고, 호출 지점 IL 변환은 별도 지원 기능으로 검증한다.

관련 소스: [InternalMethodUtility.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Utility/ProfilingUtility/InternalMethodUtility.cs), [MethodTransplanting.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Utility/ProfilingUtility/MethodTransplanting.cs), [Utility.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Utility/ProfilingUtility/Utility.cs).

### 대상 등록·수집 수명·결과 저장

- `Analyzer.xml`로 메서드·타입·하위 타입을 등록할 수 있다. 우리 도구도 대상 목록을 파일로 주고 에이전트가 같은 수집을 반복하도록 한다.
- `Profiler`는 2,000개 주기의 시간과 호출 수를 원형 버퍼에 보관한다. 같은 주기 내 여러 호출이 집계되므로 주기당 시간과 호출당 시간을 구분해야 한다.
- 수집 시작/종료와 프로파일링용 Harmony 패치 정리 경로가 있다. 사용 후 무거운 패치를 제거한다는 수명 관리 원칙을 참고한다.
- 주기별 시간·횟수를 저장하고 비교하는 기능이 있다. 에이전트 버전은 GUI나 표시 문자열 대신 버전이 있는 JSON/바이너리 세션 형식으로 제공한다.

관련 소스: [XmlParser.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Utility/XmlParser.cs), [Profiler.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Statistics/Profiler.cs), [Analyzer.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Analyzer.cs), [FileUtility.cs](https://github.com/Dubwise56/Dubs-Performance-Analyzer/blob/3c53c1b23782b3bc813335ea7378ccb7134dd2ad/Source/Profiling/Utility/FileUtility.cs).

### 그대로 적용하지 않을 부분

1. 메서드 시작과 정상 `ret` 앞에 시간을 넣는 경로, 내부 호출 래퍼에는 해당 계측 구간을 예외에서도 종료하는 `finally`가 없다. 우리 도구는 정상/예외 종료 모두에서 정확히 한 번 기록해야 한다.
2. `Profiler` 하나의 `Stopwatch`를 같은 키의 호출들이 공유한다. 해당 모델을 일반적인 재귀·중첩·다중 스레드 도구에 그대로 적용하지 않는다. 스레드별·호출별 상태를 사용한다.
3. 내부 호출 목록의 집계는 완전한 호출 트리나 self CPU와 동일하지 않다. 우리 도구는 부모 ID와 계측 범위를 별도로 기록한다.
4. 핫패스 비용을 줄이기 위해 고정한 관리 객체의 주소를 IL 상수로 넣는 코드가 있다. 첫 구현은 이 런타임 의존 방식을 복제하지 않고 정수 ID와 사전 준비한 저장소를 사용한다.
5. Tick/Update 집계 경계가 현재 GUI 카테고리에 연결된다. 에이전트 도구는 창·선택 탭과 독립적인 수집 모드를 가져야 한다.
6. 집계 작업으로 넘기는 딕셔너리 복사는 내부 `Profiler`와 배열까지 동결하는 깊은 복사가 아니다. 우리 분석기는 종료된 버퍼나 불변 값 스냅샷만 읽는다.

이는 확인한 구현의 적용 범위와 새 도구의 설계 차이다. 두 모드를 실행해 오류나 오버헤드가 관찰됐다는 의미는 아니다.

## RimDoctor에서 참고할 부분

### 실제 컴포넌트 override를 계측

`Patch_Perf_Components`는 Game/Map/WorldComponent의 구체적인 Tick override를 열거해 각각 Harmony Prefix/Postfix를 적용한다. 부모 클래스의 빈 메서드만 패치해서 실제 override 실행을 놓치는 문제를 피한다. `__state`에 원시 타임스탬프를 보관하므로 호출별 상태가 분리된다.

`Patch_Perf_ThingTick`은 고빈도 `Thing.DoTick` 계측을 선택적으로 설치/해제한다. 우리 도구도 큰 구간과 세부 핫패스를 별도 모드로 나눈다. 전체 컴포넌트 상시 계측의 부하가 충분히 작다는 소스 주석은 우리 환경에서 측정된 보장값으로 사용하지 않는다.

관련 소스: [Patch_Perf_Components.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/Patches/Patch_Perf_Components.cs), [Patch_Perf_ThingTick.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/Patches/Patch_Perf_ThingTick.cs).

### 주기 집계와 비용 귀속

`PerfMonitor`는 시간과 횟수를 누적하고 약 1초마다 TPS/FPS/ms per tick/경로 비용을 갱신한다. `TickAttribution`는 누적 딕셔너리를 교체해 이전 기간을 집계하고, 어셈블리→모드 정보를 캐시한다. 수집 중 원시값과 조회용 결과를 구분하는 구조가 에이전트 연결에 유용하다.

다만 Thing의 Def 소유 모드에 귀속한 시간에는 그 폰을 처리하는 바닐라 AI·욕구·건강 등의 비용도 포함된다. 이는 해당 모드 코드만의 실행 비용과 다르다. 우리 출력도 객체 소유, 실행 메서드 소유, Harmony 패치 소유를 구분한다. Thing 수는 CPU 실측값 대신 사용할 수 없다.

관련 소스: [PerfMonitor.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/Runtime/PerfMonitor.cs), [TickAttribution.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/Runtime/TickAttribution.cs), [PanelPerformance.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/UI/PanelPerformance.cs).

### 외부 제어와 진행 감시

`rimdoctor_bisect.sh`는 `-quicktest` 실행, 프로세스 생존, 로그 진행과 성공/실패 표식을 감시한다. `NewColonyMonitor`는 생성 단계 시간과 watchdog으로 정지를 조사한다. 에이전트 테스트도 명령 응답·세션 heartbeat·단계 표식과 절대 시간 제한을 갖추는 방향으로 참고한다.

현재 스크립트는 macOS 경로와 사용자 설정 백업/복원에 맞춰져 있다. Windows에서 그대로 실행하지 않는다. 우리 기존 PowerShell 감사처럼 격리된 `savedatafolder`를 사용하며, 로그가 잠시 조용하다는 이유만으로 전술 정지를 확정하지 않는다. 테스트 완료·실패는 구조화된 응답으로 판단한다.

관련 소스: [rimdoctor_bisect.sh](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/tools/bisect/rimdoctor_bisect.sh), [NewColonyMonitor.cs](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/Source/RimDoctor/Startup/NewColonyMonitor.cs).

### 추가로 바꿀 부분

- 메서드별 고빈도 수집은 매 호출 문자열 키/공유 lock 대신 정수 ID·스레드별 배열에 누적한다.
- TickAttribution의 매초 전체 맵 Thing 수 탐색은 우리 첫 버전의 CPU 계측에 필요하지 않다.
- 타이밍 패치는 확인한 경로에서 Postfix로 기록한다. 게임 메서드가 예외로 끝나도 세션 상태가 균형을 유지하도록 우리 종료 처리를 추가한다.
- 진단 로그의 동기 파일 쓰기는 저빈도 진단용이다. 메서드별 이벤트 파일 쓰기 경로로 사용하지 않는다.
- 성능 리포트의 복사/저장은 UI 메서드에 연결된다. 독립 CLI/JSON 조회와 수집 제어를 별도로 만든다.

## 라이선스와 재사용 범위

RimDoctor에는 [MIT LICENSE](https://github.com/itygr/RimDoctor/blob/a312bd11e1cf849878b5de7e2d76dfe57f6054d9/LICENSE)가 있다. 실제 코드를 재사용하면 저작권·라이선스 고지를 유지하고 가져온 파일과 버전을 기록한다.

확인한 Dubs 커밋의 전체 추적 파일명과 README/Source/About 검색에서는 LICENSE/COPYING/NOTICE나 명시적인 재사용 허가를 찾지 못했다. 공개 소스라는 이유로 코드 복제·재배포를 전제하지 않는다. 현재 권장안은 구조를 참고해 독립 구현하며, 원형 코드 재사용은 허가 조건 확인 후 판단하는 것이다. 이 검토에서는 외부 코드를 프로젝트에 복사하지 않았다.

## 에이전트 연결 방식 선택

| 선택지 | 장점 | 추가 작업 및 판단 |
|---|---|---|
| Dubs에 별도 브리지 모드 연결 | 기존 대상 등록·내부 계측·표시 결과 활용 | GUI 상태와 집계 경계 분리, 내부 API 버전 대응 필요. 기존 사용자 테스트 결과를 JSON으로 받아오는 보조 경로에 적합 |
| RimDoctor에 브리지 연결 | 기존 TPS·컴포넌트·모드별 리포트 활용 | 임의 메서드 깊은 계측과 에이전트 제어는 별도 구현 필요 |
| 독립 수집 모드와 CLI | 필요한 기능·부하·측정 의미를 직접 통제 | 첫 제작량은 늘지만 메서드별 조사와 반복 검증이 주 목적이므로 권장 |

확인한 소스에는 해당 목적의 MCP 서버나 임의 메서드 캡처용 에이전트 제어 API가 없다. 일반 JSON 파서나 모드 이름 약어 `mcp`를 에이전트 연결 구현으로 해석하지 않았다. 브리지 기능은 가능한 확장안이며 실제로 동작한 검증 결과가 아니다.

## 첫 구현에 반영할 변경

1. 선택한 실제 override/메서드에 타임스탬프 계측을 설치하고 호출 횟수·기간별 비용을 집계한다.
2. `communications`와 이동 캐시 Pump/큐 경로를 첫 수집 목록으로 제공한다.
3. 세션이 1초/지정 틱 단위의 값 스냅샷을 제공하고, 에이전트는 상위 메서드와 대상 기간만 조회한다.
4. 같은 세션에서 부모→내부 호출로 수집 범위를 좁힐 수 있게 한다. 완전한 self CPU를 확보한 것으로 표시하지 않는다.
5. 예외·재귀·워커·종료 후 비활성 부하를 검증하고, 이후 CPU 카운터·네이티브 샘플러를 추가한다.

상위 설계: [에이전트 전용 CPU 프로파일러 검토](에이전트%20전용%20CPU%20프로파일러%20검토.md).
