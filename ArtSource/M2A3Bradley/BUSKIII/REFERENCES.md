# BUSK III 외형 시안의 자료와 확실성

확인일: 2026-10-23. 첨부 문서의 문구는 참고 데이터로만 사용했다.

## 확인한 자료

| 자료 | 확인한 내용 | 이 자료로 확정하지 않은 내용 |
|---|---|---|
| [미 육군 BUSK 설치 기사](https://www.army.mil/article/82060/bradley_urban_survivability_kits_installed_early_under_budget) | BUSK III는 연료·좌석·포탑 생존성·비상 램프 관련 개수도 포함한다. 외부 ERA와 같은 개념으로 취급하지 않는다. | 내부 개수 형상. 이번 게임 모델은 외장만 표현한다. |
| [GD / Rafael BRAT 제품 설명서의 공개 재게시본](https://forums.kitmaker.net/uploads/default/original/4X/b/1/5/b15d6f0edcae5801a5cedf4e70aa9e234bf65d74.jpeg) | M3 18개, M4 8개, M5 55개, M6 7개, M7 8개, 합계 96개. 장착 사진과 케이스 외관. | 개별 케이스 치수, 모든 번호의 실차 위치 대응표. 제조사 원본 PDF는 이번에 열람하지 못했다. |
| [MENG SS-004 조립 설명서](https://d37tikmwcb2tc.cloudfront.net/plastic_models/file_attachment/239246-21-instructions.pdf) | PDF 19~20쪽의 차체 외장·전조등 장착대, 23쪽 단계 52~54의 포탑 외장·차장 보호 구조. Q1 하부 스커트의 세로 구획 14개와 가로 중앙 분할. | 실차 청사진/공차/절대 치수. 이 문서는 축소 모형 조립도다. 14구획을 실차 XM33 탄약 개수로 해석하지 않는다. |
| [MENG SS-006 외관 삼면도](https://d2ev13g7cze5ka.cloudfront.net/men/menss-006_11_1433397234.jpg?v=1433397234) | 전면·측면·후면의 외장 실루엣 교차 확인. | 해상도가 낮아 상세 치수 산출에 사용하지 않았다. |
| [SS-004 제품 치수 게시](https://kingshobby.com/store/m2a3-bradley-wbusk-iii-us-infantry-fighting-vehicle-meng-ss-004) | 1:35 모형 폭 107.3mm를 임시 외장 폭 기준으로 사용했다. | 실차 폭을 실측한 자료가 아니다. 환산값 3.7555m는 초안 입력값이다. |
| [실차 외관 사진 1](https://forum-ru-cdn.warthunder.com/original/3X/4/e/4ed237e8f0a66aae23ff681646b81fc41e5fd50d.jpeg), [사진 2](https://forum-ru-cdn.warthunder.com/original/3X/7/b/7b6d8ba3d9242bc555afeba3c97795ebfc32f066.jpeg) | 차체 측면의 상·하부 장갑, 전조등 상승, 포탑 외장 배치를 시각적으로 대조했다. | 출처가 재게시 사진이므로 장착 키트의 세부 형식과 치수는 확정하지 않았다. |

## 현재 모델 수량과 치수의 의미

상부 **제조사 설명서에 96개 제품 구성이 실려 있는 것은 확인했다.** 이것만으로 모든 BUSK III 차량의 수량을 확정할 수는 없다. [BRAT 기술교범 서지 정보](https://www.armyproperty.com/tm/9-1375-217-14P)에는 M3~M7, A1, A2 형식의 서로 다른 세트가 나열된다. 교범 본문은 확보하지 못했으며, 번호별 위치 매핑과 목표 BUSK III의 정확한 세트 구성은 미확인이다. 현재 96개는 이 초안에서 선택한 제품 구성이다.

추가 교차 검색에서 [FAS의 2000년 Bradley 개요](https://man.fas.org/dod-101/sys/land/m2.htm)는 A1형 105개 구성을 설명했다. 이차 자료이므로 이 숫자를 곧바로 모델에 적용하지 않았다. 형식별 차이를 경고하는 근거로만 사용하고, 서로 다른 세트의 수량을 섞어 만들지 않았다. 목표 BUSK III 세트의 수량 검증은 남아 있다.

하부는 좌우 각각 14구획, 합계 28개의 탈착 가능한 **외장 스커트 메시**다. 중앙 분할선까지 같은 메시 안에 포함했다. 이 28개는 반응장갑 탄약의 실차 구성 수량이라는 뜻이 아니다. 상부 BRAT 96개와도 별도로 센다.

치수가 기입된 실차 BUSK 장착 청사진은 확보하지 못했다. 현재 케이스 폭·높이·두께, 받침 길이, 전조등 상승량, 보호창 크기는 원본 형상과 외관 자료에 맞춘 게임용 추정치다. `settings.json`과 `output/manifest.json`에 미확인 상태를 유지한다. 포장용 팔레트의 수량·치수를 장착 수량·장갑 치수로 전용하지 않았다.

실차 도면을 받으면 우선 수정할 항목은 번호별 장착 위치, 각 케이스 외형/치수, 하부 구획과 실제 모듈의 대응, 차장 보호 구조, 장착대 돌출 거리다. 현재 파일을 **실차 청사진 치수까지 일치한 완성본으로 사용하면 안 된다.**
