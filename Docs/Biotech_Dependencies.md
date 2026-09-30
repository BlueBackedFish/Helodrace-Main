# 바이오테크 의존 지점

2026-10-01 기준, `About`, `Defs`, `Patches`, `Source/Helodrace`의 실제 로드 대상 파일을 확인했다. 이 문서는 현재 구현의 의존 관계를 기록한다. 기획 문서와 별도 호환 모드의 코드는 범위에 넣지 않았다.

## 결론

이 모드는 **바이오테크 필수**로 배포된다. [About.xml](../About/About.xml)의 `modDependencies`와 `loadAfter`에 `Ludeon.RimWorld.Biotech`가 있고, [README.md](../README.md)에도 필수라고 명시되어 있다. 아래의 `MayRequire`와 `ModsConfig.BiotechActive` 조건은 개별 정의·효과의 조건부 처리이며, 현재 배포 정책을 선택 사항으로 바꾸지는 않는다. 바이오테크 없이 모드 전체가 정상 동작하는지는 이 조사로 검증하지 않았다.

| 영역 | 바이오테크가 쓰이는 부분 | 근거 |
| --- | --- | --- |
| 제노타입·유전자 | 헤로드 5개 혈통은 `XenotypeDef`이며 모두 유전자를 보유한다. 자체 `GeneDef`는 BTX 의존, 명사수, 오만한 지도자, 극지 정밀, 독점가, 용병 결속 등 6개다. 멕시코 헤로드의 암페타민 의존 유전자 1개는 별도 BlancasDrugs 모드가 있을 때 추가된다. | [Xenotypes.xml](../Defs/Helod/Genetics/Xenotypes.xml), [Genes.xml](../Defs/Helod/Genetics/Genes.xml) |
| 폰 생성 | 일반 헤로드 폰 종류 6개와 대전쟁 병사 공통 종류에 5개 제노타입의 선택 비율이 정의되어 있다. 생성 패치는 강제 제노타입으로 헤로드 종족을 선택하고, 헤로드와 인간에게 섞이면 안 되는 제노타입을 걸러낸다. | [PawnKinds.xml](../Defs/Helod/Pawns/PawnKinds.xml), [PawnKinds_GreatWar.xml](../Defs/Helod/Pawns/PawnKinds_GreatWar.xml), [HelodPawnGeneration.cs](../Source/Helodrace/Helod/HelodPawnGeneration.cs) |
| 시작 시나리오 | `HD_Scenario_WWTech`가 `ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes`를 직접 사용하며, 매켄지·텍사스·북극·동부 헤로드를 각 1명씩 요구한다. 다섯 번째 멕시코 헤로드는 이 시나리오의 필수 시작 인원이 아니다. | [Scenarios_Helod.xml](../Defs/Scenarios/Scenarios_Helod.xml) |
| 종족 규칙 | 종족 설정에서 허용할 제노타입 5개와 금지 유전자·내인성 유전자 범주를 지정한다. C# 캐시와 `Pawn_GeneTracker.AddGene` 패치가 이를 실제 폰에 적용한다. | [HelodRaceSettings.xml](../Defs/Helod/Race/HelodRaceSettings.xml), [HelodRace.cs](../Source/Helodrace/Helod/HelodRace.cs) |
| BTX 생리 | 다섯 제노타입 모두 `HD_Gene_BTXDependency`를 가진다. `Gene_BTXAddiction`이 BTX 중독과 결핍을 유지하고, 섭취 처리에서는 활성 유전자를 조회해 BTX 필요량을 채운다. 화학물질·필요·건강 상태 정의 자체는 별도 XML에 있다. 시작 시나리오는 나프타 40개를 지급한다. | [Xenotypes.xml](../Defs/Helod/Genetics/Xenotypes.xml), [Gene_BTXAddiction.cs](../Source/Helodrace/BTX/Gene_BTXAddiction.cs), [BTXToxicity.cs](../Source/Helodrace/BTX/BTXToxicity.cs), [BTX.xml](../Defs/Helod/Health/BTX.xml), [Scenarios_Helod.xml](../Defs/Scenarios/Scenarios_Helod.xml) |
| 유전자 활용 능력 | 오만한 지도자 유전자는 기대치 단계에, 명사수 유전자는 비헤로드 사용자의 명사수 무기 모드 허용에 쓰인다. 헤로드의 명사수 모드 허용은 종족 판정으로도 처리한다. | [ModMain.cs](../Source/Helodrace/Core/ModMain.cs), [CompSharpshooterWeapon.cs](../Source/Helodrace/GreatWar/CompSharpshooterWeapon.cs) |
| 출생·성장 표현 | 종족 정의에 아기·어린이 생애 단계와 임신 기간이 있고, 생성·렌더링 코드가 아기/어린이 체형, 머리, 꼬리, 침대 위치를 처리한다. 바이오테크의 자녀 플레이와 맞닿는 부분이다. | [HelodRace.xml](../Defs/Helod/Race/HelodRace.xml), [HelodPawnGeneration.cs](../Source/Helodrace/Helod/HelodPawnGeneration.cs), [HelodRendering.cs](../Source/Helodrace/Helod/HelodRendering.cs) |
| 의상 호환 | 종족 의상 경로·허용 목록에 어린이 헬멧/롬퍼, 메카 제어·대역폭·독성 팩이 바이오테크 조건으로 들어 있다. `Apparel_Cape`는 로열티 또는 바이오테크가 있을 때 쓰는 별도 조건이다. | [HelodRaceSettings.xml](../Defs/Helod/Race/HelodRaceSettings.xml) |
| 오염·사운드 | 광염소계 핵포탄은 `ModsConfig.BiotechActive`일 때만 `PollutionUtility.GrowPollutionAt`으로 오염을 생성한다. 소이탄 사운드는 바이오테크의 `Shot_MiniFlameblaster`를 먼저 찾고 없으면 기본 사운드로 대체한다. 포탄의 나머지 폭발 처리와 사운드 대체 경로 자체는 이 조건과 분리되어 있다. | [Projectile_PhotochlorogenShell.cs](../Source/Helodrace/GreatWar/Projectile_PhotochlorogenShell.cs), [Projectile_IncendiaryGel.cs](../Source/Helodrace/GreatWar/Projectile_IncendiaryGel.cs) |

## 유지보수 시 확인할 점

- 필수 DLC 정책을 바꾼다면 메타데이터만 수정해서는 안 된다. 특히 시나리오의 제노타입 설정 부분, 폰 생성·유전자 Harmony 패치, BTX 유전자 경로를 함께 재설계해야 한다.
- `HD_Gene_AmphetamineDependency`의 XML 조건은 **BlancasDrugs**이다. 이는 바이오테크 필수 조건에 더해지는 선택 연동이며, 다른 6개 자체 유전자와 구분해야 한다.
- [CompSharpshooterWeapon.cs](../Source/Helodrace/GreatWar/CompSharpshooterWeapon.cs)와 [Projectile_IncendiaryGel.cs](../Source/Helodrace/GreatWar/Projectile_IncendiaryGel.cs)의 주석에는 바이오테크가 없어도 동작하도록 만든 시기의 설명이 남아 있다. 현재 메타데이터의 필수 정책과 혼동하지 않도록 해석해야 한다.
- 이 조사는 정적 파일 조사다. 실제 게임에서 바이오테크를 끈 상태의 로드 성공 여부나 모든 유전자 효과의 실행 결과를 판정하지 않는다.
