"""Compose C-17, F-22 and F-15EX at one measured physical scale."""
from pathlib import Path
import hashlib
import json
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent
OUT = ROOT / 'output'
sys.path.insert(0, str(ART / 'VehiclePipeline'))
from images import resize_linear, font
sys.path.insert(0, str(ART / 'PropAssets'))
from image_export import outline

PPM = 22
BG = (57, 65, 74, 255)
TEXT = (235, 240, 245)
MUTED = (184, 199, 212)
board = Image.new('RGBA', (2600, 1600), BG)
draw = ImageDraw.Draw(board)
draw.text((65, 32), 'C-17 / F-22 / F-15EX', font=font(40), fill=TEXT)
draw.text((65, 92), 'C-17 기준 실제 크기 비교 · 모든 기체 동일 축척 · 22 px = 1 m',
          font=font(24), fill=MUTED)
draw.line((65, 144, 2535, 144), fill=(105, 124, 139), width=2)
for x0, x1 in [(110, 1260), (1370, 1900), (1960, 2490)]:
    for x in range(x0, x1 + 1, 110):
        draw.line((x, 225, x, 1410), fill=(67, 77, 87))
    for y in range(240, 1411, 110):
        draw.line((x0, y, x1, y), fill=(67, 77, 87))
draw.line((1310, 165, 1310, 1430), fill=(105, 124, 139), width=2)
records = []
c17 = json.loads((ART / 'C17Globemaster/output/model_validation.json').read_text())
f22 = json.loads((ART / 'F22Raptor/output/model_validation.json').read_text())
f15 = json.loads((ART / 'F15EXEagleII/output/model_validation.json').read_text())
c17_mpu = 53 / (c17['used_bounds'][1][1] - c17['used_bounds'][0][1])
f22_mpu = 18.9 / (f22['source_pose_bounds'][1][0] - f22['source_pose_bounds'][0][0])


def place(name, pose, plain_path, outlined_path, viewport_m, length, center, nose_y):
    plain = Image.open(plain_path).convert('RGBA')
    c17_pixels_per_meter = 3243 / (c17['ortho_scale'] * c17_mpu)
    roll_pixels_per_meter = plain.width / viewport_m
    source = Image.open(outlined_path).convert('RGBA') if outlined_path else outline(
        plain, 12 * roll_pixels_per_meter / c17_pixels_per_meter)
    # Outline is the same physical width as the established C-17 12px stroke.
    side = round(viewport_m * PPM)
    small = resize_linear(source, side)
    measured = resize_linear(plain, side)
    yy, xx = np.where(np.asarray(measured)[:, :, 3] >= 128)
    assert len(yy)
    raster_length = (int(yy.max()) - int(yy.min()) + 1) / PPM
    # Edge-on zero-thickness nozzle/wing faces can disappear at exactly 90°.
    # Keep the camera's physical scale; never stretch that silhouette to fit.
    tolerance = .12 if pose == 'roll 0' else .03 * length
    assert abs(raster_length - length) < tolerance, (name, pose, raster_length)
    assert min(small.getbbox()[0:2]) > 0
    left = round(center - (int(xx.min()) + int(xx.max())) / 2)
    top = nose_y - int(yy.min())
    assert left >= 0 and top >= 0 and left + side <= board.width and top + side <= board.height
    board.alpha_composite(small, (left, top))
    records.append(dict(aircraft=name, pose=pose, source=str(plain_path.relative_to(ART.parent)),
                        source_sha256=hashlib.sha256(plain_path.read_bytes()).hexdigest(),
                        viewport_m=viewport_m, canvas_pixels=side, nominal_length_m=length,
                        raster_length_m=raster_length, scale_pixels_per_meter=PPM,
                        body_bbox_pixels=[left + int(xx.min()), top + int(yy.min()),
                                          left + int(xx.max()) + 1, top + int(yy.max()) + 1]))


place('C-17', 'roll 0', ART / 'C17Globemaster/output/Textures/Plain/HD_C17.png',
      ART / 'C17Globemaster/output/HD_C17.png', c17['ortho_scale'] * c17_mpu, 53, 685, 240)
draw.text((120, 170), 'C-17 Globemaster III', font=font(29), fill=TEXT)
draw.text((120, 207), '길이 53 m / 날개폭 51.75 m', font=font(19), fill=MUTED)
for name, folder, meta, mpu, length, span, center in [
    ('F-22 Raptor', 'F22Raptor', f22, f22_mpu, 18.9, 13.6, 1635),
    ('F-15EX Eagle II', 'F15EXEagleII', f15, 1, 19.4, 13.0, 2225),
]:
    short = 'F22' if folder == 'F22Raptor' else 'F15EX'
    base = ART / folder / 'output'
    place(name, 'roll 0', base / ('HD_' + short + '_plain.png'),
          base / ('HD_' + short + '.png'), meta['ortho_scale'] * mpu, length, center, 240)
    place(name, 'roll +90', OUT / (short + '_roll90_plain.png'), None,
          24, length, center, 900)
    x = center - 235
    draw.text((x, 170), name, font=font(29), fill=TEXT)
    draw.text((x, 207), '상면 / roll 0°', font=font(19), fill=MUTED)
    draw.text((x, 700), f'길이 {length:g} m / 날개폭 {span:g} m', font=font(21), fill=TEXT)
    draw.text((x, 830), 'roll +90° / 기체 앞뒤 축 회전', font=font(22), fill=TEXT)
    draw.text((x, 1360), '위 상면도와 동일 축척', font=font(19), fill=MUTED)

draw.line((110, 240, 2490, 240), fill=(126, 151, 171), width=1)
draw.text((110, 1430), '길이 비율   C-17 100%   /   F-22 35.66%   /   F-15EX 36.60%',
          font=font(24), fill=TEXT)
for i in range(2):
    draw.rectangle((2060 + i * 110, 1440, 2170 + i * 110, 1452),
                   fill=TEXT if i == 0 else (127, 153, 174))
for i, label in enumerate(['0', '5', '10 m']):
    draw.text((2055 + i * 110, 1460), label, font=font(18), fill=MUTED)
draw.text((65, 1500), '기존 모델의 형상과 색상 유지 · 원근 없는 직교 투영 · 격자 5 m',
          font=font(21), fill=MUTED)
draw.text((65, 1537), 'F-15EX 모델 날개폭 13.19 m (제원 13.0 m 대비 +1.5%); 길이 기준 균일 배율 적용.',
          font=font(19), fill=MUTED)
board.convert('RGB').save(OUT / 'C17_F22_F15EX_real_scale_roll90.png')
roll = json.loads((OUT / 'roll_validation.json').read_text())
assert roll['passed']
(OUT / 'comparison_validation.json').write_text(json.dumps(dict(
    passed=True, pixels_per_meter=PPM, reference='C-17 length 53 m; existing 19-tile artwork',
    independent_axis_stretch=False, aircraft=records,
    f15ex_model_span_m=f15['model_wingspan_m'], f15ex_nominal_span_m=13.0,
    in_game_tested=False), indent=2) + '\n', encoding='utf-8')
print('AIRCRAFT_SCALE_PREVIEW_PASS: five views at 22 px/m; top lengths within 0.12 m; true roll bounds preserved')
