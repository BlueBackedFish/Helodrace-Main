# 연구·아이템 텍스트 적용

적용일: 2026-11-05. 기준: `ResearchContentAudit_20261029.md`와 검수표의 제안. 검수표 원본은 변경하지 않았다.

- 연구 이름 15개를 한국어·영어에 반영했다. `HelodColdWarGrenades`는 현행 정의가 없어 재생성하지 않았다.
- `HelodLowHeavyArtillery`, `HelodSniperRifle`, `HelodLessLethalChemical`은 이름·설명·연구 데이터 전체를 보존했다. 저격 연구의 기존 M1C 문구도 요청에 따라 유지한다.
- 아이템 설명 33개를 물건의 형태·구조·용도 중심으로 정리했다. 기존 성능 수치는 바꾸지 않았다.
- 연구 설명은 실제 범위에 맞춰 한국어 3개, 영어 32개를 정리했다. 추모 연구의 인용문은 보존하고 뒤의 임시 메모만 설명으로 교체했다.
- 제작법 12개의 이름과 작업 중 메시지를 현재 결과물 이름에 맞췄다. 총 179개 문자열을 변경했다.

전체 변경 전후와 파일 경로는 [changes.json](Data/TextContent/changes.json), 제외 연구의 기준본은 [exclusions.json](Data/TextContent/exclusions.json)에 기록했다. 연구 비용·선행 관계와 아이템 수치·제작 재료·해금 조건은 이 텍스트 변경에서 유지했다. 별도의 연구 배치 제안은 이번 범위에 포함하지 않는다.

## 운용 정보

CROWS 컴퓨터·진지, 돌파 폭약, 로터리 시추기, 전신기, 무전기, 야전 지혈제의 조작·효과 설명은 정보 창의 **운용 정보 → 자세히 보기**에 분리했다. 전신의 설치 대기·업무 시간·전지 사용, 무전의 전력·서비스, 시추기 품질 100~130%, 지혈제의 투여 시간·지속·출혈 억제·불이익을 남겼다.

`ContentUsageExtension.helpKey`가 있는 정의만 항목을 추가한다. 번역은 표시 시 조회하므로 언어 문자열을 캐시하지 않는다. `ThingDef.SpecialDisplayStats`와 .NET 문자열을 사용하며 Unity 의존성이나 새 native 변환 경계는 추가하지 않았다.

## 검증

RimWorld **1.7.4995 rev309**에서 정보 항목 7개의 번역 키·단일 표시와 일반 아이템에 빈 항목이 생기지 않는 것을 확인했다. 같은 실행에서 탄약 2종과 탄소강 진압방패를 실제 제작했다. 총 **51 PASS / COMPLETE**이며 자세한 결과는 [native-checks.txt](Data/TextContent/native-checks.txt), 빌드 해시와 검증 범위는 [verification.json](Data/TextContent/verification.json)을 따른다. 수집 시점에 호스트가 종료되어 종료 코드는 확보하지 못했다.

변경 전후 XML 대조로 179개 문자열, 비텍스트 데이터 보존, 제외 연구 전체 보존을 확인했다. 기존 재료·즉시 제작·작업대·레시피 배치 검사도 통과했다. 실제 실행은 영어로 진행했으며 한국어는 XML과 키를 대조했다. 마우스 조작·화면 픽셀 검사는 하지 않았다. 기존 Workshop 폴더 누락 및 영어 번역 경고 37건은 유지된다.

탄약 설명의 탄종 표기·구조는 [미 해병대 MCRP 3-01A](https://www.trngcmd.marines.mil/Portals/207/Docs/wtbn/MCRP%203-01A.pdf), [미 육군 탄약 포트폴리오](https://jpeoaa.army.mil/Portals/94/Documents/JPEOAAPortfolioBook_2025.pdf?ver=A_B_NzEETpCjNyGj93y_2g%3D%3D), [DOT&E 2020 연차 보고서](https://www.dote.osd.mil/Portals/97/pub/reports/FY2020/other/2020DOTEAnnualReport.pdf), [M995 규격](https://quicksearch.dla.mil/qsDocDetails.aspx?ident_number=209774)과 [Nammo 설명](https://www.nammo.com/products/ammunition/small-caliber-ammunition/5-56mm-series/5-56-mm-x-45-armor-piercing-3/)을 참고했다. 현실 수치를 게임 성능에 적용하지는 않았다.
