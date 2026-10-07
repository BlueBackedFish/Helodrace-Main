# 2026-10-07 최적화 2~4단계 수집 자료

고정 전장 v2, seed `hd-perf-20261007`, 준비 600틱/측정 1200틱, 3배속, RimWorld 1.6.4871. 모든 물리 지도 해시는 `2DCA10EF6A532BA820CA2CFA41B18D21E564655193AC63E429759ED9D0135787`이다. Core 기준은 `Verse.GenRadial.NumCellsInRadius(float)`의 고정 10,000회 배치다. 비율 100%는 전체 CPU 점유율이 아니다.

| 폴더 | 의미 |
|---|---|
| before-low / before-high | 1단계 이후, 통신/반응 최적화 전. 각 2회 |
| communications-low | 통신만 변경한 중간 경량 측정. 50/200명 각 1회 |
| intermediate-low / intermediate-high | 통신·반응 주기 변경 후, 관측 후보 사각형 검색 전. 각 2회 |
| after-low / after-high | 관측 후보 검색까지 적용한 최종 코드. 각 2회 |
| detailed-before / detailed-after | 통신 내부 호출 감소 확인. LOW 50/200명 각 1회, 동일 상세 계측 대상 |

각 실행 폴더의 `measurements.ndjson`과 `profiles/capture-*.json`을 연결해 해석한다. 경량과 상세 결과를 같은 비교 그룹에 넣지 않는다. `*-aggregate.json`은 원본 기록 전체의 조건별 집계이고 `*-reviewed-comparison.json`은 감사 검사로 오염 기록을 제외한 비교다. 제외 사유는 `*-review.json`에 남긴다. 시작 단계, 무전 보유 인원 등 조건이 다른 그룹은 comparison의 unmatched에 남긴다. 종료 단계와 진행 작업량은 조건이 같아도 다를 수 있다.

원본은 사상자/불완전 준비 등 불리한 결과도 보존한다. intermediate LOW 100명 첫 실행의 3명 손실 기록은 정상 개선 비교에서 제외한다. HIGH 208명의 before 첫 실행은 실제 무전 인원이 206명이어서 208명 무전 조건의 기록과 합치지 않는다. 최종 비교의 반복 수는 각 comparison 그룹의 beforeRuns/afterRuns가 기준이다.

| 빌드 | DLL SHA256 |
|---|---|
| before | `79c2b924f55867196455bdef7caa8bac4d34edb8d9846e315fe1d2b107568ad8` |
| communications-only | `9d68ba9c5bab8e6ccd7a6d17dcc6b2837dfe872da714379394dfce0a8ff6167d` |
| intermediate / detailed-after | `6ca3ae51fe56ddb3b76d50ff01c0b26368fcc64b50fa77b661a4f314a1466b0b` |
| after | `4dc08f5f64226ef5becf8b20f8afa00fa12ee520e5f471e9d8383cea43e93178` |

functional-final-audit는 최종 DLL의 7단계 통과 기록이다. functional-audit는 앞선 진단 통과, initial/before/held-failed는 문 자동 닫힘 및 대형 도착 실패 기록이다. 상세 해석과 남은 과제는 [검증 결과 문서](../../../../../Docs/%EC%A0%84%EC%88%A0/Archive/2026-10-07/%ED%86%B5%EC%8B%A0%EA%B3%BC%20%EC%8B%A4%ED%96%89%20%EB%B0%98%EC%9D%91%20%EC%B5%9C%EC%A0%81%ED%99%94%20%EB%B0%8F%20%EB%B0%98%EB%B3%B5%20%EA%B2%80%EC%A6%9D%20%EA%B2%B0%EA%B3%BC.md)에 있다.

```powershell
$root = 'Source/Benchmarks/AgentProfiler/Results/2026-10-07-stages234'
$cli = 'Source/Tools/AgentProfiler/bin/Debug/net10.0/AgentProfiler.dll'
dotnet $cli aggregate "$root/after-low"
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Benchmarks/AgentProfiler/Compare-ReviewedBenchmarks.ps1 -BeforeRoot "$root/before-low" -AfterRoot "$root/after-low"
```

분포는 메서드별 마지막 2048회, 반복 집계의 분위수는 실행별 분위수 중앙값이다. 상위 16개 긴 호출은 부모/자식 중첩을 포함하고 전체 호출 히스토리가 아니다. 짧은 메서드 시간은 경과 시간이고 OS 스레드 CPU는 DoSingleTick에만 기록한다. 세션이 complete, dropped=0, 계측 예외=0이어도 game 로그 전체가 무오류라는 뜻은 아니다.
