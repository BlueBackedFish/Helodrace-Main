# 대기 실행·관측 캐시 전후 검증

[변경과 해석](../../../../../Docs/%EC%A0%84%EC%88%A0/Archive/2026-10-07/2%EC%88%9C%EC%9C%84%20%EB%8C%80%EA%B8%B0%20%EC%8B%A4%ED%96%89%EA%B3%BC%20%EA%B4%80%EC%B8%A1%20%EC%BA%90%EC%8B%9C%20%EC%A0%81%EC%9A%A9%20%EA%B2%B0%EA%B3%BC.md).

LOW 200→400→0명, 준비 600틱, 요청 측정 900틱, 동일 seed·지형·속도. coarse 기본 맥락 외 추가 대상은 RunScheduledUnit, RefreshContactsCore, VisibleArmedEnemies, WaitForSharedOpeningCore 네 개뿐이다.

- before/after의 capture와 capabilities는 원본 계측·대상 목록이다. after는 최종 DLL과 일치한다. 중간 DLL의 탐색 시험은 개선율에 섞지 않았다.
- measurements.ndjson은 이동·생존·시작/종료 단계·완료 기록이다.
- metrics.csv는 관련 4개 메서드와 DoSingleTick의 비용을 추린 자료다. 관련 메서드 실제 CPU 칸은 미수집으로 비워 두었다.
- review.json/comparison.json은 기존 CLI의 적격 검토와 비교 결과다. 200명은 시작 단계 불일치로 unmatched이며 400명과 0명만 정상 비교된다.
- Player.log.gz는 원본 로그 바이트를 보존했다.
- functional-before, functional-after, functional-after-failed는 비계측 이동 시험의 원본이다. 변경 전과 최종 재시험은 7단계 통과했으며, 최종 최초 시험의 단발성 1단계 실패도 보존했다.

전체 6개 성능 capture는 complete=true, dropped=0, 메서드 exceptions=0이다. 기능 실패는 정상 성능 capture와 구분한다. 400명 RunScheduledUnit의 Core 기준 틱당 경과 비용은 68.72% 줄었으나, 전체 틱 CPU는 13.050→14.740ms/tick으로 증가했다. 전체 CPU 개선 근거로 사용하지 않는다.

Core 100%는 GenRadial.NumCellsInRadius(float) 10,000회 배치 중앙값이다. 메서드 inclusive 시간은 중첩되므로 합산하지 않는다. p95/p99는 최근 2048회이며, 호출 평균·최대도 CSV와 원본에서 확인할 수 있다. 종료 단계와 스캔 작업량이 완전히 같은 시험이 아니며, 다수 무장 적 교전의 절감률은 확인하지 않았다.
