# 무전기 대화 오락 오류 수정

2026-11-03 사용자 로그의 `23:38:46` 오류는 `HD_ChatOnRadio`의 Social 오락과 `HD_Radio` 건물의 Television 오락이 일치하지 않는다는 내용이었다. 공용 전신 회사 창의 예외는 해당 로그에 없었다.

실제 1.7.4995 rev309 `JoyUtility.JoyTickCheckEnd`는 건물을 받으면 작업과 건물의 오락 종류를 대조하고 건물의 JoyGainFactor를 곱한다. 대화 드라이버는 건물 배율을 직접 넘기고 joySource는 null로 전달하도록 수정했다. 이로써 Social 대화, Television 청취, 건물 품질 배율을 모두 유지한다. 루머 1회 처리·전신 회사 서비스는 변경하지 않았다.

`dotnet run --project Source/Tests/Radio/Radio.Tests.csproj`: 57개 검사 통과. 대화 보상에 TV 건물을 넘기지 않으면서 1.35 배율을 보존하는 회귀 검사를 포함한다. 기존 검사의 전자부품 레시피 선택은 일괄 레시피와 혼동하지 않도록 1개 생산 일반 레시피를 선택한다. 실제 1.7 참조 제품 빌드는 경고·오류 0개다. 이 검사는 스텁 기반이며 전체 통신 UI의 실제 클릭 검증을 뜻하지 않는다.
