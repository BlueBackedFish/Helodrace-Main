# T2-18 맨인블랙 대체

2026-11-03. Main을 사용하는 게임에서 `StrangerInBlackJoin`의 `pawnKind` 하나만 `HD_RescuerInBlack`으로 교체한다. 원래 `IncidentWorker_WandererJoin`, 위기 호출 조건, 재발 간격·최대 인구 조건·진입·합류·편지 처리를 유지한다. 원래 `StrangerInBlack` PawnKind 자체와 크립조이너 정의는 수정하지 않는다. 크립조이너는 사용자 지시대로 콘셉트 후속이다.

새 구조자는 서부극풍 의상·Ironfang SAA·의료품 5개와 원래 식량을 가진 헤로드다. 현재 매켄지 계곡 계통을 사용하며 해당 유전자를 수정하지 않았다. 의료 훈련 12~16으로 기존 계통의 의료 약점을 보완하고, 폭력·돌봄이 가능한 배경을 요구한다. 나이는 20~45세, 노화 부상은 제외한다. BTX 의존을 위해 플레인 도그츄 15개를 휴대한다.

2026-11-04 후속 요청으로 무작위 리볼버·검은 의복을 교체했다. 헤로드 전용 카우보이 모자는 황갈색 RGB(156,117,68), 천 셔츠는 크림색 (225,210,175), 천 바지는 짙은 회갈색 (57,52,44), 일반 가죽 더스터는 갈색 (112,74,43)으로 고정한다. 현대 방탄조끼는 제거하고 네 벌만 지정한다. native `specificApparelRequirements`로 품질 Good과 개별 색상을 고정하며, SAA만 가진 전용 생성 태그를 사용한다. 다른 SAA 사용자나 무기의 기본 수치는 바꾸지 않는다.

전용 성인 백스토리 **변방을 떠도는 총잡이**는 외딴 마을을 떠돌며 분쟁을 해결하고 부상자를 치료해 온 인물이다. 사격 +6, 의료 +3, 사교 +1을 부여한다. 이 배경에서만 강제 부여되는 **총잡이 / gunslinger** 특성은 조준 준비 시간 ×0.8(기존의 80%), 원거리 발사 후 쿨다운 ×0.85(기존의 85%)로 적용한다. 일반 무작위 특성/백스토리 선택에는 등장하지 않는다. 현재 엔진 BackstoryDef에는 직접 statFactors 필드가 없으므로 native forcedTraits와 TraitDef statFactors를 통해 효과를 연결했다. 실제 `VerbProperties.AdjustedCooldown`이 RangedCooldownFactor를 곱하는 것을 확인했다. [후속 권총·패닝 조정과 실제 검증](Gunslinger_Pistol_Balance.md).

기존 헤로드 생성 보정이 PawnKind의 제노타입 설정에 앞서 다섯 계통을 무작위 지정하므로, `Patch_HelodPawnRequest`에서 이 새 PawnKind의 기본 요청에만 매켄지를 지정했다. 명시적인 헤로드/사용자 제노타입 요청과 다른 PawnKind의 생성 정책은 유지한다.

실제 1.7 `.codex-staging/StageK/events-mood-03`에서 네 번의 네이티브 `IncidentWorker_WandererJoin.GeneratePawn` 생성과 종족·생존·전투/돌봄 가능·의료/BTX 재고·계통을 확인했다. 이벤트 관련 19검사와 함께 담배 효과 검사 16개가 통과해 총 35 PASS·COMPLETE다. 이 검증은 실제 생성 경로이며 위기 상황을 발생시켜 원래 발동 시점과 편지 UX를 장시간 플레이한 것은 아니다.

후속 실제 1.7 `.codex-staging/StageKFollowup/cowboy-03`: **95 PASS, COMPLETE**. 네 명 모두 실제 SAA, 정확히 네 벌의 의상과 각 색상, 고정 성인 배경·강제 특성, trait 유무에 따른 native 웜업/쿨다운 비율 및 실제 SAA AdjustedCooldown 계산을 확인했다. 초기 01/02는 native의 착용 목록과 개별 재료·색상 조건 구분, 세력 재료 제한 때문에 실패했다. 최종 정의는 고정 착용 목록과 개별 요구 조건을 함께 쓰고 이 구조자만 세력 재료 제한을 무시한다. 외형의 실제 스크린샷 검수나 신규 위기 발동 조건 검사는 아니다.
