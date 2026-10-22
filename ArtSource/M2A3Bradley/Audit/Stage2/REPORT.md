# 2차 포탑 재구성

2026-10-22. 목표는 RimThunder의 식별 디테일을 한 면 한 색의 3D 형상으로 구현하는 것이다.
형상 수정은 포탑 계층에 한정했다. 차체 지붕·후방 해치·그릴은 3차, 최종 공통 팔레트는 4차 대상이다.

## 구현

- 1차의 `bradley_roles.json`을 실제 재생성 코드가 소비하도록 전환했다.
  포탑에서 크기만으로 작은 부품을 일괄 지우던 경로를 제거했다.
- 원본 포탑의 큰 장갑 쉘, 지붕 외곽, 회전링, 후방 벽/선반은 원본 면 집합으로 보존했다.
- 두 해치를 16분할의 얕은 뚜껑, 상면과 구분되는 테두리, 대표 손잡이·힌지로 대체했다.
- 포수 관측장비는 박스/광학창으로, 지휘관 관측장비는 본체/렌즈/외부 보호 구조로 구분했다.
- TOW를 본체, 쌍열 개구부, 고정 밴드, 마운트, 힌지, 연결부, 열린 끝단 덮개로 재구성했다.
- 비어 있던 `WT_roll_03`(TOW 연결부), `WT_ex_decor_02`(전방 상부 덮개)를 복구했다.
- 주포부 내부의 작은 기구와 잘린 판 대신 닫힌 단순 마운트/상단 덮개를 사용했다.
- 후방 수납함 5개는 포탑 외곽을 따라 회전된 열린 상자/바닥/벽/부착 브래킷으로 분리했다.
- 주포·동축기관총·안테나는 원본 끝점과 위치를 기준으로 단순한 원통으로 만들었다.

## 연막탄 배치 근거와 해석 범위

1차에서 미식별이던 연막탄은 후방 수납함과 별개의 장비로 구현했다.
장착 지역의 근거는 [부품 이름이 표시된 Bradley 포탑 참고도](https://afvdatabase.com/usa/pics/m2bradley/m2turret1-14.jpg)의
포탑 전방 양측 배치다. 브라우저로 그림의 라벨과 위치를 직접 확인했다.
좌우 각 4개 구성은 [미 육군 IN0503의 장비 설명 검색 결과](https://www.globalsecurity.org/military/library/policy/army/accp/in0503/ch1.htm)를 교차 참고했다.
원문 호스트는 접근 차단되어 본문 전체를 열지 못했으며, 개수 설명은 검색 결과에서 확인한 범위다.

실차 외관은 [DVIDS M3A3 사진](https://www.dvidshub.net/image/9212957/us-army-m3a3-bradley-cavalry-fighting-vehicle-test-fires-its-25-millimeter-chain-gun)과
[DVIDS M2A3 사진](https://www.dvidshub.net/image/8597646/1st-armored-division-selects-best-abrams-and-bradley-crews-day-6)도 브라우저로 확인했다.
사진의 장착 옵션과 가려진 부분 때문에 이들 사진만으로 정확한 A3 발사관 중심 좌표를 읽을 수는 없다.

따라서 **전방 양측 2×4라는 일반 배치를 참고해 원본 포탑 장갑면에 맞춘 아트 해석**이다.
추출 OBJ의 특정 메시를 발견했다고 주장하지 않으며, 정확한 실차 장착 치수도 아니다.
각 관의 베이스는 원본 장갑 쉘에 레이캐스트해 붙이고, 접점/법선/관 끝 좌표를 manifest에 기록했다.
포탑 뒤 5개 수납함을 발사기로 대체하지 않았다.

## 색과 비교

새 포탑은 임시 TAN 역할 팔레트를 사용한다. 무채색 비교판도 생성해 밝기만으로 부품 구분을 확인했다.
큰 지붕, 해치 상면, 테두리, 경사면, 장비 벽, 유리/개구부를 구분한다.
최종 팔레트 통일과 차체 대비 조정은 4차에서 수행한다.

[RimThunder / 1차 / 2차 포탑 비교판](turret_comparison.png)은 별도 포신 레이어를 제외하고 화면 폭을 280px로 맞췄다.
참고 모드의 픽셀은 게임 에셋으로 복사하지 않았으며 비교판에만 사용했다.

## 검증과 범위

- 포탑 3,654면, 전체 33,945면. 이 숫자는 결과 기록이며 감량 목표가 아니다.
- 1차 차체의 꼭짓점·면·부모 계층을 저장 모델과 비교해 동일함을 검증했다.
- 등록된 필수 포탑 역할이 모두 존재하고, 포탑의 빈 메시와 면적 0인 면은 없다.
- 해치 손잡이 2개, 후방 수납함 5개, 연막탄 관/개구부 각 8개를 확인했다.
- 연막탄 부착 베이스 8개가 장갑 접점에서 법선 방향으로 0.012 원본 단위 이내에 배치된다.
- 포탑·포신·출입문·램프 데모 애니메이션과 GLB의 4개 애니메이션 검증을 유지한다.
- EAST 분리 렌더, 혼합 투영 합성, 256px/512px 출력, 앞/뒤 등각 뷰를 확인했다.
- 차체의 기존 빈 메시 등은 3차 대상으로 남긴다. 모든 자기 교차를 완전 탐지했다는 뜻은 아니다.
- 게임 내 설치/표시 검수는 미실시다. 전체 차량 아트 완성이나 최종 스타일 승인을 의미하지 않는다.

## 재생성

`ReferenceFit/stage1_rimworld_base.blend`는 1차 작업본의 고정 입력이다.
이를 기준으로 포탑만 다시 만들기 때문에 빌드를 반복해도 차체가 누적 변형되지 않는다.
`build_stage1_roles.py`는 현재 역할 설정을 덮어쓰지 못하도록 동결했다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python ArtSource/M2A3Bradley/simplify_for_rimworld.py
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --python ArtSource/M2A3Bradley/validate_stage2_turret.py --python ArtSource/M2A3Bradley/RimWorld/validate.py --python ArtSource/M2A3Bradley/render_rimworld.py
& 'C:\Users\human\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' ArtSource/M2A3Bradley/export_bradley_lowres.py
& 'C:\Users\human\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' ArtSource/M2A3Bradley/Audit/compare_stage2_turret.py
```
