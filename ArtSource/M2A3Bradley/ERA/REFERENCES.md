# ERA 외형 재검토

## 직접 확인한 자료

1. [GD/Rafael 제조사 외형 자료 재게시본](https://forums.kitmaker.net/uploads/default/original/4X/b/1/5/b15d6f0edcae5801a5cedf4e70aa9e234bf65d74.jpeg)
   - 5종 케이스 외형과 차량 장착 사진. 18/8/55/7/8개, 합계 96개 구성 표기.
   - 동일한 거대 블록 두 줄 대신 작은 개별 케이스와 전면/측면/포탑 구성을 적용.
2. [미 육군 공식 사진](https://api.army.mil/e2/c/images/2013/02/27/283749/original.jpg), [사진 원문](https://www.army.mil/article/97332/savings_served_up_for_bradley_armor_plates)
   - 전면 경사판을 여러 줄로 덮는 배열, 헤드램프 주변 여유 공간, 하부 전면 노출 확인.
3. [미군 실차 정면·측면 사진 재게시본](https://en.defence-ua.com/media/contentimages/a9964bd6d35bcf81.jpg)
   - 측면 앞쪽의 낮은 단과 뒤쪽 높은 단, 노출된 원래 하부 스커트/바퀴,
     포탑 전방 블록과 외부 장비를 피하는 배치 확인. 텍스트 기사의 주장은 근거로 사용하지 않음.
4. [M5A2 케이스 후면 근접 사진](https://forums.kitmaker.net/uploads/default/original/4X/1/f/6/1f69ff60e06edec8a4c381d7b4e2d591ed835baa.jpeg)
   - 양쪽 후면 장착 채널, 상단 귀, 작은 체결부를 외관 디테일로 반영.
5. [TM 55-2350-252-14 수송 도면 스캔](https://forums.kitmaker.net/uploads/default/original/4X/5/2/2/5220163c80e8326fa3df0697c52be0f3ec78090a.jpeg)
   - 기본 차체 전면/측면 실루엣 확인용. 구형 M2/M3 수송 도면이므로 A3 ERA 설치 치수로 사용하지 않음.

## 한계 및 검증

- 전체 치수가 있는 A3 ERA 설치 청사진은 확보하지 못함. 포장용 도면은 설치 도면으로 취급하지 않음.
- 위 96개 구성에 맞춘 외형 모델이나 각 좌표, 케이스 치수 및 위치별 M형식 배정은 사진 기반 추정.
- 내부 구조/성능/실물 제작 사양은 모델링하지 않음.
- 모든 블록은 장갑 메시 레이캐스트로 표면에 맞추며 후면 채널 끝이 장갑에 0.005m 겹침.
  이는 게임 메시의 틈 방지 설정이지 실물 장착 치수가 아님.
- 차체 블록과 포탑 블록의 부모를 분리해 포탑 회전 시 장착 위치 유지.
- 기본형은 변경하지 않고 ERA 모델, 개별 GLB 및 east/등각 확인 렌더만 다시 생성.
