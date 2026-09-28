# 전진기지 계약 시스템

## 1. 개요

전진기지 계약은 전신기를 통해 협력 팩션의 전진기지를 건설하고, 일정 기간 동안 군사·군수 지원 서비스를 사용하는 시스템이다.

현재 계약 방식은 다음 두 가지뿐이다.

- **FFP**: 계약 시 서비스별 사용 재고를 미리 설정한다.
- **비용 상환**: 사용한 서비스의 비용을 기간별로 정산한다.

IDIQ 계약과 IDIQ 서비스 지시 방식은 제거되었다.

## 2. 주요 코드 구조

| 파일 | 역할 |
| --- | --- |
| `Source/Helodrace/WildWest/TelegraphTable.cs` | 전신기 UI, 계약 조건 선택, 계약 생성 |
| `Source/Helodrace/WildWest/ForwardBaseServices.cs` | 계약 종류, 서비스 종류, 서비스 비용·사거리 정의 |
| `Source/Helodrace/WildWest/ForwardBaseWorldObjects.cs` | 건설 중 전진기지와 완성된 전진기지의 저장·운영·정산 |
| `Source/Helodrace/WildWest/ForwardBaseDispatchSupport.cs` | 보병·군수 서비스 호출 |
| `Source/Helodrace/WildWest/SniperSupport.cs` | 저격지원 호출 및 실행 시점 처리 |
| `Source/Helodrace/WildWest/CasSupport.cs` | CAS 호출 및 탄약·비행시간 처리 |
| `Source/Helodrace/WildWest/MortarSupport.cs` | 박격포·105mm·155mm 포병지원 처리 |
| `Source/Helodrace/GreatWar/CompSCR300.cs` | SCR-300을 통한 계약 서비스 요청 |

## 3. 계약 생성 흐름

```text
전신기
  ↓
Dialog_TelegraphTable
  ├─ 협력 팩션 선택
  ├─ 건설 타일 선택
  ├─ 기지 종류 선택
  ├─ 계약 종류 선택
  ├─ 계약 기간 선택
  ├─ 제공 서비스 선택
  └─ FFP인 경우 서비스별 30일당 재고 횟수 설정
       ↓
HelodForwardBaseConstruction
       ↓ 7일
HelodForwardBase
```

### 기지 종류

- PB
- FB
- COP
- FOB

기지 종류에 따라 선택할 수 있는 서비스 종류가 달라진다. 군사 신용은 기지 종류·계약 종류·서비스의 해금 조건으로 사용된다.

### 계약 기간

- 30일
- 60일
- 120일
- 240일

서비스 재고의 기본 단위는 30일이다. 장기 계약을 선택해도 FFP 서비스 재고는 30일 단위로 충전된다.

## 4. FFP 계약

### 설정

FFP 계약에서 선택한 각 서비스마다 `30일당` 제공 횟수를 입력한다.

- 기본값: 서비스별 10회
- 입력 범위: 1~999,999회
- 선택하지 않은 서비스는 재고를 만들지 않는다.
- 입력값은 계약 초안과 계약 정보에 표시된다.

예시:

```text
저격지원       20회 / 30일
CAS             5회 / 30일
보급 수송대    50회 / 30일
```

### 재고 동작

FFP 재고는 실제 아이템이 아니라 `HelodForwardBase` 내부의 서비스 사용 가능량으로 관리된다.

1. 계약이 완성되면 서비스별 설정 횟수가 해당 기간의 재고가 된다.
2. 서비스를 호출할 때마다 재고가 1회 차감된다.
3. 재고가 0이면 해당 서비스 요청이 거부된다.
4. 다음 30일 서비스 단위가 시작되면 사용량이 초기화되고 설정 수량만큼 재충전된다.

재고 차감은 모든 주요 서비스 호출 경로에서 공통으로 처리된다.

- 일반 서비스: `TryConsumeServiceUse`
- 박격포·포병: `TryConsumeAmmunitionSupport`
- W48: W48 주문 생성 시 탄약지원 소비 경로 사용

### FFP 사용량 표시

전진기지 Inspect 정보에는 서비스별로 다음과 같이 표시된다.

```text
서비스: 남은 재고 / 설정 재고, 누적 사용량
```

사용량 기록은 저장되므로 세이브를 불러와도 현재 기간 사용량과 누적 사용량이 유지된다.

## 5. 비용 상환 계약

비용 상환 계약은 FFP 재고를 사용하지 않는다.

- 서비스 호출 횟수 제한 없음
- 서비스 사용액에 대한 군사 신용 상한 없음
- 일반 서비스는 실제 실행 시점에 사용량 기록
- 포병·탄약지원은 호출 시 탄약 비용 기록
- 서비스 기간이 끝나면 실제 사용 비용을 `HD_Money`로 정산

정산에 필요한 `HD_Money`가 부족하면 정산 실패로 처리되고 전진기지는 철수한다.

현재 군사 신용은 비용 상환의 사용량 상한으로 쓰이지 않는다. 다만 계약·기지·서비스 선택 화면의 해금 조건과 계약 금액 계산용 가격 보정에는 계속 사용된다.

## 6. 서비스 호출 공통 흐름

```text
지원 가능한 전진기지 탐색
  ↓
계약 서비스 포함 여부 확인
  ↓
HasServiceCapacity 확인
  ├─ FFP: 현재 30일 재고 확인
  └─ 비용 상환: 서비스가 계약되어 있으면 통과
  ↓
서비스 호출 또는 실행
  ↓
TryConsumeServiceUse / TryConsumeAmmunitionSupport
  ↓
사용량 및 정산 데이터 기록
```

서비스별 실제 실행 시점은 서비스마다 다르다.

- FFP: 호출 시점에 재고를 소비한다.
- 비용 상환: 서비스 실행 시점에 사용량을 소비한다.

## 7. 저장 데이터

`HelodForwardBaseConstruction`과 `HelodForwardBase`는 다음 계약 정보를 저장한다.

- `contractCostKind`: FFP 또는 비용 상환
- `contractServices`: 계약 서비스 목록
- `contractServiceUnitCounts`: 서비스별 FFP 30일 재고 설정
- `contractDurationDays`: 계약 기간
- `contractMilitaryCredit`: 계약 생성 시점의 군사 신용
- `usagePeriodStartTick`: 현재 30일 서비스 단위 시작 시각
- `usageServices`: 사용량을 추적 중인 서비스 목록
- `usageCounts`: 현재 서비스 단위 사용량
- `totalUsageCounts`: 계약 전체 누적 사용량
- `mortarUsageCostGoldStandard`: 비용 상환 포병 탄약 비용

기존 세이브 호환을 위해 다음 보정이 적용된다.

- 기존 IDIQ 계약 값은 FFP로 정규화된다.
- 기존 세이브에 서비스별 FFP 횟수 정보가 없으면 기본값 10회가 채워진다.

## 8. 별도 제한 사항

전진기지 FFP 서비스 재고와 별개로, 일부 지원 기능에는 작전 자체의 제한이 남아 있다.

- W48은 한 번에 하나의 주문만 대기할 수 있다.
- CAS에는 항공기 비행시간·탄약·진행 중 작전 상태가 있다.
- 포병 한 번의 호출은 설정된 탄종과 탄 수만큼 탄약을 사용한다.

이 제한들은 계약의 30일 서비스 재고 제한과는 별개의 전투·작전 처리 규칙이다.

## 9. 관련 번역 키

- `HD_TelegraphTable_ForwardBase_ServiceUnitCount`
- `HD_TelegraphTable_ForwardBase_ServiceUnitCountShort`
- `HD_ForwardBase_ServiceStockExhausted`
- `HD_ForwardBase_ServiceUsageLineStock`
- `HD_ForwardBase_ServiceUsageLineUnlimited`

번역 파일:

- `Languages/English/Keyed/TelegraphTable.xml`
- `Languages/Korean (한국어)/Keyed/TelegraphTable.xml`
