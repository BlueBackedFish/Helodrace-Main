# Helodrace-Main
RimWorld 1.6 race mod. Requires Harmony and Biotech; Humanoid Alien Races is no longer required.

HAR 제거 구현, 백업 위치, 검증 결과와 남은 게임 테스트는 [제거 기록](Archive/HAR-Removal.md)을 참고하세요. 기존 HAR 세이브의 호환 변환은 제공하지 않습니다.

## Facial Animation

Facial Animation(`Nals.FacialAnimation`) 호환은 본편에 내장되어 있습니다. `LoadFolders.xml`이 해당 모드가 활성일 때만 `Compat/FacialAnimation`을 읽습니다. C# 소스는 `Source/HelodraceFacial`, 원본 PSD는 `ArtSource/FacialAnimation`에 있습니다. 별도 `Helodrace - Facial Animation` 모드는 더 이상 필요하지 않으며 함께 켜면 중복 적용됩니다.

## Gunplay (Forked)

Gunplay (Forked)(`assssssqwww.gunplay`)가 활성이면 `Compat/Gunplay`를 읽습니다. 혼합 탄띠의 예광탄(`HD_Projectile_20mmHEIT`, `HD_Bullet_12799M20_Proj`)만 붉게 발광하는 궤적을 남깁니다. 원본 Gunplay(`automatic.gunplay`)는 1.5까지만 지원해 1.7 본편과 함께 쓸 수 없으므로 대상에서 제외했습니다.
