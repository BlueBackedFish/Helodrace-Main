"""Before/after display board and measured final-color report."""
from pathlib import Path
import json
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/HeightQuality'
board=Image.new('RGB',(1100,900),(79,79,79));d=ImageDraw.Draw(board)
d.text((25,15),'Before: part baselines override height',fill='white')
d.text((575,15),'After: height-first final paint',fill='white')
for row,pair in enumerate((
    (OUT/'before_east_512.png',ROOT/'RimWorld/VF_East/512/preview_east.png'),
    (OUT/'before_closeup.png',OUT/'after_front.png'))):
    for col,path in enumerate(pair):
        im=Image.open(path).convert('RGBA');box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox()
        im=im.crop(box);size=(490,round(im.height*490/im.width));im=im.resize(size,Image.Resampling.LANCZOS)
        board.paste(im,(25+550*col,75+row*365),im)
d.text((25,855),'Comparison images fitted to the same width. Game-size exports remain 256px / 512px.',fill='white')
board.save(OUT/'before_after.png')
small=Image.open(ROOT/'RimWorld/VF_East/256/preview_east.png').convert('RGBA')
alpha=Image.new('RGB',(808,300),(79,79,79));ad=ImageDraw.Draw(alpha)
for i,color in enumerate(((245,245,245),(126,126,126),(28,28,28))):
    tile=Image.new('RGBA',(256,256),(*color,255));tile.alpha_composite(small)
    alpha.paste(tile.convert('RGB'),(10+i*266,30));ad.text((10+i*266,8),str(color[0]),fill='white')
alpha.save(OUT/'alpha_backgrounds.png')
before=json.loads((OUT/'before_analysis.json').read_text());after=json.loads((OUT/'after_analysis.json').read_text())
pixels=json.loads((OUT/'render_validation.json').read_text())['actual_png_height_samples']
labels={'hull_roof':'차체 지붕','turret_roof':'포탑 지붕','hatch_03':'평평한 해치 03','hatch_02':'곡면 해치 02','commander_sight_top':'관측장치 상단'}
table=['| 부위 | 높이 | 이전 최종 색 | 수정 최종 색 | 실제 PNG |','|---|---:|---:|---:|---:|']
for key,label in labels.items():
    table.append(f"| {label} | {after['samples'][key]['height']:.3f} | {before['samples'][key]['final_display_luminance']:.1f} | {after['samples'][key]['final_display_luminance']:.1f} | {pixels[key]['actual_png_luminance']:.1f} |")
text='''# 높이 순서·텍스처 품질 검수 — 2026-10-23

이전 모델은 부품별 명도 기본값이 높이 배율을 압도했다. 지붕→포탑→곡면 해치가 211→204→197로 어두워졌는데도, 검사가 보정 계수만 확인해 통과했다.
이전 자료는 커밋 `63b47c69`의 모델/출력이다. `before_analysis.json`은 그때 최종 셰이더 색을 계산한 기록이다.

## 확인한 원인과 해결

- **밝기 역전:** 부품별 중앙값·높이·카메라 배율 중첩을 제거하고, 월드 높이에서 최종 표시 명도를 직접 계산했다. 일반 TAN 윗면에는 후속 배율·자동 홈 암색을 적용하지 않는다.
- **형태가 합쳐짐:** 벽·경사를 상면과 더 구분하고 정면/측면에도 차이를 뒀다. 같은 높이·기울기인 정면/측면 경사의 표시 명도 차이 검사를 추가했다.
- **곡면 색 끊김:** 법선 임계값과 명도 4단위 재질 양자화 대신 연속 코너 색을 사용했다. UV 이음새는 위치로 연결하고 실제 모서리는 남겼다.
- **해치 디테일 누락:** 해치 03이 원본 1,071면에서 188면으로 줄며 손잡이·힌지가 사라졌다. 해당 원본 그룹만 복구했다. 다른 형상·부모·피벗을 보존했다.
- **축소 손실:** sRGB 8비트 선곱 축소를 선형 RGB·float·알파 선곱 필터링으로 바꿨다. 밝은 선/투명 경계의 에너지와 RGB 번짐을 알려진 입력으로 검사한다.
- **출력 과정:** 128샘플/16비트 마스터와 256·512px 게임 크기 출력을 생성했다. 16비트 PNG를 Blender에서 선형 float로 읽을 때 중복 디코딩하지 않는다. 시험 중 검게 나온 그릴도 기존 패턴을 직접 연결해 복구했다.

## 실제 최종 명도

숫자는 sRGB 표시 RGB의 Rec.709 가중 명도(0~255)다. 부품별 면적 가중 평균과 실제 PNG의 가려지지 않은 안정적인 3×3 상면 패치 중앙값은 샘플 위치가 달라 약간 다르다.

'''+ '\n'.join(table)+'''

최종 Emission이 코너 RGB에서 직접 들어가는지, 저장된 RGB가 기록 명도와 같은지 검사했다. 실제 PNG는 BVH로 가림을 제외한 상면 패치를 검사하며 높이 순서와 원본 레이어→합성의 색 보존을 확인했다. 계수만 검사하는 이전 방식은 폐기했다.

## 시각/형상 검수

- `before_after.png`: 이전/현재 EAST와 포탑 확대를 같은 화면 폭으로 비교. 비교판 크기 조정은 게임 출력 해상도와 구분한다.
- `rimthunder_color_comparison.png`: 차체와 포탑을 각각 490px 폭으로 맞춘 RimThunder 비교. 왼쪽은 같은 TAN 틴트의 참고 연구 이미지이며 실제 게임 색 캡처가 아니다.
- `alpha_backgrounds.png`: 256px 출력을 밝은색·회색·어두운 배경에서 확인. 큰 검은 경계나 흰 후광 없음.
- `after_front.png`, `after_rear.png`: 해치·원본 연결부·연막탄·경사면 확대 확인. 앞/뒤 등각 렌더에서도 새로운 뚫림이나 부유 부품은 확인되지 않았다.
- 52,878면·103개 메시. 해치 03 외 형상 변경 없음. 원본 복구 부품 위치, 양수 면적, 계층·피벗, 4개 애니메이션 검증 통과.
- GLB는 최종 vertex color와 unlit 재질을 저장한다. 절차형 그릴은 단색 대체다. 게임 텍스처 기준은 BLEND 렌더 PNG다.

RimThunder의 굵은 외곽선과 일부 도해식 선 밀도까지 동일하게 만든 것은 아니다. 광학창·금속 포신·그릴·오목한 홈·후방 바구니 바닥·지정 손잡이 강조는 기록된 예외다. ERA는 보류했다. 실제 게임의 drawSize·줌에서는 아직 확인하지 않았다.
'''
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
print('HEIGHT QUALITY REPORT AND COMPARISONS PUBLISHED')
