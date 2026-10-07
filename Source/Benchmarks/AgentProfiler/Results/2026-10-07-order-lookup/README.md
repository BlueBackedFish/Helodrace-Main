# 명령 조회 캐시 전후 간단 검증

[변경·검증 결과](../../../../../Docs/전술/1순위%20명령%20조회%20캐시%20적용%20결과.md).

각각 LOW 400명→0명, 준비 600틱, 측정 900틱, seed hd-perf-20261007. extra target은 `Helodrace.MapComponent_RaidTacticalOrders::For` 한 개이며 preset=coarse의 기본 맥락은 동일하다. 이전 광범위 타깃 목록을 사용하지 않았다.

- `before/after/measurements.ndjson`: 생존·이동·전술 단계·완료 원본.
- `before/after/profiles/capture-*.json`, `capabilities.json`: 원본 계측과 대상 목록.
- `before/after/Player.log.gz`: 원본 로그 바이트를 보존한 gzip. 관련 감사 실패/초기화 예외를 확인할 수 있다.
- [metrics.csv](metrics.csv): 분석에 사용한 For/전체 틱만 추린 값. For 실제 CPU 칸은 미수집이라 비워 두었다.
- [comparison.json](comparison.json): 기존 CLI compare 원본, sameTargets=true. scenario metadata만으로 진행/환경이 같음을 보증하지 않는다는 경고를 보존한다.
- [review.json](review.json): complete, 이동·생존·단계와 미시작 명령 상태.

전체 4개 capture는 complete=true, dropped=0, 메서드 exceptions=0이다. 양쪽 감사 complete, 성공 계획 37개, 400명 전원 생존, 이동 399/398명, 미시작 명령 0건. 종료 단계 Support/ObserveOpening 차이는 원본을 참고한다.

Core 100%는 GenRadial.NumCellsInRadius(float) 10,000회 배치 비용이다. 비율은 각 capture 기준 중앙값을 사용한다. For는 경과 시간, 전체 DoSingleTick만 실제 Windows 스레드 CPU다. p95/p99는 마지막 2048회이고 inclusive는 중첩될 수 있다. 단일 간단 전후 시험이며 전체 틱 차이 전부를 패치 효과로 해석하지 않는다.
