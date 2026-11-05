# 텍스트 검수 결과 (2026-11-05)

기준: [텍스트 검사 규칙](TextReviewRules.md). 대상: Helodrace-Main, Helodrace-blancasdrugs(1.6·1.7 폴더), Helodrace-LobosArsenal(1.6). `PawnKindDef`와 군용 태블릿은 제외했다. 동기화된 원문·번역 대조표는 각 저장소 루트의 `번역_원문_대조.md`에 있다.

## 1. 언어 결함 처리

| 저장소 | 처리 | 건수 |
|---|---|---|
| Main | Defs의 한국어 원문을 영어로 교체하고 한국어를 DefInjected로 이동 (작업 문구 19, 습격 전략 3, 포박 3, 지원탄 1, 이름 규칙 팩 1) | 27 |
| Main | 원문만 한국어였고 같은 번역이 이미 있던 항목: 원문만 영어로 교체 (`HD_HelodCivilHighFaction.label` → Vesenica State, `HD_RawBTXMadman` → madman) | 2 |
| Main | C# 표시 문자열(`ModMain.cs` 설정 화면 2개)을 Keyed `HD_Settings_LimitToOnePlatoon(_Desc)`와 `Translate()`로 교체 | 2 |
| Main | 한국어 Keyed에만 있던 키의 영어 원문 추가 (기계 동력망·무반동포·M6 가방·의료 가방) | 44 |
| Main | 영어 원문의 `{0}`이 빠진 한국어 Keyed 수정 (`HD_Company_ArmsDesc`, `HD_Company_PetroleumDesc`) | 2 |
| Main | 한국어 번역 누락 추가 | 38 |
| Main | 리스트 위치가 바뀐 키 이동 (`HD_DistillationTower.comps.2` → `comps.3`) | 3 |
| Main | 영어 원문이 Def에 없던 로터리 시추기 연료 문구 추가. 한국어 `경유`는 아이템명 `디젤`로 통일 | 3 |
| Main | 삭제된 Def를 가리키던 번역 키 삭제 (BTXAddiction, HD_MakeSlush_*, HD_CNGas, HD_CSGas, HD_PhotochlorogenGas, HD_SweetGas, MK14 소켓 2·3) | 18 |
| blancasdrugs | `Languages/English/DefInjected` 삭제(원문은 Defs가 기준). 값이 달랐던 연구 설명 4개는 영어 DefInjected 문장을 Defs로 옮김(한국어 번역과 같은 내용) | 2벌 |
| blancasdrugs | 경로가 틀린 섭취 문구 키 수정 (`BD_X.ingestCommandString` → `BD_X.ingestible.ingestCommandString`) | 28×2 |
| blancasdrugs | 없는 Def를 가리키던 키 삭제 (OpioidsRecovery, Opioidsstronghold, KetamineRecovery, Ketaminestronghold, BlancaResearchTab) | 13×2 |
| blancasdrugs | 비어 있던 `BD_AmbienSleep.description` 영어·한국어 추가 | 3 |
| LobosArsenal | `fuelLabel`·`fuelGizmoLabel`에 Keyed 키 이름이 원문으로 들어가 화면에 키가 그대로 보이던 문제 수정. Def에 영어를 쓰고 한국어 DefInjected 추가, 쓰이지 않게 된 Keyed 2개 삭제 | 2 |
| LobosArsenal | 한국어 번역 누락 추가 (항공기·폭탄·작업·포탑 라벨) | 30 |

이름 규칙 팩은 한국어 표기에서 영어 이름을 역으로 복원했다. `챔드윙`→Chadwick, `캐머`→Cammer, `로스트`→Lost, `파튼`→Parton, `바이네스`→Baines는 한국어 표기에서 복원한 철자다. 영미권 이름이므로 영어가 원문이다.

## 2. 현실 명칭 잔존

| 대상 | 남은 현실 명칭 | 대응 가상 명칭 | 매핑 신뢰도 | 수정 필요 여부 | 비고 |
|---|---|---|---|---|---|
| HD_Apparel_ShieldDefenTechIIIA.description | Intruder G2 | DefenTech(디펜테크) | 확정 | 수정함 | 규칙 문서의 예시와 동일. 한국어에는 남아 있지 않았음 |
| HD_ModularPart_BufferTube_Colt.label | Colt | Ironfang Arms(아이언팽) | 확정 | 수정함 | SAA·M1860 Army·M1911이 모두 아이언팽 표기 |
| HD_ModularPart_BufferTube_ColtA2_Part.label | Colt | Ironfang Arms(아이언팽) | 확정 | 수정함 | 같은 근거 |
| HD_ModularPart_BufferTube_ColtA2_Part.description | Colt | Ironfang Arms(아이언팽) | 확정 | 수정함 | 같은 근거 |
| HD_Make_Gun_KragJorgensen_Weapon.label (한국어) | 크라그-요르겐센 | 그레이록 M1892 / 베르그-라센 | 확정 | 수정함 | 총기 이름·설명이 그레이록 조병창의 베르그-라센 소총 |
| HD_Gun_Henry_Weapon.label | Henry | (설계자 이름 계열) | 보류 | 수정 안 함 | 설계자 이름이 붙은 총기명. 제조사는 이미 Frontier Arms로 표기 |
| HD_Gun_Glock19Gen5_Weapon | Glock | (치환 근거 없음) | 보류 | 수정 안 함 | 가상 명칭 치환 사례가 없어 A 항목 대상 아님 |
| HD_ModularP320FluxPartsBox 등 Flux Raider 계열 | Flux Raider | (치환 근거 없음) | 보류 | 수정 안 함 | 가상 명칭 치환 사례가 없음 |

### 명칭 대응 근거

| 현실 명칭 | 가상 명칭 | 신뢰도 | 근거 |
|---|---|---|---|
| Colt | Ironfang Arms / 아이언팽 | 확정 | SAA, M1860 Army, M1911 |
| Springfield Armory | Graylock (Arsenal) / 그레이록 | 확정 | M1861, M1873, M1892, M1903 |
| Winchester | Frontier Arms / 프론티어 암즈 | 확정 | M1887, M1895, M1897, Henry 소총 제조사 |
| Browning (설계자) | Hartmann / 하트먼 | 확정 | M1919 원설계, M1911 설계자(한국어 설명) |
| Garand (설계자) | Hartwell / 하츠웰 | 유력 | M1, M1D 총기명 |
| Thompson | Harrington / 헤링턴 | 유력 | M1A1 총기명 한 종류만 확인 |
| Pedersen (설계자) / Remington | Whitmore / 휘트모어 | 유력 | M1903 Whitmore device. 총포상 이름도 Whitmore Arms |
| Krag–Jørgensen | Berg-Larsen / 베르그-라센 | 확정 | M1892 설명, 제작법 이름 |
| Smith & Wesson | Wolfe & Mansion (W&M) / 울프앤 맨션 | 유력 | Model 3. 대표 표기 W&M, 비고: Wolfe & Mansion |
| Stokes (박격포) | Hawkins / 호킨스 | 유력 | 3인치 박격포·포탄 3종 |
| Daniel Defense | Dalton Defense | 가능성 있음 | 수직 전방손잡이 1종 |

## 3. 설명문 검수

한국어 설명 대부분은 이전 정리를 거쳐 메타 설명이 빠져 있었고, 영어 원문에만 판정·조작 문장이 남아 있었다. 규칙 2절에 따라 한국어 기준으로 판정하고 영어 원문에 반영했다. `상태`가 `보류`인 항목은 정보 손실이나 설정 판단이 걸려 있어 수정하지 않고 기록만 했다.

| ID | 대상 | 원문 | 판정 | 문제 유형 | 문제 구간 | 현실 고유명사 | 삭제 가능 내용 | 보존할 핵심 정보 | 재작성 방향 | 상태 |
|---|---|---|---|---|---|---|---|---|---|---|
| T001 | Main `HD_PowerCutter.description` | A heavy cordless cut-off saw designed to slice through masonry, metal… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | While equipped, it increases construction speed by 25%. |  | 건설 속도 보정 문장 | 석재·금속 절단 공구, 비상 근접 무기 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T002 | Main `HD_PowerforgeJackhammer.description` | A heavy battery-powered demolition tool built around a hardened recip… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | While equipped, it increases mining speed by 35%… |  | 채굴·건설 속도 보정 문장 | 왕복 비트 철거 공구, 비상 타격 무기 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T003 | Main `HD_Gun_Nailgun_Weapon.description` | A rugged cordless nailgun intended for rapid assembly work. It can la… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | While equipped, it increases general labor speed… |  | 작업 속도 보정 문장 | 무선 네일건, 비상시 못 발사 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T004 | Main `HD_Gun_M16A4_Weapon.description` | A modern service rifle chambered for a small-caliber, high-velocity c… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | trade eight tiles of effective range |  | 명사수 사거리 교환 판정 | 소구경 고속탄, 3점사, 빠른 지향 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T005 | Main `HD_Gun_M16A1_Weapon.description` | An early lightweight 5.56 mm service rifle with full-automatic fire. … | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | trade eight tiles of effective range |  | 명사수 사거리 교환 판정(영·한) | 초기형 경량 5.56mm, 완전자동 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T006 | Main `HD_Gun_M16A2_Weapon.description` | A refined 5.56 mm service rifle with improved sights, heavier furnitu… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | trade eight tiles of effective range |  | 명사수 사거리 교환 판정(영·한) | 개선 조준기, 3점사 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T007 | Main `HD_Gun_M16A3_Weapon.description` | A full-automatic derivative of the M16A2 intended for sustained close… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | trade eight tiles of effective range |  | 명사수 사거리 교환 판정(영·한) | M16A2 완전자동 파생형 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T008 | Main `HD_Gun_M60E3_Weapon.description` | A stripped-down lightweight modernization of the belt-fed M60, fitted… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | A Helod sharpshooter can trade effective range… |  | 명사수 판정 문장 | 양각대 없는 경량 M60, 전방 손잡이, 개량 총열 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T009 | Main `HD_Building_M167VADS.description` | A towed 20 mm Vulcan air defense system with a 500-round ammunition d… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It must be directly operated by a pawn. |  | 조작 문장 | 견인식 20mm 벌컨, 500발 드럼 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T010 | Main `HD_Building_M1917HMG.description` | A tripod-mounted, water-cooled heavy machine gun with a 250-round bel… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It must be directly operated by a pawn. |  | 조작 문장 | 삼각대 수랭식, 250발 탄띠 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T011 | Main `HD_Building_M2HB.description` | A pedestal-mounted .50 caliber heavy machine gun fed from a 100-round… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It must be directly operated by a pawn. |  | 조작 문장 | 거치식 .50구경, 100발 탄띠 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T012 | Main `HD_Building_3InchHawkinsMortar.description` | A crew-served 3-inch trench mortar for lobbing high-explosive shells … | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It must be directly operated by a pawn… |  | 조작·재장전 문장(영·한) | 다인 운용 3인치 참호 박격포 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T013 | Main `HD_Building_M1_81mmMortar.description` | A crew-served 81mm infantry mortar for dropping M43 high-explosive sh… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It must be directly operated by a pawn… |  | 조작·재장전 문장 | 81mm 보병 박격포, M43 고폭탄 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T014 | Main `HD_Building_M1916_37mmGun.description` | A crew-served 37mm direct-fire infantry gun. Its low mount is treated… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | treated as a transparent base |  | 그래픽 구현 설명 | 37mm 직사 보병포, 낮은 포가 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T015 | Main `HD_Building_M2A2LightHowitzer.description` | A 105mm field howitzer on a split-trail carriage. Its chief of sectio… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | All required roles must be staffed before it can fire. |  | 포반 역할별 판정 문장 | 105mm 개각식 곡사포, 포반 구성, M1 고폭탄 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T016 | Main `HD_Building_M114Howitzer.description` | A medium 155mm field howitzer on a split-trail carriage. Its section … | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | must all be at their stations before it can fire |  | 포반 배치 판정 문장 | 155mm 개각식 곡사포, 전용탄 종류 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T017 | Main `HD_Apparel_ShieldIronHideIV.description` | A wheeled, full-height class IV ballistic shield fitted with a transp… | 재작성 권장 | 메타 설명; 과도한 사족; 언어 위치 오류(원문·번역 불일치) | Attacks from the sides or rear bypass the shield entirely. |  | 측후방 판정·명중 보정 문장 | 바퀴식 전신 IV급 방패, 관측창, 소총 거치대 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T018 | Main `HD_ANFOBlastingCharge.description` | A prepared ammonium nitrate and diesel blasting charge. It can be fir… | 재작성 권장 | 메타 설명; 과도한 사족; 언어 위치 오류(원문·번역 불일치) | within 50 cells … level 12 miner |  | 거리·채굴 판정 수치 | 질산암모늄·디젤 폭약, 원격 발파, 습기에 약함 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T019 | Main `HD_ANFODetonator.description` | A hand-operated blasting machine wired to every friendly ANFO charge … | 재작성 권장 | 메타 설명; 불필요한 사용법; 언어 위치 오류(원문·번역 불일치) | Select it and use the detonation command |  | 조작 안내 | 연결 폭약 일괄 점화 장치 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T020 | Main `HD_Building_SweetGasCan.description` | A temporary chemical canister using the photochlorogen can body. When… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | if Odyssey is installed, vacuum resistance reduces exposure |  | DLC·저항 판정 문장 | 점화식 화학 캔, 지연성 수포 작용제 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T021 | Main `HD_ContinentalPetroleumBond.description` | An official corporate debt certificate issued by the Continental Petr… | 경미한 수정 | 카탈로그식 문체; 언어 위치 오류(원문·번역 불일치) | It serves as highly liquid industrial wealth storage. |  | 용도 설명 | 액면 100 스탈레르 기업 채권, 석유 시황 연동 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T022 | Main `HD_Bugle.description` | A compact brass signal horn carried as utility gear. It can be worn i… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | worn in the utility slot like a psychic shock lance |  | 장착 슬롯 설명 | 신호용 소형 나팔 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T023 | Main `HD_Apparel_IZLIDUltra.description` | A compact long-range infrared laser marker. It gives an SCR-300 opera… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | gives an SCR-300 operator access to laser-guided CAS |  | 기능 해금 문장 | 소형 장거리 적외선 표적지시기 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T024 | Main `HD_Apparel_LA16uPEQ.description` | A pistol-form laser target marker designed for rapid field use. It en… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | It enables laser-guided CAS with less bulk |  | 기능 해금 문장 | 권총형 표적지시기, 악천후 취약 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T025 | Main `HD_Apparel_ZaperX26_Device.description` | A compact electroshock sidearm carried as auxiliary equipment. Its de… | 경미한 수정 | 메타 설명; 언어 위치 오류(원문·번역 불일치) | without replacing the wearer's primary weapon |  | 장비 슬롯 판정 | 유선 탐침·접촉 전극 전기충격기 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T026 | Main `HD_PrimaryCell.description` | battary | 경미한 수정 | 기타; 언어 위치 오류(원문·번역 불일치) | battary |  |  | 화학 반응 일회용 전지 | 게임 판정 문장 삭제, 이미 정리된 한국어 번역과 같은 내용으로 영어 원문을 맞춤 | 적용 |
| T027 | Main `HD_Apparel_ShieldDefenTechIIIA.description` | A lightweight class IIIA handheld ballistic shield inspired by the co… | 재작성 권장 | 현실 고유명사 잔존; 과도한 성능 비교; 메타 설명 | inspired by the compact Intruder G2 profile | Intruder G2 | IV급 방패 비교, 측후방 판정, 권장 무기 안내 | 디펜테크 경량 IIIA 방패, 넓은 관측창, 가벼움 | 현실 모델명·비교·판정 삭제, 한국어 번역 내용으로 맞춤 | 적용 |
| T028 | Main `HD_ModularPart_BufferTube_Colt.label` | Colt buffer tube | 경미한 수정 | 현실 고유명사 잔존 | Colt | Colt |  | 버퍼 튜브 | Colt → Ironfang(아이언팽) | 적용 |
| T029 | Main `HD_ModularPart_BufferTube_ColtA2_Part.label` | Colt A2 rifle buffer tube | 경미한 수정 | 현실 고유명사 잔존 | Colt | Colt |  | A2 소총 버퍼 튜브 | Colt → Ironfang(아이언팽) | 적용 |
| T030 | Main `HD_ModularPart_BufferTube_ColtA2_Part.description` | Colt A2 rifle buffer tube connects the stock and houses the recoil bu… | 경미한 수정 | 현실 고유명사 잔존 | Colt A2 | Colt |  | 개머리판·완충 장치 연결 | Colt → Ironfang(아이언팽) | 적용 |
| T031 | Main `HD_Make_Gun_KragJorgensen_Weapon.label` (한국어) | 크라그-요르겐센 제작 | 경미한 수정 | 현실 고유명사 잔존 | 크라그-요르겐센 | Krag–Jørgensen |  | 제작 대상 | 결과물 이름 그레이록 M1892로 치환 | 적용 |
| T032 | Main `HD_Make_Gun_M1911_Weapon.label` (한국어) | M1911 권총 제작 | 경미한 수정 | 설정 불일치 | M1911 권총 |  |  | 제작 대상 | 영어 원문·결과물 이름(아이언팽 M1911)과 맞춤. 설명도 함께 수정 | 적용 |
| T033 | Main `HD_Apparel_CarvalyUniform` (label, description) | A standard uniform for Carvaly officers. Designed specifically for th… | 경미한 수정 | 내용 오류; 메타 설명 | Carvaly / Gives bonuses to movement speed |  | 스탯 보너스 문장 | 기병대 표준 제복 | 철자 Carvaly → cavalry, 보너스 문장 삭제, 한국어 조사 오류(기병대원를) 수정 | 적용 |
| T034 | Main `HD_Apparel_CarvalyHat` (label, description) | A matching hat for the Carvaly uniform. Gives bonuses to movement spe… | 경미한 수정 | 내용 오류; 메타 설명 | Carvaly / Gives bonuses |  | 스탯 보너스 문장 | 기병대 제복과 짝을 이루는 모자 | 철자 수정, 보너스 문장 삭제 | 적용 |
| T035 | Main `HD_GovernmentBond.description` | A high-value bond issued by the United States of Vesennica. It stores… | 경미한 수정 | 설정 불일치 | Vesennica |  |  | 베세니카 합중국 고액 채권 | 표기를 Vesenica로 통일 | 적용 |
| T036 | Main `HD_Building_Switchblade600Launcher.description` | 스폰 테스트용 발사대입니다. | 재작성 권장 | 메타 설명 | not craftable or normally buildable yet |  | 테스트·배치 안내 | 배회형 탄약 밀폐식 발사관 | 개발 안내 삭제, 물건 자체만 설명(영·한) | 적용 |
| T037 | LobosArsenal `HDA_C17.description` | A heavy transport aircraft with a 19 x 19 tile flat overhead sprite. … | 경미한 수정 | 메타 설명 | 19×19타일의 평면 상부 텍스처 |  | 그래픽 구현 설명 | 강하 차량 1대 수송, 운용 순서 | 구현 설명만 삭제(영·한). 운용 순서는 유지 | 적용 |
| T038 | Main `HD_MakeShaftBulk5.description` | Turn carbon steel into a straight mechanical shaft on a lathe. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T039 | Main `HD_MakeShaftBulk10.description` | Turn carbon steel into a straight mechanical shaft on a lathe. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T040 | Main `HD_ConvertIndustrialComponentToGeneralMachinePartBulk5.description` | Rework a standard industrial component into a general-purpose machine… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T041 | Main `HD_ConvertIndustrialComponentToGeneralMachinePartBulk10.description` | Rework a standard industrial component into a general-purpose machine… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T042 | Main `HD_MakePressComponentBulk5.description` | Stamp carbon steel into rough press components with the hydraulic pre… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T043 | Main `HD_MakePressComponentBulk10.description` | Stamp carbon steel into rough press components with the hydraulic pre… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T044 | Main `HD_MillGeneralMachinePartBulk5.description` | Mill carbon steel into a general-purpose machine part. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T045 | Main `HD_MillGeneralMachinePartBulk10.description` | Mill carbon steel into a general-purpose machine part. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T046 | Main `HD_RollUniformSteelPlateBulk5.description` | Roll carbon steel into an even, uniform steel plate. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T047 | Main `HD_RollUniformSteelPlateBulk10.description` | Roll carbon steel into an even, uniform steel plate. | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T048 | Main `HD_MakeBasicElectricalComponentsBulk5.description` | Fabricate insulated wiring, terminals, switches, and simple resistive… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T049 | Main `HD_MakeBasicElectricalComponentsBulk10.description` | Fabricate insulated wiring, terminals, switches, and simple resistive… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T050 | Main `HD_MakeBasicElectronicComponentsBulk5.description` | Assemble vacuum tubes, sockets, control elements, and precision conta… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T051 | Main `HD_MakeBasicElectronicComponentsBulk10.description` | Assemble vacuum tubes, sockets, control elements, and precision conta… | 경미한 수정 | 번역 누락(원문 측 정보 누락) | (배치 정보 없음) |  |  | 배치 수량·작업량 감소·중단 보존 | 한국어에만 있던 배치 정보를 영어 원문에 추가 | 적용 |
| T052 | Main `HD_PhotochlorogenRisk.description` (한국어) | 포토클로로겐 노출 뒤 나타나는 지연성 호흡기 손상 위험입니다. 초기 반응은 가라앉을 수 있으나 잠복기에 들어서면 자연 회복되… | 경미한 수정 | 번역 누락 | 지속적인 치료로 악화를 억제해야 합니다. |  |  | 치료 주기, 3시간 지연, 품질 합계 500% | 상태 설명이므로 영어 원문 수치를 그대로 번역 | 적용 |
| T053 | Main `HD_SweetGasRisk.description` (한국어) | 스위트 가스 노출 뒤 잠복기를 거쳐 나타나는 수포 작용제 손상 위험입니다. 발현되면 극심한 고통과 외부 조직의 영구 흉터를 … | 경미한 수정 | 번역 누락 | 지속적인 치료로 진행을 억제할 수 있습니다. |  |  | 흉터 부위 수, 3시간 지연, 품질 합계 500% | 상태 설명이므로 영어 원문 수치를 그대로 번역 | 적용 |
| T054 | Main `HelodLessLethalLowRanged.description` | beanbag | 경미한 수정 | 기타 | beanbag |  |  | 비살상 원거리 제압 장비 연구 | 임시 메모를 한국어 번역과 같은 설명으로 교체 | 적용 |
| T055 | Main `HD_ChewingPlainDogChew.stages.0.description` | temp | 경미한 수정 | 기타 | temp |  |  | 씹는 소리 의성어 | 임시 값을 한국어에 맞춘 의성어로 교체 | 적용 |
| T056 | Main `HD_Gene_Sharpshooter.description` | It's high noon | 경미한 수정 | 설정 불일치 | It's high noon |  |  | 총기를 잘 다룸, 특수 조작 사용 | 영어 원문을 한국어 번역 의미에 맞춤 | 적용 |
| T057 | blancasdrugs `BD_RestorilSleep.description` (1.6·1.7 한국어) | 섹스 | 강한 재작성 필요 | 번역 누락; 기타 | 섹스 |  | 부적절한 임시 문자열 | 레스트릴 숙취·진정 상태 | 영어 원문을 번역해 교체 | 적용 |
| T058 | blancasdrugs `BD_SonataSleep.description` (1.7 한국어) | 임시 값임 | 경미한 수정 | 번역 누락 | 임시 값임 |  |  | 균형 판정 트리거 | 영어 원문을 번역 | 적용 |
| T059 | blancasdrugs `BD_Somnifacientstronghold.description` (1.6·1.7 한국어) | 체내에 흡수된 수면제 계수의 총합을 나타내는 지표입니다. 자극제 축적치와 달리 이 수치 자체는 신체 장기에 손상을 주거나 부… | 경미한 수정 | 기타 | \ㅜ |  |  | 축적치 설명 | 깨진 줄바꿈 기호를 \n으로 수정 | 적용 |
| T060 | Main `HD_Money.description` | A beautifully engraved paper banknote used as the standard currency a… | 경미한 수정 | 메타 설명 | Only 25% of its market value contributes to colony item wealth. |  |  | 기지가치 반영 비율(필요 정보) | 사용자 결정에 따라 운용 정보로도 옮기지 않고 해당 문장을 영어·한국어에서 삭제 | 적용 |
| T061 | Main `HD_ClayTrap.description` | A compact spring-powered clay-target launcher for outdoor shooting re… | 경미한 수정 | 불필요한 사용법 | leave fourteen tiles clear |  |  | 사용 조건(필요 정보) | 운용 정보로 옮길 후보. 이번에는 유지 | 보류 |
| T062 | Main `HD_DrankAromaticBaseOil.stages.0.description` (한국어) | 달달하덩 | 경미한 수정 | 설정 불일치 | 달달하덩 |  |  | 따뜻하고 상쾌한 기분 | 임시 농담 문구로 확인됨. 한국어는 유지하고 영어 원문을 같은 느낌의 짧은 문구(So sweet~)로 교체 | 적용 |
| T063 | Main `HD_MakePrimaryCell.description` | Assemble a primary cell (chemical battery) using steel and a general … | 경미한 수정 | 내용 오류 | general machine part ↔ 화학 반응 부품 |  |  | 재료 목록 | 실제 재료(강철 10, 범용 기계 부품 1)에 맞춰 한국어 번역 수정. 영어 원문은 이미 일치 | 적용 |
| T064 | Main `HD_FrontierRescuer.description` (한국어) |  | 유지 | 기타 | [PAWN_objective] 생략 |  |  |  | 한국어에서 대명사를 생략한 자연스러운 번역. 서식 인자 차이는 의도된 것으로 판정 | 유지 |
| T065 | blancasdrugs `BD_Morphine.description` (1.6·1.7) | An opioid analgesic formulated in a pencil-style auto-injector for po… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T066 | blancasdrugs `BD_Fentanyl.description` (1.6·1.7) | A potent opioid analgesic formulated as an Oral Transmucosal Fentanyl… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T067 | blancasdrugs `BD_Ketamine.description` (1.6·1.7) | A dissociative anesthetic packaged as a sealed vial with a disposable… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T068 | blancasdrugs `BD_Laudanum.description` (1.6·1.7) | An opium tincture bottled as a bitter medicinal draught. It is crude … | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T069 | blancasdrugs `BD_Ambien.description` (1.6·1.7) | A fast-acting non-benzodiazepine (Z-drug) sedative designed for rapid… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T070 | blancasdrugs `BD_Sonata.description` (1.6·1.7) | An ultra-fast-acting non-benzodiazepine (Z-drug) sedative. Upon inges… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T071 | blancasdrugs `BD_Restoril.description` (1.6·1.7) | A potent benzodiazepine-based sedative and sleep inducer. Upon ingest… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T072 | blancasdrugs `BD_MKultra.description` (1.6·1.7) | "We've got a job to do." A permanent neurological rewiring of the cen… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T073 | blancasdrugs `BD_Desoxyn.description` (1.6·1.7) | "this is (not) a meth" A potent amphetamine-based central nervous sys… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T074 | blancasdrugs `BD_Benzedrine.description` (1.6·1.7) | An early-form amphetamine central nervous system stimulant. Upon inge… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T075 | blancasdrugs `BD_Dexedrine.description` (1.6·1.7) | A refined form of amphetamine stimulant with drastically reduced side… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T076 | blancasdrugs `BD_Modafinil.description` (1.6·1.7) | A modern, non-addictive eugeroic drug designed purely to suppress sle… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T077 | blancasdrugs `BD_Cola.description` (1.6·1.7) | A sweet caffeinated soft drink that briefly lifts spirits and helps w… | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T078 | blancasdrugs `BD_CocaineCola.description` (1.6·1.7) | A stimulant-laced cola based on an older, far less cautious formula. … | 재작성 권장 | 메타 설명; 과도한 사족; 카탈로그식 문체 | sedative factor / pawn's pain to 20% / caps maximum consciousness |  | 계수·시간·능력치 수치, 다른 약물과의 비교 | 제형(오토인젝터·OTFC·바이알), 약물 계열, 숙취·붕괴 설정, 인용문 | 수치·판정을 빼고 제형·계열·설정 중심으로 압축(영·한, 1.6·1.7). 옮긴 수치는 blancasdrugs PLAN-DrugInfo.md 4절에 기록, 설명창 표시는 추후 계획 | 적용 |
| T079 | blancasdrugs `BD_RestorilHigh`·`BD_ModafinilHigh`·`BD_Cola.description` (1.7) |  | 경미한 수정 | 언어 위치 오류(원문·번역 불일치) | 한국어가 영어보다 훨씬 김 |  |  |  | 효과·붕괴 헤디프 분리 때 영·한 범위를 맞추기로 함(PLAN-DrugInfo.md 1절) | 계획 |
| T080 | blancasdrugs `BD_Somnifacientstronghold.description` (한국어 둘째 문단) |  | 경미한 수정 | 언어 위치 오류(원문·번역 불일치) | 체내에 모다피닐이 있을 때는 … |  |  | 모다피닐 예외 규칙 | 상호작용 설명창 작업에서 코드와 대조 후 정리(PLAN-DrugInfo.md 2절) | 계획 |

## 4. 이번 범위에서 다루지 않은 것

- 연구 이름(6절): 2026-11-05 [연구·아이템 텍스트 적용](Text_Content_Application.md) 이후 바뀐 연구가 없어 다시 검수하지 않았다. LobosArsenal 연구 3개도 이번에는 이름 검수를 하지 않았다.
- Keyed 툴팁·계약서·운용 정보는 8절 예외에 따라 C·E 항목을 적용하지 않았다.
- 게임 실행 확인은 하지 않았다. XML 구문 검사, 원문·번역 키 대조, 서식 인자 대조로 검증했다.
