# 총구화염 모양 설정

파츠 ThingDef의 comps 안에 있는
li Class="Helodrace.ModernWar.CompProperties_ModularWeaponNode"에 추가합니다.

```xml
<muzzleEffectKind>MuzzleBrake</muzzleEffectKind>
```

- Bare: 총열 기본형
- FlashHider: 소염기형
- MuzzleBrake: 머즐브레이크형
- Suppressor: 소음기형
- Auto (생략 시 기본값): 소음기 소켓은 소음기형, 일반 총구 파츠는
  소염기형, 총구 파츠가 없으면 총열 기본형

기존 파츠에는 값을 명시했습니다. 모양은 파츠 이름으로 분류하지 않습니다.
크기 계수, 화염 억제, 끝점 보정은 별도이며 이번 변경 대상이 아닙니다.
내장 편집기의 XML 출력에도 명시한 값이 포함됩니다.
XML 변경 후 게임을 재시작해야 합니다.
