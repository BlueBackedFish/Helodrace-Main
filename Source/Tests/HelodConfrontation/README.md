# 헤로드 대치 검증

계산 및 XML 연결 검사:

```powershell
dotnet run --project Source/Tests/HelodConfrontation/HelodConfrontation.Tests.csproj -- 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld'
```

`RuntimeSmoke.csproj`는 실제 RimWorld 안에서 실행하는 별도 테스트 모드의 DLL이다. 일반 모드의 Assemblies에는 넣지 않는다. 별도 게임 실행 폴더와 `-savedatafolder`를 사용하고, Harmony → Core/DLC → Helodrace → 테스트 모드 순서로 활성화한다. `HelodAssembly` MSBuild 속성으로 검증할 Helodrace.dll 경로를 지정할 수 있다. 테스트 모드의 packageId는 `helodrace.confrontation.smoke`로 지정한다.

`-batchmode -quicktest -logFile "<로그 경로>"`로 실행한다. RimWorld가 로딩 중 그래픽 자원을 사용하므로 `-nographics`는 사용하지 않는다. 기존 플레이 세이브나 사용자 모드 설정을 사용하지 않는다.

테스트는 실제 맵에 헤로드 세 명을 생성하고 대치 진입, 중복 진입 억제, 대치 작업, 공격 억제, 중재자의 이동 및 중재 시간 누적을 검사한다. 게임 전체를 `HelodConfrontationSmoke`라는 이름으로 저장하고 다시 불러와 참가자·중재자 참조와 진행 시간을 확인한다. 이후 결과 추첨만 테스트용 Harmony 패치로 고정해 화해·해산·바닐라 싸움의 실제 경로와 기억, 취소·디스폰 정리, 이종족 싸움의 바닐라 유지를 검사한다. 완료하거나 실패하면 해당 테스트 게임 프로세스를 종료한다.

성공 로그:

- `HELOD_CONFRONTATION_START_PASS`
- `HELOD_CONFRONTATION_SAVE_PASS`
- `HELOD_CONFRONTATION_OUTCOMES_PASS`

실패하면 `HELOD_CONFRONTATION_FAIL`과 예외를 기록한다.

2026-10-25 검증: RimWorld 1.6.4871 rev590, Core 및 전체 DLC와 Harmony를 활성화한 독립 실행에서 세 성공 로그를 모두 확인했다. 전체 모드 및 런타임 테스트 DLL 빌드는 경고·오류 0개이며, 계산·정의 검사의 75개 시나리오도 통과했다.

# 동작과 조정값

헤로드끼리의 `StartSocialFight`를 대치로 바꾼다. 시작 불가능하거나 이미 정신 상태에 있는 대상에게는 새 대치를 겹쳐 걸지 않는다. 한쪽만 헤로드이면 원래 사회적 다툼으로 진행한다. 대치가 싸움으로 결정되면 재진입 방지 범위 안에서 원래 `StartSocialFight`를 호출하므로 바닐라 정신 상태·Job·종료 기억·신체 부위 보호와 기존 헤로드 피해 제한이 적용된다.

설정 위치는 `Defs/Helod/Race/HelodRaceSettings.xml`의 `confrontation`이다.

| 값 | 기본값 | 의미 |
|---|---:|---|
| mediatorRadius | 6 | 둘 중 한 명 기준 주변 중재자 탐색 반경 |
| mediatorJoinChance | 0.45 | 후보마다 적용하는 기본 참여 확률. 무드·사교·친절함·직설적 성격으로 보정 |
| durationTicks | 360~600 | 대치 지속 시간. 정상 속도 기준 약 6~10초 |
| fullMediationTicks | 120 | 중재 효과가 온전히 반영되기 위한 실제 현장 중재 시간 |

중재자는 양쪽과 같은 세력이며 깨어 있고 대화·이동이 가능해야 한다. 징집·정신 상태·포로·노예·강제 작업·다른 중재 및 중단하기 어려운 작업 중인 폰은 제외한다. 시작 시 후보를 한 번 확인하고 무작위 순서로 참여를 추첨해 최대 한 명을 보낸다. 시야와 도달 가능성을 확인하며, 실제 현장에서 대화하는 동안만 중재 시간을 누적한다. 중재자가 떠나거나 작업이 취소되면 결과 판정 시 중재 효과가 사라진다.

양쪽의 무드(더 낮은 쪽을 추가 반영), 상호 의견, 직설적·피의 갈망·친절함 특성과 중재 시간·중재자의 사교·무드가 세 결과의 가중치를 정한다. 어떤 조건에서도 세 결과 모두 가능하다. 판정은 낮은 thingID의 참가자가 한 번만 수행한다.

- 화해: 상대에게 받은 모욕·무시 기억을 지우고 양쪽에 의견 +12, 5일의 화해 기억을 준다.
- 해산: 관계 기억을 추가하지 않고 대치를 종료한다. 기존 모욕·무시 기억은 유지한다.
- 싸움: 원래 바닐라 사회적 다툼을 시작한다.

한쪽이 쓰러지거나 죽거나 디스폰하거나 다른 정신 상태로 바뀌면 대치를 취소하고 상대와 중재 작업을 정리한다. 취소에는 화해·싸움 기억을 주지 않는다. 정상 대치 도중 두 참가자의 거리가 8칸을 넘어서도 해산 처리한다.
