# HELLVADS 중장비 강하 아트

2026-10-25 LVADS 외형 수정: 기존 밝은 천의 폐쇄 돔을 올리브색 원형 화물 낙하산으로 바꾸고 중앙 벤트, 32개 방사형 보강선과 원주 보강 띠를 추가했다. Type V 계열을 참고한 금속 데크·측면 레일·결속 슬롯·끝단 부재를 사용하며, 크라프트색 층상 허니컴 완충재는 차량 궤도와 팔레트 사이에 배치했다. 4개 낙하산과 기존 게임 중량·크기는 유지한다. 실제 장비의 정확한 결속·치수·인증 형식을 복제한 모델은 아니다.

외형 참고: [미 육군 LVADS 화물 낙하산 시험 사진](https://www.army.mil/article/230124/82nd_airborne_soldiers_test_forklift_after_cargo_aircraft_airdrop), [ALVADS/G-16 시험 자료](https://www.army.mil/article/188703/airborne_testers_ensure_army_equipment_survivability_when_dropped_from_above), [차량과 금속 팔레트 사이 허니컴 완충재 설명](https://www.army.mil/article/264240/us_army_tests_cutting_edge_parachute_system). 새 RRDAS의 에어백 방식과 기존 LVADS의 허니컴 방식을 혼합하지 않았다.

M8 AGS LEVEL 3, 기본형 M3A3, BUSK M3A3에 각각 맞춘 팔레트·완충 블록·고정끈·현수줄·낙하산 모델과 동서남북 텍스처다. 원본 차량의 메시와 UV는 변경하지 않고, 별도 루트에 균일 배율만 적용한다. 차량 자체는 장비 텍스처에 굽지 않는다.

`output/{m8,bradley,bradley_busk}/vehicle_drop_rig.blend`에서 실제 차량에 연결된 끈 모델을 확인할 수 있다. 팔레트와 고정끈은 착륙 후 차량 위에 남고, 현수줄과 낙하산은 강하 중에만 표시한다. `output/manifest.json`에는 잘라낸 각 레이어의 실제 타일 크기, 차량 중심 기준 오프셋, 파일 해시가 들어 있다.

장비는 절대 크기 기준 170.6667px/타일로 출력한다. 전체 등록 캔버스는 18타일, 최종 3072px이다. 팔레트·낙하산 외곽에는 최종 크기 기준 12px 검은 윤곽선을 넣는다. 가는 현수줄·고정끈은 실제 곡선 메시의 굵기를 사용하며 두꺼운 외곽선을 덧대지 않는다. 모든 재질은 고정 RGB의 Emission이고 조명·그림자·AO를 사용하지 않는다.

4개 낙하산과 총중량 30,000kg 한도는 게임 밸런스 설정이다. 팔레트 700kg과 낙하산 묶음 300kg은 별도 제작·회수 아이템이며 브래들리 기본 중량은 23,000kg이다. 개념 참고: [미 육군 HELLVADS 공고](https://sam.gov/opp/c845b356f1684854a91c7525295840f6/view), [USTRANSCOM 대형 공중투하 플랫폼 자료](https://www.ustranscom.mil/cmd/associated/rdte/files/40%20Ft%20Aerial%20Delivery%20Platform.pdf).

차량은 강하 준비 중 무장을 전방으로 돌리고 TOW·장전 해치를 접는다. 준비 중/완료 상태 모두 새 탑승과 이동·발사를 차단하며 정렬이 끝나야 적재할 수 있다. 강하 연출은 기체 꼬리 아래에서 출발하고, 기체 방향을 유지한 채 수평 관성이 줄어든 뒤 수직으로 내려온다. 현수줄은 정적 텍스처 대신 팔레트 고정점과 움직이는 낙하산 림 사이를 매 프레임 연결한다. `output/cord_anchors.json`에 12개 방향별 등록 좌표를 기록한다.

재생성 순서:

```powershell
python ArtSource/HeavyAirdrop/configure.py
foreach ($variant in @('m8', 'bradley', 'bradley_busk')) {
  & 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/HeavyAirdrop/build.py -- $variant
}
python ArtSource/HeavyAirdrop/package.py
python ArtSource/HeavyAirdrop/configure_gameplay.py --mod-root ../Helodrace-LobosArsenal
```

원본 차량 경로와 SHA256은 `settings.json`으로 고정한다. 빌드는 원본 메시 좌표·폴리곤·UV 해시가 유지되는지 확인하며, 낙하산 신규 면의 퇴화 여부도 검사한다. 패키징은 48개 런타임 레이어의 불투명 픽셀 존재와 캔버스 잘림 여부를 검사한다. 모델 전체 검수 이미지는 `output/rig_review.png`다.

모드에 레이어를 가져온 뒤 `configure_gameplay.py`를 실행하여 별도 장비 정의·아이콘·번역·차체 중량과 낙하산 현수줄 좌표를 갱신한다. 실제 게임 렌더러의 새 강하 검수 이미지는 `output/descent_review.png`, 검증 기록은 `output/descent_validation.txt`다.
