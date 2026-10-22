"""Compare minimum contrast and two-tone curves with the previous paint."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,subprocess
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/CelContrast'
baseline=json.loads(subprocess.check_output(['git','show','e28f5f01:ArtSource/M2A3Bradley/RimWorld/recolor_validation.json'],encoding='utf-8'))
current=json.loads((ROOT/'RimWorld/recolor_validation.json').read_text())
old_manifest=json.loads(subprocess.check_output(['git','show','e28f5f01:ArtSource/M2A3Bradley/RimWorld/manifest.json'],encoding='utf-8'))
manifest=json.loads((ROOT/'RimWorld/manifest.json').read_text())
assert old_manifest['geometry_sha256']==manifest['geometry_sha256']
geometry={'baseline_commit':'e28f5f01','geometry_sha256':manifest['geometry_sha256'],'faces':manifest['faces'],'meshes':manifest['meshes'],'paint_only_geometry_unchanged':True}
(OUT/'geometry_validation.json').write_text(json.dumps(geometry,indent=2)+'\n')
(OUT/'before_analysis.json').write_text(json.dumps({'commit':'e28f5f01','samples':baseline['height_order_samples']},indent=2)+'\n')
board=Image.new('RGB',(1100,900),(79,79,79));d=ImageDraw.Draw(board)
d.text((25,15),'Before | continuous curved shading',fill='white')
d.text((575,15),'After | stronger small steps + two-tone curves',fill='white')
for row,pair in enumerate(((OUT/'before_east_512.png',ROOT/'RimWorld/VF_East/512/preview_east.png'),(OUT/'before_front.png',OUT/'after_front.png'))):
    for col,path in enumerate(pair):
        im=Image.open(path).convert('RGBA');box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox()
        im=im.crop(box);im=im.resize((490,round(im.height*490/im.width)),Image.Resampling.LANCZOS)
        board.paste(im,(25+550*col,75+row*365),im)
d.text((25,870),'Comparison fitted to equal visible widths; game exports remain 256px / 512px.',fill='white')
board.save(OUT/'before_after.png')
pixels=json.loads((OUT/'render_validation.json').read_text())['actual_png_height_samples']
labels={'hull_roof':'차체 지붕','turret_roof':'포탑 지붕','hatch_03':'평평한 해치','hatch_02':'곡면 해치','commander_sight_top':'관측장치 상단'}
table=['| 부위 | 이전 색 | 수정 색 | 실제 PNG |','|---|---:|---:|---:|']
for key,label in labels.items():
    table.append(f"| {label} | {baseline['height_order_samples'][key]['final_display_luminance']:.1f} | {current['height_order_samples'][key]['final_display_luminance']:.1f} | {pixels[key]['actual_png_luminance']:.1f} |")
cells=current['curved_patches_use_two_flat_colors']
report='''# 최소 단계 대비·2구간 셀 채색 — 2026-10-23

기존 형상은 그대로 유지하고 채색만 수정했다. 기준은 이전 커밋 `e28f5f01`이며 `geometry_validation.json`에서 형상 해시·52,878면·103개 메시가 같음을 확인했다.

## 대비 응답

주요 상판 높이에서 얻은 기존 명도 차이에 `14 + 3×log(1+차이/8)`을 적용했다. 작은 차이에는 높은 확대율을, 큰 차이에는 완만한 증가율을 준다. 기준 단계의 출력 간격은 약 16~18이며 최고 기준 명도 246을 유지해 아래쪽 색을 낮추는 방향으로 조정한다.

대표 상면의 최종 색에서 포탑 지붕→평평한 해치 차이는 약 8→16, 두 해치 사이 차이는 약 5→20으로 늘었다. 곡면 해치→관측장치의 큰 차이는 약 20→14로 줄었다. 모든 꼭짓점의 무한히 작은 차이에 고정 간격을 준다는 뜻은 아니며, 주요 부품 단계의 구분을 위한 값이다.

## 곡면 처리

힌지·관측장치의 둥근 측면·곡면 해치·포신 등의 연결 표면을 찾고 실제 모서리에서 나눴다. 표면마다 최대 두 개의 고정색만 사용하며 밝음/어두움 간격은 30이다. 힌지의 끝마개는 같은 부품의 두 색으로 묶는다. 오목한 홈은 그중 어두운 색을 쓴다. 유리·금속 등 다른 재질은 별도다.

메시 코너 색에서 각 셀 표면의 색이 최대 2개인지, 삼각형 내부의 코너 색이 동일한지 검사했다. 방향상 한 구간만 존재하는 표면은 한 색으로 남는다. 최종 PNG 경계의 안티앨리어싱은 두 색을 혼합할 수 있으나, 표면 안에 연속 색 띠를 넣지 않는다.

## 최종 명도

숫자는 sRGB 표시 RGB의 Rec.709 가중 명도(0~255)다. 면적 평균과 PNG의 보이는 안정적인 상면 패치 중앙값은 샘플 위치가 달라 약간 차이가 난다.

'''+ '\n'.join(table)+f'''

셀 채색 적용 대상은 {len(cells)}개 메시, {sum(v['patches'] for v in cells.values())}개 연결 표면이다. 상면 높이 순서, 두 색 제한, 색 간격, 투명 PNG 합성, 원본 형상·계층과 4개 애니메이션 검증을 통과했다. 256px·512px 출력을 확인했다.

`before_after.png`는 동일 화면 폭으로 맞춘 전후 EAST/포탑 비교다. `after_front.png`·`after_rear.png`는 확대 검수, `render_validation.json`은 실제 PNG 명도 검사다. 카메라·레이어 피벗·투명 배경·무광원/그림자 없음·ERA 보류를 유지했다. 실제 게임 내 drawSize·줌 검수는 미실시다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
print('CEL CONTRAST REPORT AND COMPARISON PUBLISHED')
