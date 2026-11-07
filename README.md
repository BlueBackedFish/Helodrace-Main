# Helodrace-Main
RimWorld 1.6 race mod. Requires Harmony and Biotech; Humanoid Alien Races is no longer required.

HAR 제거 구현, 백업 위치, 검증 결과와 남은 게임 테스트는 [제거 기록](Archive/HAR-Removal.md)을 참고하세요. 기존 HAR 세이브의 호환 변환은 제공하지 않습니다.

## Facial Animation

Facial Animation(`Nals.FacialAnimation`) 호환은 본편에 내장되어 있습니다. `LoadFolders.xml`이 해당 모드가 활성일 때만 `Compat/FacialAnimation`을 읽습니다. C# 소스는 `Source/HelodraceFacial`, 원본 PSD는 `ArtSource/FacialAnimation`에 있습니다. 별도 `Helodrace - Facial Animation` 모드는 더 이상 필요하지 않으며 함께 켜면 중복 적용됩니다.
