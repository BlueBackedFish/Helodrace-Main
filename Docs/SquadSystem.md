# 분대·지휘 체계 구현 계획

## 1. 목표

기존 RimWorld 습격 시스템의 전투원 운용 방식을 대체하여, 습격대가 실제 군사조직과 유사한 **계층형 지휘구조**를 가지도록 한다.

핵심 요구사항은 다음과 같다.

- 습격에 참가한 Pawn을 하나 이상의 전술 단위로 조직한다.
- 조직은 고정된 깊이가 아니라 가변적인 트리 구조를 가진다.
- 투입 병력 규모와 교리에 따라 필요한 계층만 생성한다.
- 각 Pawn은 조직 내에서 역할(Role)을 가진다.
- 지휘관 손실 시 미리 정의된 승계 순서에 따라 지휘권이 이전된다.
- 지휘구조와 PawnKind는 분리한다.
- WW2식 분대, 현대식 화력조 기반 분대 등 서로 다른 편제를 동일 시스템으로 표현할 수 있어야 한다.
- 이후 명령 전달, 통신, 지휘 지연, 지휘 효율, AI 전술과 연결할 수 있도록 설계한다.

---

# 2. 기본 구조

조직은 `CombatGroup`의 트리로 구성한다.

```text
CombatGroup
├─ Parent
├─ Children[]
├─ Members[]
├─ Commander
├─ RoleAssignments[]
└─ Doctrine / FormationDefinition
```

`CombatGroup`은 특정 계급을 의미하지 않는다.

동일 클래스로 다음을 모두 표현할 수 있다.

```text
Platoon
└─ Squad
   └─ Fireteam
```

또는

```text
Squad
└─ Fireteam
```

또는

```text
Squad
```

따라서 **루트 노드가 반드시 Platoon일 필요는 없다.**

---

# 3. 가변 조직 트리

습격 생성 시 실제 투입 병력에 맞춰 트리를 생성한다.

## 소규모 투입

```text
1st Squad
├─ Squad Leader
├─ Rifleman
├─ Rifleman
├─ Automatic Rifleman
├─ Rifleman
└─ Rifleman
```

이 경우 Platoon 노드는 존재하지 않는다.

## 현대식 9인 분대

```text
1st Squad
├─ Squad Leader
│
├─ Alpha Fireteam
│  ├─ Team Leader
│  ├─ Automatic Rifleman
│  ├─ Grenadier
│  └─ Rifleman
│
└─ Bravo Fireteam
   ├─ Team Leader
   ├─ Automatic Rifleman
   ├─ Grenadier
   └─ Rifleman
```

## 소대급 투입

```text
1st Platoon
├─ Platoon Leader
├─ Platoon Sergeant
│
├─ 1st Squad
│  ├─ Squad Leader
│  ├─ Alpha Fireteam
│  └─ Bravo Fireteam
│
├─ 2nd Squad
│  ├─ Squad Leader
│  ├─ Alpha Fireteam
│  └─ Bravo Fireteam
│
└─ 3rd Squad
   ├─ Squad Leader
   ├─ Alpha Fireteam
   └─ Bravo Fireteam
```

병력이 부족하면 완성된 소대를 억지로 만들지 않는다.

예를 들어 14명만 투입되었다면 교리에 따라

```text
Squad A
Squad B
```

또는

```text
Platoon HQ
├─ Squad A
└─ Squad B
```

중 하나를 선택할 수 있다.

---

# 4. 편제 Definition

실제 조직구조는 XML/Def에서 정의할 수 있도록 한다.

예:

```text
FormationDef
- US_WW2_RifleSquad
- US_Modern_RifleSquad
- US_Modern_RiflePlatoon
- MilitiaSquad
- SOFTeam
```

각 FormationDef에는 최소한 다음 정보가 필요하다.

```text
FormationDef

Name
UnitLevel
MinimumPersonnel
IdealPersonnel
MaximumPersonnel

RequiredRoles[]
OptionalRoles[]

ChildFormations[]

CommandStructure
SuccessionRules
Doctrine
```

이를 통해 코드에

```text
if (modernUS)
    makeTwoFireteams();
```

같은 방식으로 편제를 박지 않는다.

---

# 5. Pawn과 조직 역할 분리

Pawn 자체의 종류와 조직 내 역할은 별개로 처리한다.

예:

```text
PawnKind:
State_Rifleman

AssignedRole:
SquadLeader
```

또는

```text
PawnKind:
State_Rifleman

AssignedRole:
FireteamLeader
```

따라서 동일한 PawnKind가 상황에 따라

```text
Rifleman
FireteamLeader
AssistantSquadLeader
ActingSquadLeader
```

가 될 수 있다.

이렇게 해야 지휘관 사망 이후 일반 소총수가 지휘권을 승계하는 것도 자연스럽게 처리할 수 있다.

---

# 6. Role 시스템

각 Pawn에는 해당 전투조직에서의 역할을 나타내는 Role을 할당한다.

예:

```text
PlatoonLeader
PlatoonSergeant

SquadLeader
AssistantSquadLeader

FireteamLeader

AutomaticRifleman
Grenadier
Rifleman
Medic
Marksman
RadioOperator
```

Role은 단순한 표시가 아니라 다음 정보를 가질 수 있다.

```text
RoleDef

CommandAuthority
SuccessionPriority
CanCommandUnitLevel
CombatFunction
RequiredEquipment
PreferredSkill
DoctrineTags
```

---

# 7. 지휘권과 전투 역할 분리

특히 중요한 부분이다.

`AutomaticRifleman`, `Grenadier`, `Medic` 같은 것은 **전투 역할**이고,

`SquadLeader`, `FireteamLeader` 등은 **지휘 역할**이다.

한 Pawn이 둘을 동시에 가질 수 있도록 한다.

예:

```text
Pawn
├─ CombatRole: AutomaticRifleman
└─ CommandRole: FireteamLeader
```

그러면 팀장이 사망했을 때 자동소총수가 팀장을 승계하더라도 자동화기를 버릴 필요가 없다.

---

# 8. 지휘권 승계

각 조직은 단순히 현재 지휘관만 저장하지 않고 **승계 후보 목록**을 유지한다.

예:

```text
Alpha Fireteam

Commander:
Pawn A

Succession:
1. Pawn A - Team Leader
2. Pawn B - Assistant / designated successor
3. Pawn C - Rifleman
4. Pawn D - Automatic Rifleman
```

Pawn A가 사망하거나 전투불능 상태가 되면 Pawn B에게 지휘권을 넘긴다.

---

# 9. 승계 우선순위

승계는 단순 Pawn 배열 순서가 아니라 점수 또는 계층화된 규칙으로 결정한다.

예:

```text
1. 명시적으로 지정된 부지휘관
2. 동일 조직의 하위 지휘관
3. 지휘 자격을 가진 일반 대원
4. 숙련도가 높은 일반 대원
5. 남은 Pawn 중 최적 후보
```

내부적으로는 다음과 같이 계산할 수 있다.

```text
SuccessionScore =
    RolePriority
  + RankPriority
  + LeadershipQualification
  + Experience
  + DoctrineModifier
```

다만 **명시적인 군사적 승계 순서가 존재하는 경우 점수 계산보다 우선**한다.

즉,

```text
Explicit succession
        ↓
Qualified subordinate commander
        ↓
Dynamic scoring
```

순서로 처리한다.

---

# 10. Acting Role

승계했다고 원래 역할 자체를 덮어쓰지 않는다.

예:

```text
OriginalRole:
FireteamLeader

ActingRole:
SquadLeader
```

또는

```text
OriginalRole:
Rifleman

ActingRole:
FireteamLeader
```

UI에서는

```text
소총수
화력조장 대행
```

처럼 표시할 수 있다.

이 구조는 전투 종료 후 원래 편제를 복구하거나 상급자가 복귀했을 때 지휘권을 반환하기 쉽게 한다.

---

# 11. 계층적 승계

승계는 해당 노드 내부에서 먼저 해결한다.

예:

```text
Platoon
│
├─ Squad A
│  ├─ Alpha
│  └─ Bravo
│
└─ Squad B
```

Squad A의 분대장이 사망하면 먼저 Squad A 내부에서 후임자를 찾는다.

```text
Assistant Squad Leader
↓
Alpha Team Leader
↓
Bravo Team Leader
↓
기타 자격자
```

소대장 사망도 마찬가지다.

```text
Platoon Sergeant
↓
1st Squad Leader
↓
2nd Squad Leader
↓
...
```

따라서 전체 트리가 자연스럽게 **지휘권 승계 그래프** 역할도 한다.

---

# 12. 지휘관 손실 상태

지휘관 사망 즉시 새로운 지휘관이 완전한 효율로 작동하게 만들지는 않는다.

상태를 다음처럼 구분한다.

```text
Normal
↓
CommanderLost
↓
SuccessionPending
↓
ActingCommander
↓
CommandRestored
```

예를 들어:

```text
분대장 사망
→ 1.5초 지휘 혼란
→ Alpha Team Leader 승계
→ 5초간 지휘 효율 70%
→ 정상화
```

이를 기존에 구상한 **지휘 지연/지휘 효율 시스템**과 연결한다.

---

# 13. 상급 지휘망 단절

상급 지휘관이 없어졌다고 하위 조직이 정지해서는 안 된다.

예:

```text
Platoon Leader DEAD

Squad A → 자체 행동 지속
Squad B → 자체 행동 지속
Squad C → 자체 행동 지속
```

대신 다음 요소가 영향을 받는다.

```text
소대 단위 목표 변경
분대 간 협조
지원화력 요청
예비대 투입
공격 방향 변경
퇴각 명령
```

즉 **하위 조직의 전술행동과 상위 조직의 지휘기능을 분리**한다.

---

# 14. 명령 계층

향후 명령 시스템은 조직 트리를 따라 전달한다.

```text
Platoon Leader
       ↓
Squad Leader
       ↓
Fireteam Leader
       ↓
Pawn
```

예:

```text
소대장:
건물 A 확보

↓

1분대장:
Alpha → 정면 제압
Bravo → 우측 기동

↓

Alpha TL:
창문과 출입구 제압

Bravo TL:
우측 벽 따라 이동
```

상급 지휘관은 세부 Pawn 이동을 직접 결정하지 않는 구조를 기본으로 한다.

---

# 15. Command Intent

명령을 단순 목적지 좌표가 아니라 **의도 + 파라미터**로 저장한다.

예:

```text
Order

Type: Assault
Target: Building_A

Intent:
Capture

Parameters:
Aggression = High
Spacing = Medium
CasualtyTolerance = Medium
```

하위 지휘관이 이를 자기 조직 수준의 명령으로 변환한다.

```text
Platoon:
Assault Building A

↓

Squad:
Approach East

↓

Fireteam:
Suppress Entrance

↓

Pawn:
MoveTo Cell
Attack Target
```

이렇게 하면 계층형 지휘체계가 AI 행동에도 실제 의미를 갖게 된다.

---

# 16. WW2 / 현대 편제 차이

동일 시스템에서 교리만 다르게 정의한다.

## WW2식

```text
Squad
├─ Squad Leader
├─ Assistant
├─ Automatic Rifleman
└─ Riflemen...
```

특징:

```text
CommandDepth = 낮음
DirectControl = 높음
FireteamIndependence = 낮음
SquadCohesion = 높음
```

분대가 하나의 큰 전술 단위로 움직인다.

## 현대식

```text
Squad
├─ Squad Leader
├─ Alpha Fireteam
└─ Bravo Fireteam
```

특징:

```text
CommandDepth = 높음
DirectControl = 낮음
FireteamIndependence = 높음
DistributedLeadership = 높음
```

화력조가 자체적으로 명령을 수행할 수 있다.

이 차이를 통해 장비뿐 아니라 **조직 자체가 기술/교리 발전 요소**가 된다.

---

# 17. 습격 생성 과정

기존 습격 생성 후 Pawn을 억지로 분류하는 방식보다 조직을 먼저 계획하고 Pawn을 채우는 방식이 바람직하다.

```text
Raid 생성
↓
Faction / Doctrine 결정
↓
투입 Point 결정
↓
병력 규모 계산
↓
적절한 Formation 선택
↓
Organization Tree 생성
↓
각 Node에 Role Slot 생성
↓
Pawn 생성
↓
Role Slot에 Pawn 배치
↓
장비 배정
↓
승계 순서 계산
↓
전술 목표 배정
↓
습격 시작
```

핵심은

**Pawn 생성 → 분대 구성**

보다

**분대 구성 → 필요한 Pawn 생성**

순서로 만드는 것이다.

---

## 18. 완편 단위 기반 스폰

모든 전투 조직은 **FormationDef에 정의된 정원을 완전히 충족한 상태로만 생성**한다.

불완전 분대, 불완전 화력조, 인원 부족 소대는 생성하지 않는다.

예:

```text
Modern Rifle Squad
StandardPersonnel: 9
```

9인 분대를 선택했다면 반드시 9명을 생성한다.

```text
1st Squad
├─ Squad Leader
├─ Alpha Fireteam
│  ├─ Team Leader
│  ├─ Automatic Rifleman
│  ├─ Grenadier
│  └─ Rifleman
└─ Bravo Fireteam
   ├─ Team Leader
   ├─ Automatic Rifleman
   ├─ Grenadier
   └─ Rifleman
```

7명이나 8명짜리 축소 분대는 생성하지 않는다.

---

## 18.1 편제 단위별 비용

각 완편 조직에는 예상 투입 비용을 계산한다.

```text
FormationCost =
    PersonnelCost
  + EquipmentCost
  + SpecialistCost
```

예:

```text
Rifle Squad = 520pt
Rifle Platoon = 1,850pt
```

실제 값은 해당 Formation을 구성하는 Pawn 및 장비를 기반으로 계산할 수 있도록 한다.

---

## 18.2 포인트가 약간 부족한 경우

현재 Raid Point가 다음 완편 조직을 생성하는 데 약간 부족하다면 일정 허용범위까지 초과를 허용한다.

예:

```text
Raid Points: 500
Squad Cost: 520

Difference: +20
```

허용범위 안이면 그냥 520pt 규모의 완편 분대를 생성한다.

```text
Available: 500
Required: 520
Tolerance: 10%

→ Spawn
```

즉, **편제를 맞추기 위해 소규모 포인트 오버런을 허용**한다.

허용범위는 고정 포인트보다 비율 기반으로 설정할 수 있게 한다.

```text
FormationPointTolerance = 0.10
```

---

## 18.3 다음 편제를 만들기에는 포인트가 크게 부족한 경우

현재 편제를 생성하고 남은 포인트가 다음 완편 전투단위를 만들기에는 부족하며, 허용 가능한 초과 범위에도 들어오지 않는다면 추가 Pawn을 생성하지 않는다.

예:

```text
Raid Points: 1,400

Squad Cost: 520

2 Squads = 1,040
3 Squads = 1,560
```

3번째 분대를 생성하기 위한 차이가 허용범위를 넘는다면:

```text
Spawn:
2 × Rifle Squad

Spent:
1,040pt

Reserve:
360pt
```

남은 360pt는 버리지 않는다.

이를 **Support Point / Reserve Point**로 보존한다.

---

## 18.4 잔여 포인트

습격 조직은 잔여 포인트를 별도 자원으로 가진다.

```text
CombatOrganization

InitialRaidPoints
FormationPointsSpent
SupportPointsRemaining
```

예:

```text
InitialRaidPoints:      1400
FormationPointsSpent:   1040
SupportPointsRemaining: 360
```

이 포인트는 일반 전투 Pawn을 추가하는 데 사용하지 않는다.

추후 외부 지원 시스템에서 소비할 수 있도록 남겨둔다.

예상 사용처:

- 박격포 지원
- 포병 지원
- CAS
- 드론
- 증원
- 탄약/보급
- MEDEVAC/CASEVAC
- 전자전
- 정찰
- 기타 교리별 외부 지원

구체적인 소비 방식은 현재 단계에서는 구현하지 않는다.

---

## 18.5 편제 선택 원칙

편제 생성 알고리즘의 기본 원칙은 다음과 한다.

```text
Raid Point 결정
↓
Faction / Doctrine 확인
↓
사용 가능한 Formation 후보 확인
↓
완편 Formation 조합 계산
↓
정원 단위로만 조직 생성
↓
소규모 부족분?
├─ Yes → 허용범위 내에서 초과 생성
└─ No
↓
추가 Formation 생성 가능?
├─ Yes → 추가 완편 Formation 생성
└─ No
↓
잔여 포인트 → Support Point
```

핵심 규칙:

> **병력 수를 포인트에 맞추지 않고, 포인트를 편제 단위에 맞춘다.**

---

# 19. 조직 ID

각 조직과 Pawn에는 영구적인 참조를 부여한다.

```text
CombatOrganization ID: Raid_382

Group:
Raid_382_Squad_1

Group:
Raid_382_Squad_1_Alpha
```

Pawn에는 다음 정보를 저장한다.

```text
OrganizationID
GroupID
ParentGroupID

CombatRole
CommandRole
ActingRole
```

세이브/로드 이후에도 동일한 조직 트리를 복원할 수 있어야 한다.

---

# 20. 핵심 클래스 초안

```text
CombatOrganization
│
├─ RootGroups
├─ Doctrine
├─ InitialRaidPoints
├─ FormationPointsSpent
├─ SupportPointsRemaining
└─ GlobalCommandState

CombatGroup
│
├─ Parent
├─ Children
├─ Members
├─ Commander
├─ ActingCommander
├─ SuccessionList
└─ CommandState

PawnOrganizationComponent
│
├─ Organization
├─ Group
├─ CombatRole
├─ CommandRole
├─ ActingRole
└─ CommandQualifications
```

Definition 계층:

```text
FormationDef
RoleDef
DoctrineDef
```

전술 AI 및 전술 명령 Definition은 현재 단계에서 포함하지 않는다.

---

# 21. 전술 AI

## 현재 범위에서 제외

전술 AI는 **이번 조직/편제 시스템의 구현 범위에 포함하지 않는다.**

현재 프로젝트에 존재하는 전술 AI 또는 유사 시스템과도 연결하지 않는다.

조직 시스템은 전술 AI와 독립적으로 작동하도록 작성한다.

현재 필요한 것은:

```text
Pawn
↓
Organization
↓
Group
↓
Role
↓
Command Hierarchy
↓
Succession
```

까지다.

다음 요소는 구현하지 않는다.

```text
Assault AI
Suppress AI
Fire and Maneuver
Bounding Overwatch
CQB AI
Group Movement AI
Target Assignment
Formation Movement
Tactical Order
Command Intent
```

이 영역은 추후 별도의 전술 AI 시스템으로 추가한다.

단, 미래의 전술 AI가 조직 정보를 읽을 수 있도록 API/인터페이스 수준의 확장성은 남겨둔다.

예:

```text
GetOrganization(Pawn)

GetGroup(Pawn)

GetCommander(CombatGroup)

GetSubordinates(Pawn)

GetParentGroup(CombatGroup)

GetChildGroups(CombatGroup)

GetEffectiveCommander(CombatGroup)

GetSupportPoints(CombatOrganization)
```

조직 시스템 자체가 Pawn의 전술적 행동을 결정해서는 안 된다.

---

# 22. 개발 단계

## Phase 1 — 조직 데이터

구현:

- `CombatOrganization`
- `CombatGroup`
- Parent/Child 트리
- Pawn ↔ Group 연결
- Role 할당
- 세이브/로드

목표:

```text
소대
└─ 분대
   └─ 화력조
```

같은 임의 깊이의 조직구조를 생성하고 Debug Inspector에서 확인할 수 있어야 한다.

---

## Phase 2 — 완편 Formation 생성

구현:

- `FormationDef`
- Formation별 정확한 정원
- Role Slot
- 완편 단위 Pawn 생성
- 편제 비용 계산
- Point Tolerance
- Formation 조합 선택
- 잔여 Support Point 계산

불완전 편제는 지원하지 않는다.

목표:

```text
Raid Points
↓
완편 Formation 조합
+
Support Points
```

으로 항상 변환되어야 한다.

---

## Phase 3 — 역할 배정

구현:

- CombatRole
- CommandRole
- 역할별 자격
- Formation Role Slot → Pawn 배정
- 장비/전문 역할과 지휘 역할 분리

---

## Phase 4 — 지휘권과 승계

구현:

- Commander
- CommandAuthority
- SuccessionPriority
- SuccessionList
- ActingRole
- 지휘관 사망/다운 감지
- 자동 승계

목표:

지휘관이 제거되었을 때 조직 트리에 정의된 규칙에 따라 정확한 후임자가 선택되어야 한다.

---

## Phase 5 — 지휘 상태

구현:

```text
Normal
CommanderLost
SuccessionPending
ActingCommander
CommandRestored
```

여기에 추후 사용할 수 있도록 다음 데이터도 마련한다.

```text
CommandDelay
CommandEfficiency
ParentCommandAvailable
```

단, 이것을 실제 Pawn 전술 AI에 적용하지는 않는다.

---

## Phase 6 — 외부 지원 기반

`SupportPointsRemaining`을 다른 시스템에서 조회하고 소비할 수 있는 인터페이스를 만든다.

```text
GetSupportPoints()

CanSpendSupportPoints(cost)

TrySpendSupportPoints(cost)

RefundSupportPoints(cost)
```

실제 포병/CAS/증원 등의 지원 시스템은 아직 구현하지 않는다.

---

# 23. 1차 MVP 완료 조건

첫 프로토타입은 **전술 AI 없이 조직 생성과 승계만 검증**한다.

예:

```text
Raid Points: 1,300
Squad Cost: 520
```

결과:

```text
Combat Organization

├─ 1st Squad
│  ├─ Squad Leader
│  ├─ Alpha Fireteam [4]
│  └─ Bravo Fireteam [4]
│
└─ 2nd Squad
   ├─ Squad Leader
   ├─ Alpha Fireteam [4]
   └─ Bravo Fireteam [4]

Formation Cost: 1,040
Support Points: 260
```

각 Pawn의 Debug Inspector에서는 다음을 확인할 수 있어야 한다.

```text
Organization: Raid #184
Unit: 1st Squad / Alpha
Combat Role: Rifleman
Command Role: None
Commander: Pawn A

Succession:
1. Pawn A
2. Pawn B
3. Pawn C
```

이후 Squad Leader를 강제로 사망/다운시키면 지정된 승계 규칙에 따라:

```text
Alpha Team Leader
→ Acting Squad Leader
```

가 되어야 한다.

여기까지 정상적으로 동작하면 **조직·편제·승계 시스템의 1차 기반 구현 완료**로 본다.

전술 AI는 이 기반 시스템이 안정화된 이후 별도 설계한다.