# 방탄 등급·소재 개발 메모

각 방탄판·방호 장구 Def의 `방탄 참고:` XML 주석은 다음 개발을 위한 추정 범위 또는 목표값이다. 현재 방호 수치·제작 재료를 바꾸거나 실물 인증을 선언하지 않는다. `hg1`, `hg2`, `rf1`, `rf2`, `rf3`는 NIJ 0101.07이 참조하는 NIJ 0123.00의 위협등급이다. `-hg1`은 HG1 미달로 취급하는 보수적인 개발 표기다. 전체 시험자료가 없는 항목은 주석에 추정 근거를 남긴다. [NIJ 위협등급 정의](https://nij.ojp.gov/topics/equipment-and-technology/specification-nij-ballistic-protection-levels-and-associated-test-threats-nij-standard-012300)

군용 SAPI 계열은 군 규격·리비전·백킹 조건을 확인해야 한다. 특정 탄환을 막았다는 자료만으로 NIJ 전체 시험 통과를 확정하지 않는다. 헬멧·바이저·방패에는 방탄복과 다른 시험 조건이 있으므로 여기서는 위협 수준을 비교한다. [NIJ 0101.07 적용 범위](https://nij.ojp.gov/topics/equipment-and-technology/ballistic-resistance-body-armor-nij-standard-010107)

## 등급별 게임 방호력 기준

| 등급 | 날카로움 방호 | 확정 방어 관통력 컷 |
| --- | --- | --- |
| HG1 | 70% | 13 |
| HG2 | 85% | 19 |
| RF1 | 120% | 33 |
| RF2 | 140% | 43 |
| RF3 | 165% | 50 |

위 값은 게임 밸런스 기준이며 실제 NIJ 시험 수치가 아니다. 방패의 확정 방어 컷은 HG2/RF3 기준을 적용한다. 나머지 장비의 등급별 날카로움 방호 및 플레이트 컷은 아직 기준표만 확정한 상태다. 범위 등급인 장비의 단일 적용 등급과 `-hg1` 장비의 수치는 추후 정한다.

확정 방어 관통력 컷은 기존 기준 `10 / 15 / 26 / 34 / 40`에 `5/4`를 곱한 뒤 정수로 올림한 값이다.

게임 내부 값으로 적용할 때 날카로움 방호는 각각 `0.70 / 0.85 / 1.20 / 1.40 / 1.65`이며, `guaranteedBlockPenetration`은 각각 `0.13 / 0.19 / 0.33 / 0.43 / 0.50`으로 환산한다. 관통력이 컷 이하일 때 확정 방어한다. 플레이트는 해당 방향 판의 내구도 40% 이상, 방패는 전체 내구도 75% 이상을 피격 직전에 확인한다. 방패에는 정면 방호각과 기존 원거리 날카로움 피해 조건도 적용한다. 확정 방어에 성공해도 장구 내구도는 감소하며, 조건을 벗어나면 기존 확률 방어를 사용한다.

## 플레이트 내구도 계산

플레이트 내구도 감소량은 `판에 도달한 데미지 × 공격 AP × 소재 계수 × 1.5`로 계산하고 기존 `GenMath.RoundRandom`으로 정수화한다. AP는 내부 비율값을 사용한다. 예를 들어 데미지 20, AP 30(내부값 0.30)이면 세라믹 판은 평균 9, UHMWPE 판은 평균 6.3, 방탄강 판은 평균 4.5의 내구도를 소모한다. 앞뒤/좌우 두 판의 내구도는 계속 독립적으로 저장한다.

| 게임 소재 | 내구도 감소 계수 |
| --- | --- |
| 세라믹 (`Ceramic`) | 1 |
| UHMWPE | 0.7 |
| 방탄강 (`BallisticSteel`) | 0.5 |
| 복합소재 (`Composite`) | 해당 판의 `compositeDurabilityCoefficient` 고유값 |

소재와 복합소재 계수는 실제 플레이트 아이템의 `CompProperties_ArmorPlateSet`에 지정한다. 기본 소재는 세라믹이며, 복합소재는 `material=Composite`와 `compositeDurabilityCoefficient`를 함께 설정한다. 현재 게임용 단순화 분류에 따라 SAPI/SSAPI/ESAPI/ESBI는 세라믹, RAMPART 4800은 UHMWPE를 사용하며 각 아이템 XML에 소재를 명시한다. 아래의 실제품 소재 자료는 이 게임용 분류와 별개로 보존한다.

사이드 판은 같은 계열 정면판 내구도의 75%로 설정한다. SAPI 32 / SSAPI 24, ESAPI 40 / ESBI 30이며 RAMPART 4800은 기존 56을 유지한다. 이전 저장 호환을 위한 비선택 측면 부품은 기존 아이템 참조를 유지한다. 방패의 내구도 소모는 기존 차단/관통 배율 계산을 유지한다.

## 플레이트·방패 등급·소재

| 항목 | 개발 참고 등급 | 구분한 소재 | 메모 |
| --- | --- | --- | --- |
| SAPI / SSAPI | rf1-rf2 | 세라믹 | 백킹은 아라미드/UHMWPE 중 제조사·리비전별 확인. SSAPI는 Side SAPI이며 XSAPI가 아니다. |
| ESAPI / ESBI | rf3 | 세라믹 | 타격면의 세라믹 종류와 아라미드/UHMWPE 백킹은 실모델별 확인. |
| RAMPART 4800 | rf3 | UHMWPE | 실제품: HESCO 4800 플레이트. 세라믹 세부 종류는 공개 자료에서 확정하지 않음. |
| 모델이 지정되지 않은 이전 저장용 판 | rf1-rf3 | 세라믹(세부 재질 미지정) | 타격면·섬유 백킹 미지정. |
| DefenTech IIIA 방패 | hg2 | UHMWPE | 실제품: PROTECH Intruder G2. 본체 PE 확인; UHMWPE 세부 등급·창 재질은 별도 확인. |
| Ironhide IV 방패 | rf3 | 세라믹 | 실제품: Point Blank SOB SS-4 NIJ Level IV Ballistic Shield and Carry System. 백킹·창의 세부 소재는 미공개. |

## 기타 장구 방탄 등급

| 항목 | 개발 참고 등급 |
| --- | --- |
| IBTV 본체·연질 확장 보호대 | hg2 |
| FAST MT | hg2 |
| 멀티히트 바이저 | hg2 |
| M1952A | -hg1 |
| 보강 직물 외투 | -hg1 |
| 가죽조끼 | -hg1 |
| 무릎 보호대 | -hg1 |
| M1 헬멧 / 금속조끼 | -hg1 |

소재 구분은 플레이트 특성과 향후 방패의 추가 연산을 위한 참고로 남긴다. 세라믹 타격면과 아라미드/UHMWPE 섬유 백킹은 함께 적고, `복합소재`는 조합 구조를 설명하는 말로 사용한다. 제조사·리비전·상표·섬유 등급이 확인되지 않으면 미지정 또는 추정으로 표시한다. 플레이트·방패 이외 장구의 XML 참고 주석에는 방탄 등급만 남긴다.

플레이트 백킹의 아라미드 방탄섬유는 파라아라미드 계열로 구분하며 Kevlar는 그 상표 예시다. Spectra는 UHMWPE 섬유다. 소재의 계열과 상표를 구분하고, 특정 플레이트 모델의 사용 상표가 확인되지 않으면 임의로 붙이지 않는다. [DuPont Kevlar 기술 자료](https://www.dupont.com/content/dam/aramids/amer/us/en/safety/public/documents/en/Kevlar_Technical_Guide_0319.pdf), [Spectra 제조사 자료](https://www.solstice.com/us/en/brands/spectra-polyethylene-fiber)

## 실제 플레이트·방패 소재 확인

| 게임 항목 | 대응 실제품 | 확인한 소재 | 미확인 범위·근거 |
| --- | --- | --- | --- |
| RAMPART 4800 방탄판 | HESCO 4800 | 세라믹 + Spectra(UHMWPE) | 제조사 사양의 세라믹/Spectra 조합과 섬유 제조사의 UHMWPE 설명을 연결. 세라믹 세부 종류·배합은 미공개. [HESCO 작성 4800 사양서](https://www.blackboxgroup.org/wp-content/uploads/2024/04/Hesco-4800-Tech-Sheet-231001.pdf), [Spectra 재질](https://www.solstice.com/us/en/brands/spectra-polyethylene-fiber) |
| DefenTech IIIA 방패 | PROTECH Intruder G2 | PE(폴리에틸렌) 본체 | 제조사 제품 페이지의 표현을 유지. UHMWPE 세부 등급·창의 재질은 별도 확인. [제조사 제품 자료](https://safariland.com/products/intruder-g2-e2-80-93-type-iiia-shield-2035g2) |
| Ironhide IV 방패 | Point Blank / Special Ops Bunker SOB SS-4 | 세라믹 본체 | 제조사는 본체를 세라믹 장갑으로 설명. 백킹·투명 창의 구체 재질은 공개하지 않으므로 PE/아라미드/유리로 확정하지 않음. [제조사 제품 자료](https://www.pointblankenterprises.com/sob/ss-4.html) |

SAPI는 M80, ESAPI는 APM2 위협 표기를 사용한다는 군 자료와 군용 삽입판의 세라믹 구조를 참고했다. 위 표의 새 NIJ 범위는 이를 바탕으로 한 개발 추정이다. [미 육군 삽입판 식별 자료](https://home.army.mil/jackson/7417/7930/3250/CIF_External_SOP_20260512.pdf), [DLA 방탄장구 구성](https://www.dla.mil/Portals/104/Documents/DispositionServices/ddsr/TurnIn/bodyarmor.html)

FAST MT의 공개 사양은 9mm·파편 시험을 제시한다. 바이저는 제조사의 별도 시험에서 9mm와 .44 위협을 제시한다. [FAST MT 사양](https://shop.gentexcorp.com/content/Ops-Core-FAST-MT-Super-High-Cut-Sell-Sheet.pdf), [멀티히트 바이저 사양](https://shop.gentexcorp.com/ops-core-multi-hit-handgun-face-shield/)

Intruder G2의 제조사 자료는 IIIA급을 명시한다. M1952의 역사 자료는 파편 방호 목적을 설명한다. [Intruder G2](https://safariland.com/products/intruder-g2-e2-80-93-type-iiia-shield-2035g2), [미 정부 방탄재 교육 자료](https://ojp.gov/pdffiles1/Digitization/60136NCJRS.pdf)

IBTV의 목은 본체 `apparel/bodyPartGroups/Neck`로 보호한다. 목 보호대는 슬롯·기본 구성·프리셋·제작 목록에서 제외했으며, 이전 저장의 Def 참조를 위해 비활성 정의만 남겼다.
