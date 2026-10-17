# 총기 모딩 화면 조작과 그룹 정의

- 휠 또는 `−` / `+`: 커서 위치를 기준으로 25~800% 확대·축소. 버튼은 화면 중앙 기준.
- 가운데 버튼 드래그: 총기 화면 이동. `화면 초기화`: 현재 그룹에 맞는 100% 배율과 중앙 위치 복원.
- `보기`: 전체 조립체, 주 무기, 조준기 전체, 개별 조준기 그룹, 부착 무기 중 선택.
- 그룹을 선택하면 미리보기에는 해당 파츠만 표시하고 트리에는 상위 연결 경로를 흐리게 남긴다. 상위 파츠를 선택하면 전체 보기로 돌아간다.
- 트리의 파츠 이미지에 마우스를 올리면 확대 미리보기, 클릭하면 별도 이미지 창. 이미지 창도 휠·가운데 버튼 드래그를 지원한다.
- 파츠 이미지는 본체, `_Outline`, `additionalGraphics`를 합친 경계로 맞춘다. 테두리 파일이 없는 레이어는 검은 실루엣 테두리를 그린다. 원본 텍스처는 수정하지 않는다.

조준기 그룹은 기존 광축 판정의 실제 멤버를 사용한다. 가늠쇠·가늠자, 확대경, 비활성 조준기와 광축별 구분을 유지하며 조준기 자체에 달린 하위 파츠도 포함한다. 서로 다른 무기에 속한 조준기는 광축 높이가 같아도 보기 그룹을 나눈다.

독립적으로 보고 싶은 부착 무기의 `CompProperties_ModularWeaponNode`에 다음 값을 지정한다.

```xml
<isWeaponSubassembly>true</isWeaponSubassembly>
```

그 파츠와 하위 파츠가 별도 무기 그룹에 속한다. 더 아래에 다른 무기 그룹이 시작되면 해당 부분은 다시 독립한다. M203A2 본체에 적용했으므로 총열·트리거와 함께 `부착 무기` 메뉴에 나타난다. 이 표시는 모딩 화면의 그룹 소속을 정하며 발사·탄약 로직은 바꾸지 않는다.

검증:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj --no-restore -p:IntermediateOutputPath=obj/ModularWeaponNavigation/ -v minimal
dotnet run --project Source/Tests/ModularWeaponNavigation/ModularWeaponNavigation.Tests.csproj
```

자동 검증은 트리 탐색·그룹 소속·카메라 좌표·이미지 경계를 다룬다. 게임 내에서는 M203A2와 조준기가 달린 총기로 그룹 전환, 확대 후 파츠 클릭, 트리 이미지 팝업, 창 크기 변경을 확인한다.
