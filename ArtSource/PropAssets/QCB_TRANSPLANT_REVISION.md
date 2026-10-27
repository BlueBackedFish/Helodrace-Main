# M2HB QCB 외형과 CROWS II 무기 이식

기준 커밋은 `965c198b`이다. M2HB를 QCB 외형으로 조금 바꾸고 그 M2HB 모델을 CROWS II에 이식했다.

[FN Herstal 공식 제품 페이지](https://fnherstal.com/en/defence/portable-weapons/fn-m2hb-qcb/)와 [공식 측면 사진](https://fnherstal.com/app/uploads/FN_M2HB_QCB_Mk2.png)의 총열 교환용 손잡이·기부 결합부 외형을 참고했다. 기존 총몸·방열구멍·총열·총구를 유지하고 접힌 손잡이·결합 칼라·잠금부를 별도 시각 메시 8개로 추가했다. 게임용 스타일 표현이며 외형 변경은 이 부분으로 한정했다. `HeavyMachineGuns/qcb_design.json`이 형상 설정, `qcb_model.py`가 추가 메시 빌더다.

지상용 모델이 유일한 무기 원본이다. CROWS II 빌더는 지상용 QCB 모델에서 총몸·총열과 QCB 부품을 가져온다. 지상용 삼각대·탄통·탄띠를 제외하고, CROWS 원본의 받침·회전 프레임·탄통·급탄 슈트·센서 장비를 유지한다. 기존 총열 축·총구 위치에 맞춘 단일 균일 배율과 이동을 적용하며 총을 늘이거나 줄여 변형하지 않는다. CROWS 원본에 있던 `.50CAL`, `.50CAL SLEEVE`, `.50CAL BARREL` 세 메시는 제거했고 이전 전용 채색 매핑·프로파일도 제거했다.

검증 결과:

- 지상용 원본 모델의 면·정점·UV, 기존 피벗·표시 배율·탄통·탄띠를 대조했다. 추가 부품은 `art_addition='QCB visual kit'`로 분리했다. 손잡이·결합부는 반동하는 총열 레이어, 고정 잠금부는 총몸 레이어다.
- `CROWSII/validate_donor.py`에서 20개 이식 메시의 정점 21,408개, 면 23,755개, UV 루프 87,329개와 모든 면의 Emission 색이 지상용 원본과 동일함을 확인했다. QCB 부품 8개도 포함한다. 이식 전 좌표계를 평가한 뒤 장착 변환을 적용해 추가 부품의 원래 위치를 유지한다.
- `review_m2_detail.py`로 실제 마스터/1024px 출력에 방열구멍의 외피·내부, 총열 기부·단차·가는 구간·총구·QCB 금속 부속의 색이 나오는지 확인했다. 형상 변경을 요청받은 총 레이어의 알파는 달라지고, CROWS 프레임/받침·지상용 삼각대·M1917의 픽셀은 그대로다.
- 4방향 렌더와 동일한 월드 배율의 `output/qcb_transplant_preview.png`에서 손잡이·급탄부·장착 상태를 시각 검수했다. 기존 외곽선 0.026칸, 1024px 게임용 해상도, 카메라·CROWS 회전축을 유지했다. 작은 부속은 축소 시 일부 합쳐진다.

원본 비교 결과는 `CROWSII/output/donor_validation.json`, 출력 검수는 `output/m2_detail_validation.json`에 있다. `output/m2_detail_comparison.png`는 QCB 전후 비교다. M2HB 교체 텍스처는 설치 모드에 백업 후 반영한다. CROWS는 기존 아트 자료 범위이며 새 게임 정의는 추가하지 않았다. 실제 게임 실행·사격 검증은 하지 않았다.

재생성은 `HeavyMachineGuns/build.py -- --kind M2HB` → `CROWSII/build.py` 순서다. 두 모델·출력을 갱신한 뒤 각 모델 검증과 `review_m2_detail.py`를 실행한다. 상세 명령은 두 자산 README에 있다.
