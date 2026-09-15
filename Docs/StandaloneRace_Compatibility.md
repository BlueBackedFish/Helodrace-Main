# HAR 제거 후 호환 모듈 수정

## 원인

헬로드는 여전히 `race/intelligence=Humanlike`인 일반 `Pawn`이다. 바닐라 `Pawn.IsColonist`는 플레이어 소속, Humanlike, 노예/비인간화 상태를 확인하며 `defName=Human`을 요구하지 않는다.

CE 기즈모 누락의 직접 원인은 별도 호환 모듈의 XML 선택자였다. 메인 모드는 `AlienRace.ThingDef_AlienRace`에서 `ThingDef`로 전환됐지만 CE와 Facial Animation 호환 모듈은 이전 요소 이름을 참조하고 있었다. CE의 첫 종족 확장 추가 작업부터 실패해 이후 컴포넌트 추가도 실행되지 않았다.

설치된 CE의 `CompPawnGizmo.CompGetGizmosExtra`는 장착 무기의 `CompRangedGizmoGiver` 명령을 반환한다. 이 컴포넌트가 종족에 붙지 않으면 무기가 정상이어도 해당 경로의 명령이 나타나지 않는다.

## 수정 파일 (별도 저장소)

- `../Helodrace-CombatExtended/Patches/CombatExtended_HelodRaceAndArmor.xml`: Helod 대상 XPath 6개를 `/Defs/ThingDef[defName="Helod"]` 기준으로 변경.
- `../Helodrace-Facial/Patches/Helodrace/FacialAnimationComps_Helod.xml`: 같은 유형의 XPath 3개 변경.

헬로드의 종족 정체성, 신체, 정착민 판정, 귀/꼬리, 기존 전투 수치는 변경하지 않았다. 인간에게 붙은 모든 타 모드 컴포넌트를 무조건 복제하지도 않는다. 그러한 방식은 중복 컴포넌트와 종족별 동작 충돌을 만들 수 있다.

## 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Source/Verify-RaceCompatibility.ps1
```

테스트는 설치된 게임 DLL의 실제 표준 `PatchOperation.Apply`를 실행한다. 게임 밖 테스트 프로세스에서만 DeepProfiler를 끈다.

- 이전 XPath의 CE 패치 실패 재현.
- 전체 CE 종족/방어구 패치와 Facial 컴포넌트 패치를 양쪽 순서로 적용 성공.
- CE 기즈모·탄약·제압 컴포넌트, Facial 핵심 컴포넌트가 한 번씩 추가됨을 확인.
- 자체 종족 확장 보존, CE Humanoid 피격 형태, CE 근접 도구 3개 확인.
- CE, Facial, blancasdrugs, LobosArsenal 네 동반 모듈의 활성 XML/C#/프로젝트 파일에서 HAR 참조 없음 확인.

이는 XML 적용 검증이며 실제 게임에서 기즈모를 클릭한 검증은 아니다. 모든 외부 모드의 호환성을 보증하지 않는다. 추가 모드에서 이전 HAR 타입을 직접 참조한다면 해당 호환 패치도 전환해야 한다.

## 적용

메인 DLL만 갱신하는 것으로는 해결되지 않는다. 위 두 **호환 모듈의 수정된 XML**을 게임이 읽는 모드 폴더에도 반영하고 게임을 완전히 재시작해야 한다. 이 작업에서는 소스 저장소만 수정했으며 설치된 모드 폴더와 세이브는 덮어쓰지 않았다.

기즈모 확인은 플레이어 정착민에게 CE 지원 원거리 무기를 장착한 상태에서 진행한다. 개발자 로그에서 위 두 XML 패치 실패가 사라졌는지도 확인한다. 다른 파일의 오류(예: 별도 모듈러 무기 패치 실패)는 이 종족 XPath 수정과 구분해서 진단해야 한다.
