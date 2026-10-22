"""Scoped surface-mode correction and a repeatable art-review record."""
from pathlib import Path
import json,subprocess,os,tempfile
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/SurfaceRules'
old=json.loads(subprocess.check_output(['git','show','7956c9a3:ArtSource/M2A3Bradley/RimWorld/manifest.json'],encoding='utf-8'))
current=json.loads((ROOT/'RimWorld/manifest.json').read_text())
assert current['geometry_sha256']==old['geometry_sha256']
assert current['height_first_paint']['step_contrast']==old['height_first_paint']['step_contrast']
assert current['height_first_paint']['cel_shading']==old['height_first_paint']['cel_shading']
mode=json.loads((OUT/'mode_validation.json').read_text())
assert mode['all_non_lid_corner_colors_match_baseline']
pixels=json.loads((OUT/'render_validation.json').read_text())['actual_png_height_samples']
board=Image.new('RGB',(1100,900),(79,79,79));draw=ImageDraw.Draw(board)
draw.text((25,15),'Before | broad lids also forced into two tones',fill='white')
draw.text((575,15),'After | broad lid volume + unchanged small cel parts',fill='white')
for row,pair in enumerate(((OUT/'before_east_512.png',ROOT/'RimWorld/VF_East/512/preview_east.png'),(OUT/'before_front.png',OUT/'after_front.png'))):
    for col,path in enumerate(pair):
        im=Image.open(path).convert('RGBA');box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox()
        im=im.crop(box);im=im.resize((490,round(im.height*490/im.width)),Image.Resampling.LANCZOS)
        board.paste(im,(25+col*550,75+row*365),im)
draw.text((25,870),'Same geometry and all non-lid corner colors. Comparison fitted to equal visible widths.',fill='white')
with tempfile.TemporaryDirectory(prefix='.comparison-',dir=OUT) as staging:
    target=Path(staging)/'comparison.png';board.save(target)
    os.replace(target,OUT/'before_after.png')
summary={'baseline_commit':'7956c9a3','geometry_sha256':current['geometry_sha256'],'geometry_unchanged':True,
 'height_contrast_settings_unchanged':True,'small_cel_settings_unchanged':True,
 'all_non_lid_corner_colors_unchanged':True,'broad_lids':mode['broad_lids'],'actual_png_samples':pixels}
(OUT/'iteration_validation.json').write_text(json.dumps(summary,indent=2)+'\n')
report='''# 표면별 채색 규칙과 이번 반복 기록 — 2026-10-23

## 이번 수정

| 항목 | 기록 |
|---|---|
| 기준 결과 | 커밋 `7956c9a3`, `before_east_512.png`, `before_front.png` |
| 문제 | 넓은 해치 본판까지 두 색으로 나눠 볼록한 부피가 사라짐 |
| 근거 | 사용자가 넓은 본판은 이전 표현을 선호한다고 지정. 로컬 RimThunder M2A3 포탑의 넓은 볼록 해치에서도 완만한 명도 변화를 확인 |
| 대상 | 해치 역할 안에서 상면 투영 면적이 가장 큰 본판 연결 영역. 폴리곤 수로 선택하지 않음 |
| 변경 변수 | 해당 본판의 셀 채색 제외 + 높이·법선에 따른 부드러운 코너 색 보간 |
| 유지 변수 | 높이 단계 대비, 작은 힌지·손잡이·관측장치 측면의 두 색, TAN·재질·카메라·레이어·피벗·형상 |
| 범위 검증 | 본판 이외 모든 코너 RGB 해시가 기준 결과와 같음. 형상 해시도 동일 |
| 256px | 큰 해치가 볼록하게 읽히며 작은 부속의 두 색과 구분됨 |
| 512px | 넓은 본판의 곡률 명도가 복원되고 원통 부속에 여러 색 띠를 다시 넣지 않음 |
| 남은 차이 | RimThunder의 굵은 외곽선·일부 도해식 선 밀도와 원본 M2A3/M3A3 형식 차이. 이번에는 수정 범위에 포함하지 않음 |

## 후속 반복에 적용할 규칙

공통 지침 `ArtSource/VEHICLE_ART_STYLE.md` v1.8의 표면 유형 표와 7단계 반복 절차가 현재 기준이다. 다음 회차 기록은 `ArtSource/VEHICLE_ART_BRIEF_TEMPLATE.md`의 반복 채색 표를 사용한다.

1. 넓은 평판은 정돈된 면 색, 넓은 볼록 본판은 부드러운 부피, 작은 힌지·원통은 2색, 광학창·금속·홈은 별도 재질로 구분한다.
2. 부품 사이의 높이 단계와 본판 내부의 곡률을 분리한다. 본판 안의 작은 명도 변화마다 최소 간격을 강제하지 않는다.
3. 기준 PNG/설정을 보존하고 문제 부위·레퍼런스 근거를 먼저 정한다. 회차마다 해당 표면의 한 변수군만 바꾼다.
4. 같은 방향·본체 화면 폭으로 RimThunder와 비교한다. 긴 포신이나 투명 캔버스를 크기 기준으로 삼지 않는다. 256px 식별력 → 512px 색 띠/이음새 순으로 판단한다.
5. 최종 PNG 높이 순서와 다른 부위의 색, 형상·피벗·레이어를 검사하고 전후 비교를 남긴다. 한 부위의 결론을 모든 곡면에 일괄 적용하지 않는다.

## 비교 자료와 검증

`before_after.png`는 전후 EAST/포탑 확대다. `rimthunder_color_comparison.png`는 긴 포신을 제외한 차체/포탑 본체를 각각 490px 폭으로 맞춘다. 레퍼런스는 같은 TAN 틴트를 입힌 참고 연구 이미지이며 실제 게임 색 캡처가 아니다. 원본 파일과 해시는 `comparison_metrics.json`에 기록한다. 분리 차체 레이어의 포탑 링 개구부는 포탑 레이어로 덮이는 구조이며, 합성 EAST에서는 가려진다.

`mode_validation.json`은 넓은 본판과 작은 부속의 구분, `iteration_validation.json`은 형상/기존 설정 보존, `render_validation.json`은 실제 PNG와 합성 색을 검증한다. 52,878면·103개 메시, 원본 부품·부모와 4개 애니메이션, 투명 배경·그림자 없음·ERA 보류를 유지한다.

256px/512px 렌더 검수는 완료했다. 실제 게임 drawSize·줌 검수는 미실시다. 이번 결과를 자동으로 최종 승인된 RimThunder 스타일 표준으로 간주하지 않는다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
print('SURFACE RULES COMPARISON, SCOPE AND ITERATION REPORT PASSED')
