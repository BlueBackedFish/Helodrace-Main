"""Final, scoped gun-cover/coax-cover contrast comparison."""
from pathlib import Path
from PIL import Image,ImageDraw
import json,subprocess,tempfile,os
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/GunPanels'
before=json.loads(subprocess.check_output(['git','show','6a7ab6f6:ArtSource/M2A3Bradley/RimWorld/manifest.json'],encoding='utf-8'))
current=json.loads((ROOT/'RimWorld/manifest.json').read_text())
assert before['geometry_sha256']==current['geometry_sha256']
settings=dict(current['height_first_paint']);settings.pop('local_raised_panels')
assert settings==before['height_first_paint']
check=json.loads((OUT/'validation.json').read_text());assert check['all_other_corner_colors_identical'] and 'actual_png_samples' in check
summary={'baseline_commit':'6a7ab6f6','geometry_unchanged':True,'geometry_sha256':current['geometry_sha256'],'existing_global_paint_settings_unchanged':True,'all_non_target_colors_unchanged':True,'target_faces':79}
(OUT/'scope_validation.json').write_text(json.dumps(summary,indent=2)+'\n')
board=Image.new('RGB',(1040,370),(79,79,79));draw=ImageDraw.Draw(board)
for column,name in enumerate(('before_front','after_front')):
    im=Image.open(OUT/(name+'.png')).convert('RGBA').crop((330,420,545,545))
    im=im.resize((480,279),Image.Resampling.LANCZOS)
    x=20+column*520;board.paste(im,(x,45),im)
    draw.text((x,15),'Before' if column==0 else 'After | raised covers separated',fill='white')
draw.text((20,340),'Zoomed comparison only. Geometry, aperture material and every other painted corner remain unchanged.',fill='white')
with tempfile.TemporaryDirectory(prefix='.comparison-',dir=OUT) as staging:
    path=Path(staging)/'comparison.png';board.save(path);os.replace(path,OUT/'before_after.png')
coax=check['actual_png_samples']['WT_bone_gun']['actual_png_luminance'];cover=check['actual_png_samples']['WT_ex_decor_01']['actual_png_luminance']
report=f'''# 포 덮개·기총 주변 덮개 단차 마무리

기준: 커밋 `6a7ab6f6`. 형상은 유지하고, 실제 미세 단차가 색에서도 읽히도록 두 부위만 조정했다.

- 기총 주변 덮개: `WT_bone_gun`의 원본 829~906면, 기존 셀 표면. 2색 사이 간격 30을 유지하며 밝은 쪽을 약 202로 조정.
- 포 덮개: `WT_ex_decor_01`의 윗판 0번 면. 평균 명도 약 208, 완만한 색 변화 약 207~210을 유지.
- 기준 지붕 명도 약 196에서 두 미세 단계 간격을 6으로 설정. 더 높은 해치 기준 약 212보다 낮게 제한해 높이 순서를 유지.
- 구멍 내부·기총 금속·광학창 재질, 다른 덮개 면과 모든 해치/관측장치 색은 그대로 유지.

실제 저장 PNG에서 기총 덮개 상단 {coax:.1f}, 포 덮개 {cover:.1f}로 확인했다. `validation.json`은 대상 **79면 이외 모든 코너 색이 이전과 같음**을 검증한다. `scope_validation.json`은 전역 채색 설정과 형상 해시가 그대로임을 확인한다.

`before_after.png`는 두 부위의 확대 비교이며 게임 크기 출력이 아니다. 256px/512px EAST, 앞/뒤 확대, 높이 순서·알파 합성·52,878면/103개 메시·4개 애니메이션 검증을 통과했다. 실제 게임 내 drawSize·줌 검수는 미실시다.
'''
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
print('SCOPED GUN COVER COMPARISON AND GEOMETRY/PALETTE VALIDATION PASSED')
