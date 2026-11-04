# 고정화기 포장·재배치와 중량

2026-11-05 적용. 모드의 `building.turretGunDef`가 있는 고정화기 11종에 `MinifiedThing`과 `BuildingsSecurity` 분류를 추가했다. 기본 게임의 **제거/포장 → 운반 → 설치** 경로를 사용한다. 설치된 건물의 재설치 명령도 같은 경로를 이용한다. 스위치블레이드의 기존 시험·시나리오 전용 생산 정책은 유지한다.

기본 `Mass`는 탄약을 제외한 화기·거치대/포가 또는 아래에 명시한 장비 세트의 기준 중량이다. 별도 승무원·견인차·탄약 운반차는 포함하지 않는다. 휴대 케이스나 냉각수처럼 자료에 포함된 구성품은 표에 명시했다. 게임의 잔탄·연료를 추가 중량으로 환산하는 체계나 탄약 아이템 자체의 중량은 이번에 변경하지 않았다.

| 화기 | 기존 kg | 적용 kg | 기준 |
| --- | ---: | ---: | --- |
| M1917 | 48 | 42.7 | 냉각수 포함 총·핀틀 41lb + M1917A1 삼각대 53.2lb [1] |
| M2HB | 58 | 58.1 | 총 84lb + M3 삼각대 44lb [2] |
| M1916 37mm | 46 | 78.9 | 총·주퇴부 88lb + 삼각대 86lb. 현행 그림에 없는 바퀴·차축 제외 [3] |
| 3인치 Hawkins | 47 | 47.2 | 현행 Stokes 그래픽에 대응하는 Stokes 104lb 기준 [4] |
| M1 81mm | 62 | 61.7 | 포·양각대·받침판 합계 136lb [5] |
| M2A2 105mm | 1020 | 2259 | M2A2 포가를 포함한 4980lb [6] |
| M114 155mm | 5800 | 5760 | M114A1 장비 제원 기준 [7] |
| M220 TOW | 93 | 120.4 | M220A1 전체 구성품·휴대 케이스 포함, 미사일 제외 265.5lb [8] |
| M167 | 1576 | 1588 | 이동 구성 중량. 사격 구성 1565kg과 구분 [9] |
| CROWS II | 230 | 145.5 + 장착 화기 | 무장 없는 Weapon Station 기준. 별도 컴퓨터 제외 [10] |
| Switchblade 600 발사관 | 54 | 14.5 | 제조사 AUR 29.5kg − 탄체 15kg으로 산출한 빈 발사관 추정치 [11] |

CROWS의 추가 화기 중량은 M2HB 38.1kg [12], Mk 19 32.92kg [13], M240B 12.52kg [14], M249 7.94kg [15]이다. `CrowsWeaponDef.weaponMass`를 `CompCrowsAmmo.GetStatOffset(Mass)`에서 합산한다. 빈 거치대에는 더하지 않는다. 전용 중간재 제작비·탄약 수량·성능·상부 그래픽 크기는 유지한다. 새 Unity API나 변환 경계를 추가하지 않았다.

CROWS를 포장하면 기존 연결 해제 처리에 따라 원격 사격이 멈추고 컴퓨터 연결이 해제된다. 설치 후 컴퓨터에 다시 연결한다. 장착 화기와 잔탄은 건물 내부에 남는다. 진행 중인 장착 작업의 자재는 기존 취소 규칙에 따라 지도에 반환된다.

## 중량 근거

원문을 직접 인용하지 않고 기준 구성과 환산값을 기록했다. 미 육군·해병대 매뉴얼의 일부 링크는 원문 스캔을 보관한 외부 사이트다. 서로 다른 형식·이동/사격 구성의 중량을 섞지 않았다. CROWS의 고정식 받침대는 모드의 표현이며 별도 실측 중량을 주장하지 않는다.

1. [FM 23-55 (1945), p.1](https://www.big-red-one.org/FM%2023-55%20M1917%20M1919%20.30%20CAL%20MACHINE%20GUN.pdf).
2. [TC 3-21.75, p.10-9](https://rdl.train.army.mil/catalog-ws/view/100.ATSC/C6763F0D-4346-4DA6-BC8C-4EBFF5B34997-1377112048566/tc_3-21x75.pdf).
3. [FM 23-75, p.2](https://www.ibiblio.org/hyperwar/USA/ref/FM/PDFs/FM23-75.pdf).
4. [미 육군 Combat Studies Institute, Still the General, Stokes 설명 주석](https://www.govinfo.gov/content/pkg/GOVPUB-D101-PURL-gpo107717/pdf/GOVPUB-D101-PURL-gpo107717.pdf).
5. [미 해병대 Weapons (Marine Infantry Battalion), p.53](https://stephentaylorhistorian.com/wp-content/uploads/2020/02/weapons-marine-infantry.pdf).
6. [Australian Army Journal, July 1960, p.38](https://researchcentre.army.gov.au/sites/default/files/aaj_134_jul_1960.pdf).
7. [MCIA Philippines Handbook, p.A-26](https://info.publicintelligence.net/MCIA-PhilippinesHandbook.pdf).
8. [FM 3-22.34, §1-2](https://www.trngcmd.marines.mil/Portals/207/Docs/TBS/FMFRP%206-90-1%20Tow%20Weapon%20System.pdf).
9. [MCIA Honduras Handbook, p.A-14](https://info.publicintelligence.net/MCIA-HondurasHandbook.pdf).
10. [TM 9-1090-219-10, 0002-44](https://www.scribd.com/document/845419694/TM-9-1090-219-10-30-Mar-2017-pdf).
11. [AeroVironment 2023 제품 카탈로그, Switchblade 600](https://www.avinc.com/images/uploads/product_docs/2023_Product_Catalog.pdf). 제조사 공개값을 뺀 산출값이며 빈 발사관의 개별 실측 공표값은 아니다.
12. [미 육군 M2/M2A1 제원](https://cpeground.army.mil/Equipment/Equipment-Portfolio/PM-MBCT-Lethality-Portfolio/M2-M2A1-50-Caliber-Machine-Gun/).
13. [미 해병대 Mk 19 Mod 3 제원](https://gulflink.osd.mil/11th_marines/11th_marines_refs/n19en244/9867a7c6f72a0ad08525627c006cb4ef.html).
14. [미 육군 M240B/L/H 제원](https://cpeground.army.mil/Equipment/Equipment-Portfolio/PM-SL-Portfolio/M240B-L-H-Medium-Machine-Gun/).
15. [미 육군 M249 중량 설명](https://www.army.mil/article/148002/New_light_machine_gun__M249_put_on_weight_control/).

## 실제 게임 검증

RimWorld **1.7.4995 rev309**에서 `Start-Checks.ps1 -FixedRelocation`으로 **252 PASS / COMPLETE**를 확인했다. 11개 정의 전부와 CROWS 추가 장착형 3종을 포함해 실제 포장·운반·회전 재설치 **14회**를 수행했다.

- 보안 건물 저장 분류·포장 명령이 실제 게임에서 연결됨.
- 작업자의 실제 제거 작업과 포장 아이템 운반·설치 작업이 완료됨.
- 동일 건물과 내장 총기의 객체, 손상된 내구도, 잔탄 연료, 장전 포탄 종류·수량이 보존됨.
- 설치·포장·재설치 상태의 무게가 일치하고 중복 포장 아이템이 남지 않음.
- CROWS 빈 거치대 및 장착형 4종의 무게 합산과 포장 시 원격 연결 해제 확인.

초기 시험에서는 재설치 후 자동 수리가 내구도 검사에 개입했다. 시험에서 건설을 끄면 설치 작업도 중단되어, 최종 실행에서는 건설을 유지하고 **시험 작업자의 자동 수리만** 차단했다. 이 조건 보완으로 제품 코드의 포장·설치 경로를 우회하지 않았다.

[검사 로그](Data/FixedWeaponRelocation/native-checks.txt), [무게 변경 목록](Data/FixedWeaponRelocation/weights.json), [실행 정보·DLL 해시·한계](Data/FixedWeaponRelocation/verification.json)에 기록했다. XML 전체 파싱, 레시피 배치, 기존 재료 검사를 통과했다. 중량·포장 외의 비용·연구·전투·그래픽 데이터 보존도 대조했다.

저장/재로드와 다른 지도로 이동하는 카라반 여행은 별도로 실행하지 않았다. 수집 시 호스트 종료 코드는 확보되지 않았으며 COMPLETE와 정상 로그 종료를 확인했다. 기존 Workshop 폴더 누락 및 영어 번역 경고 37건은 유지된다.
