# HELLVADS 중장비 강하 아트

M8 AGS LEVEL 3, 기본형 M3A3, BUSK M3A3에 각각 맞춘 팔레트·완충 블록·고정끈·현수줄·낙하산 모델과 동서남북 텍스처다. 원본 차량의 메시와 UV는 변경하지 않고, 별도 루트에 균일 배율만 적용한다. 차량 자체는 장비 텍스처에 굽지 않는다.

`output/{m8,bradley,bradley_busk}/vehicle_drop_rig.blend`에서 실제 차량에 연결된 끈 모델을 확인할 수 있다. 팔레트와 고정끈은 착륙 후 차량 위에 남고, 현수줄과 낙하산은 강하 중에만 표시한다. `output/manifest.json`에는 잘라낸 각 레이어의 실제 타일 크기, 차량 중심 기준 오프셋, 파일 해시가 들어 있다.

장비는 절대 크기 기준 170.6667px/타일로 출력한다. 전체 등록 캔버스는 18타일, 최종 3072px이다. 팔레트·낙하산 외곽에는 최종 크기 기준 12px 검은 윤곽선을 넣는다. 가는 현수줄·고정끈은 실제 곡선 메시의 굵기를 사용하며 두꺼운 외곽선을 덧대지 않는다. 모든 재질은 고정 RGB의 Emission이고 조명·그림자·AO를 사용하지 않는다.

4개 낙하산과 36,000kg 한도, 키트 1,000kg은 게임용 시각화·밸런스 설정이다. 실제 강하 장비의 설계나 결속 절차를 재현한 문서가 아니다. 개념 참고: [미 육군 HELLVADS 공고](https://sam.gov/opp/c845b356f1684854a91c7525295840f6/view), [USTRANSCOM 대형 공중투하 플랫폼 자료](https://www.ustranscom.mil/cmd/associated/rdte/files/40%20Ft%20Aerial%20Delivery%20Platform.pdf).

재생성 순서:

```powershell
python ArtSource/HeavyAirdrop/configure.py
foreach ($variant in @('m8', 'bradley', 'bradley_busk')) {
  & 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 8 --python-exit-code 1 --python ArtSource/HeavyAirdrop/build.py -- $variant
}
python ArtSource/HeavyAirdrop/package.py
```

원본 차량 경로와 SHA256은 `settings.json`으로 고정한다. 빌드는 원본 메시 좌표·폴리곤·UV 해시가 유지되는지 확인하며, 낙하산 신규 면의 퇴화 여부도 검사한다. 패키징은 48개 런타임 레이어의 불투명 픽셀 존재와 캔버스 잘림 여부를 검사한다. 모델 전체 검수 이미지는 `output/rig_review.png`다.
