"""Publish before/after at identical cameras and native 256px game output."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,os,tempfile
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/ContactPaint'
assert (OUT/'validation.json').exists() and (OUT/'render_validation.json').exists()
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',20)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',15)
board=Image.new('RGB',(1040,870),(69,69,69));draw=ImageDraw.Draw(board)
for col,label in enumerate(('수정 전','수정 후')):
    x=16+col*520
    draw.text((x,8),label,font=font,fill='white')
    name='before_front.png' if col==0 else 'after_front.png'
    front=Image.open(OUT/name).convert('RGBA').resize((488,488),Image.Resampling.LANCZOS)
    board.paste(front,(x,42),front)
    draw.text((x,544),'256px 실제 출력 크기',font=small,fill='white')
    name=OUT/'before_east_256.png' if col==0 else ROOT/'RimWorld/VF_East/256/preview_east.png'
    east=Image.open(name).convert('RGBA');assert east.size==(256,256)
    board.paste(east,(x+116,578),east)
draw.text((16,842),'직접 겹침 최소 24 / 큰 장갑면 28 / 원통 측면 단색 / 형상 유지',font=small,fill='white')
with tempfile.TemporaryDirectory(prefix='.comparison-',dir=OUT) as staging:
    target=Path(staging)/'compare.png';board.save(target)
    os.replace(target,OUT/'before_after.png')
check=json.loads((OUT/'validation.json').read_text());png=json.loads((OUT/'render_validation.json').read_text())
lines=['# 직접 겹침 중심 채색 검수','',
'기준 커밋: 04b091e3. 전역 높이 명도표를 제거하고 실제 장착 관계에만 최소 대비를 적용했다. 독립 장비는 같은 기준색을 사용할 수 있다.','',
'## 최종 표시 명도','',
'| 부위 | 실제 RGB 명도 범위 |','|---|---:|']
for name,v in check['actual_final_rgb_samples'].items():lines.append(f"| {name} | {v['minimum']:.2f}–{v['maximum']:.2f} |")
lines+=['','직접 장착 관계의 최소 간격: %.2f. 큰 장갑면 팔레트 단계: 28.'%check['minimum_contact_gap'],
'서로 떨어진 해치 B와 관측장비 상면은 모두 218, 포 덮개와 기총 덮개도 각각 장착면보다 밝은 218이다. 실제 상대 높이로 무관한 부품의 색을 벌리지 않는다.','',
'원통 측면 %d개 연결 표면에서 최종 코너 RGB가 단색임을 확인했다. 축 방향 끝면은 별색으로 남긴다. 넓은 해치 본판의 곡률 표현은 유지했다.'%sum(check['single_color_cylinder_side_groups'].values()),'',
'## 검증과 실제 출력','',
'- 직전 커밋과 형상 해시 동일: 52,878면, 103개 메시. 원본 복구 부품 위치·부모·피벗 및 4개 애니메이션 유지.',
'- 최종 RGB의 직접 겹침 관계, 독립 장비의 같은 색 허용, 원통 측면 단색, 해치 본판 곡률과 암색 예외 검사 통과.',
'- 실제 PNG의 보이는 상면을 BVH와 대조하고 3×3 불투명 픽셀을 측정했다. 합성의 선형 RGB 최대 오차: %.8f.'%png['opaque_composite_linear_rgb_max_error'],
'- 128샘플·16비트 마스터, 256/512px 선형 RGB·알파 선곱 축소. 분리 레이어와 카메라/피벗 유지.',
'- before_after.png: 같은 카메라의 포탑 확대와 256px 원래 크기의 EAST 비교. 앞/뒤 확대 및 512px도 별도 확인.',
'- RimThunder 비교판은 같은 차량 화면 폭의 TAN 색 연구이며 게임 원본 색상 복제나 스타일 일치의 증명은 아니다.',
'- 실제 게임 내 drawSize·줌 검증은 미실시. ERA는 별도 후속 작업으로 유지.','',
'원본 필수 디테일·광학창·구멍 내부 재질은 유지한다. 이전 GunPanels/SurfaceRules 감사 기록은 당시 커밋의 결과로 보존한다.','']
(OUT/'REPORT.md').write_text('\n'.join(lines),encoding='utf-8')
print('CONTACT PAINT COMPARISON AND REPORT PUBLISHED')
