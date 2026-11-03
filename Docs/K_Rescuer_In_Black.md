# T2-18 맨인블랙 대체

2026-11-03. Main을 사용하는 게임에서 `StrangerInBlackJoin`의 `pawnKind` 하나만 `HD_RescuerInBlack`으로 교체한다. 원래 `IncidentWorker_WandererJoin`, 위기 호출 조건, 재발 간격·최대 인구 조건·진입·합류·편지 처리를 유지한다. 원래 `StrangerInBlack` PawnKind 자체와 크립조이너 정의는 수정하지 않는다. 크립조이너는 사용자 지시대로 콘셉트 후속이다.

새 구조자는 검은 의복·리볼버·의료품 5개와 원래 식량을 가진 헤로드다. 현재 매켄지 계곡 계통을 사용하며 해당 유전자를 수정하지 않았다. 의료 훈련 12~16으로 기존 계통의 의료 약점을 보완하고, 폭력·돌봄이 가능한 배경을 요구한다. 나이는 20~45세, 노화 부상은 제외한다. BTX 의존을 위해 플레인 도그츄 15개를 휴대한다.

기존 헤로드 생성 보정이 PawnKind의 제노타입 설정에 앞서 다섯 계통을 무작위 지정하므로, `Patch_HelodPawnRequest`에서 이 새 PawnKind의 기본 요청에만 매켄지를 지정했다. 명시적인 헤로드/사용자 제노타입 요청과 다른 PawnKind의 생성 정책은 유지한다.

실제 1.7 `.codex-staging/StageK/events-mood-03`에서 네 번의 네이티브 `IncidentWorker_WandererJoin.GeneratePawn` 생성과 종족·생존·전투/돌봄 가능·의료/BTX 재고·계통을 확인했다. 이벤트 관련 19검사와 함께 담배 효과 검사 16개가 통과해 총 35 PASS·COMPLETE다. 이 검증은 실제 생성 경로이며 위기 상황을 발생시켜 원래 발동 시점과 편지 UX를 장시간 플레이한 것은 아니다.
