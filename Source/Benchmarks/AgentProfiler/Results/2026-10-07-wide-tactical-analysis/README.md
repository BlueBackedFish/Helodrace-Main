# 전술 AI 광범위 계측 자료 — 2026-10-07

[분석 보고서](../../../../../Docs/%EC%A0%84%EC%88%A0/Archive/2026-10-07/%EC%A0%84%EC%88%A0%20AI%20CPU%EC%99%80%20%ED%8B%B1%20%EC%A7%80%EC%97%B0%20%EC%B5%9C%EC%A0%81%ED%99%94%20%EA%B0%80%EB%8A%A5%EC%84%B1%20%EC%A1%B0%EC%82%AC.md)의 원본 데이터다. 이번 작업은 조사만 수행했다. 게임 코드·DLL 수정, 빌드, 배포, 최적화 구현은 하지 않았다.

기존 설치 DLL SHA256: `4dc08f5f64226ef5becf8b20f8afa00fa12ee520e5f471e9d8383cea43e93178`. 별도 CreatorTemp savedata/로그를 사용하는 quicktest이며 설치 DLL/PDB 복사 부분을 제외한 임시 실행기를 사용했다. 실제 게임 Pawn/Job/PathFinder를 실행한다. 기존 플레이 저장과 설정은 사용하지 않았다.

44개 완료 capture, 구체적 메서드 서명 350개(Helodrace 336 / engine 14), 실제 호출 서명 260개. 128개 타깃 한도 때문에 여러 실행으로 나눴다. 전체 capture의 complete=true, dropped=0, 계측된 메서드 exceptions=0을 확인했다. **기능 감사 실패 및 불완전 실행은 별도다.**

## 파일

- [methods.csv](methods.csv): capture별 전체 메서드 호출 수, 누적 경과 시간, 평균 µs, ms/tick, calls/tick, max/p50/p95/p99, Core 비율, 실제 CPU 수집 여부. CPU 미측정 칸은 빈 값이다.
- [coverage.csv](coverage.csv): 선택한 전체 서명과 미호출 여부. 서로 다른 phase/인원의 calls 합계는 coverage 확인용이며 성능 비교 기준이 아니다.
- [review.json](review.json): 감사 complete, 이동·생존·단계, 사상자/불완전 제외 이유, capture 검증과 build metadata. `benchmarkAuditEligible`는 같은 조건에서 비교를 검토할 수 있는 최소 필터일 뿐, 다른 target/시작 단계/인원과 바로 비교할 수 있다는 뜻이 아니다.
- [source-fingerprints.csv](source-fingerprints.csv): 조사 전후 관련 소스 130개 파일의 SHA256. 전부 동일하다.
- `targets/*.targets.txt`: 각 수집 묶음의 추가 메서드 선택자. profiler preset은 coarse이며 기본 target도 추가된다. 오버로드 및 iterator MoveNext는 capabilities에서 확인한다.
- 각 run의 `measurements.ndjson`: 실제 감사 상태·메타데이터·기존 stage 계측.
- 각 run의 `Player.log`: 경고, 원인 미확정 종료 및 기능 감사 실패를 포함한 격리 테스트 원본 로그.
- 각 run의 `profiles/capabilities.json`, `capture-*.json`: 등록 대상과 원본 capture. `status/error.json`은 제어 상태 자료이고 capture가 아니다.

설정·저장 파일, 사용자 플레이 자료, 테스트 프로세스 ID와 임시 실행 스크립트는 포함하지 않았다.

## 수집 묶음

| run | 목적 | 감사 결과 |
|---|---|---|
| execution-low / execution-high | 실행·관측·반응 상태 | 각 50/200/400 요청 인원 및 0명, complete |
| movement-low | 이동·합류·security·명령 | complete |
| planning-cold-low | warmup 0, 계획 iterator·cache snapshot | complete |
| communications-high | 통신 descriptor·연결 그래프·보고 | complete; 403명 중 무전기 운용자 401명 |
| lifecycle-low | 반복 상태 검증·대기·생명주기 | complete |
| support-low | 지원·협력·남은 실행 helper | complete; 400명 사례는 394명 생존으로 비교 제외 |
| vanilla-high | Pawn/Job/path와 전술 hook | complete; engine 호출은 게임 전체 |
| verify-low | For 내부 계측 없이 좁은 확인 | complete |
| verify-high | 좁은 HIGH 확인 | 52/208 capture만 있음, 전체 감사 불완전 |
| verify-high-retry | 누락 HIGH 403/cleanup | complete |
| coarse-low-control | LOW 400/0 기본 preset만 | complete |
| unprofiled-low-control | LOW 400/0 신규 profiler 비활성 | complete; OS CPU와 Core 값 없음 |
| functional-movement | security 기능 시도 | 시작 전 종료, 불완전, capture 없음 |
| functional-movement-retry | 11 LOW / 1분대 security 기능 재현 | 7단계 통과, 수동 명령 BOM 오류로 capture 없음 |
| functional-movement-profile | 정상 JSON 수동 계측 | capture 3개, 기능 1단계에서 1800틱 제한 실패 |

기본 warmup=600/sample=1200, 시드 `hd-perf-20261007`, fixture version 2, 지도 구조 해시 `2DCA10EF6A532BA820CA2CFA41B18D21E564655193AC63E429759ED9D0135787`. HIGH 요청 50/200/400은 실제 52/208/403명이다. 단독 400명 대조군은 50→200→400 순서의 실행과 history/phase가 다르다. 사상자·미시작 이동·단계 차이는 원본과 report에서 확인해야 한다.

수동 functional capture는 기본 population/scenario/speed=-1이고 benchmark=null이다. 실제 fixture 인원 11명/LOW 1분대는 기능 감사 코드와 로그의 맥락이며 CSV의 자동 metadata를 임의로 변경하지 않았다. 마지막 진행 중 창은 게임 종료 시 저장되지 않았다. 실패 functional 데이터를 steady 정상 완료 비용/개선율에 포함하지 않는다.

## 해석

Core 100%는 `Verse.GenRadial.NumCellsInRadius(float)`의 고정 10,000회 배치 한 번이다. CPU 점유율이 아니다. 각 capture의 실제 reference 샘플 중앙값으로 정규화한다.

실제 GetThreadTimes CPU는 `DoSingleTick`만 있다. 다른 메서드와 기존 `cpuCumulative` stage는 Stopwatch 경과 시간이다. 짧은 메서드 CPU 값이 없는 것을 0비용으로 해석하지 않는다. p50/p95/p99는 마지막 2048 완료 호출의 분포, max는 전체 구간이다. inclusive는 중첩되며 trackedSelf는 선택된 자식만 제외한다.

프로파일러의 실제 계측 오버헤드, JIT/GC·환경 부하, 시나리오의 진행 차이가 남는다. 모든 메서드 수치를 합산하거나 다른 묶음에서 나온 비율을 하나의 전후 성능 개선으로 집계하지 않는다. 원본 분석/수집 조건은 [프로파일러 설명](../../../../Tools/AgentProfiler/README.md)도 참고한다.
