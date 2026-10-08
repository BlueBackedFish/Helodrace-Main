# R6 야전 기본 약식 실행

2026-10-09. LOW 12명·High 13명, 각각 4,800틱, fixture v20. 관련 야전/관측/경계 메서드만 추가 수집한다. `audit.json`은 동작 근거, `capture.json`은 원본 측정, `summary.json`은 호출·평균·상위 지연·Core 비율, `provenance.json`은 원본 SHA256이다.

두 실행 모두 사거리 밖 실제 감지·관측 이동축·고유 자리·숨은 위치 고정·CQB 복귀·전체 임무 완료. High는 실제 한 화력조 기동도 확인했다. LOW 실행 후 벽에 잘린 자리의 현재 위치 주변 fallback을 추가했으며 High 실행에 그 빌드가 포함된다. 서로 다른 DLL의 실행을 동일 빌드 반복 CPU 비교로 합치지 않는다.

메서드 측정은 inclusive 경과 시간이다. AssignFieldPost/BeginFieldBound는 AdvanceFieldResponse 하위이므로 중복 합산하지 않는다. 전체 틱만 OS CPU를 측정한다. Core는 고정 10,000회 배치 중앙값이다. N/V 2배 정식 판정, 대규모/다중 맵/저장·로드는 R7이다. 계획 연막과 치료 AI는 이 실행의 검증 대상이 아니다.
