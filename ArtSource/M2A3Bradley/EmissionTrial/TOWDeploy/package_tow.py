"""Publish game-sized TOW layers and a readable deployment preview."""
from pathlib import Path
import json
import sys
import tempfile
import os
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))
from export_bradley_lowres import resize_linear

NAMES = [
    'turret_base_top_east',
    'turret_stowed_top_east', 'turret_deployed_top_east',
    'tow_stowed_top_east', 'tow_deployed_top_east',
    'preview_stowed_east', 'preview_deployed_east',
]
BG = (58, 63, 68, 255)
FONT = 'C:/Windows/Fonts/segoeui.ttf'


def backdrop(source):
    result = Image.new('RGBA', source.size, BG)
    result.alpha_composite(source.convert('RGBA'))
    return result.convert('RGB')


def main():
    master = json.loads((HERE / 'render_validation.json').read_text())['pivot']
    report = {}
    with tempfile.TemporaryDirectory(prefix='.package-', dir=HERE) as staging:
        stage = Path(staging)
        for size in (256, 512):
            target = stage / str(size)
            target.mkdir()
            for name in NAMES:
                result = resize_linear(Image.open(HERE / (name + '.png')), size)
                bounds = result.getbbox()
                assert bounds and all((bounds[0] > 0, bounds[1] > 0,
                                       bounds[2] < size, bounds[3] < size)), (name, bounds)
                result.save(target / (name + '.png'))
            pivots = {
                'size': [size, size], 'top_layer_pivot': [size / 2, size / 2],
                'hull_anchor': [round(v * size / 1024, 3) for v in master['hull_anchor']],
                'hull_elevation': 64, 'turret_elevation': 90,
                'coordinates': 'pixels, top-left origin',
                'layer_order': ['hull', 'turret_base_top_east', 'tow_STATE_top_east'],
                'alternative': 'turret_STATE_top_east replaces base + moving layers',
                'resize': 'linear-light float premultiplied Lanczos',
            }
            (target / 'pivots.json').write_text(json.dumps(pivots, indent=2) + '\n')
            report[str(size)] = {'rgba_layers': len(NAMES), 'unclipped': True, 'pivots': pivots}

        board = Image.new('RGB', (1064, 868), BG[:3])
        draw = ImageDraw.Draw(board)
        heading = ImageFont.truetype(FONT, 25)
        caption = ImageFont.truetype(FONT, 19)
        for i, state in enumerate(('stowed', 'deployed')):
            x = 12 + 532 * i
            draw.text((x + 10, 12), state.upper() + (' / 0 deg' if i == 0 else ' / -90 deg'),
                      font=heading, fill=(238, 234, 220))
            board.paste(backdrop(resize_linear(Image.open(HERE / (state + '.png')), 512)), (x, 48))
            board.paste(backdrop(Image.open(stage / '256' / ('preview_' + state + '_east.png'))),
                        (x + 128, 586))
            draw.text((x + 100, 558), 'EAST composite / native 256px',
                      font=caption, fill=(219, 220, 222))
        board.save(stage / 'stowed_deployed.png')

        frames = [backdrop(Image.open(p)) for p in sorted((HERE / 'Motion').glob('*.png'))]
        assert len(frames) == 11
        frames[0].save(stage / 'tow_deploy.gif', save_all=True, append_images=frames[1:],
                       duration=[700, 110, 110, 110, 110, 500, 200, 110, 110, 110, 600],
                       loop=0, disposal=2)
        with Image.open(stage / 'tow_deploy.gif') as gif:
            assert gif.size == (384, 384) and gif.n_frames >= 9
            report['motion_preview'] = {'size': list(gif.size), 'frames': gif.n_frames, 'loop': 0}
        (stage / 'package_validation.json').write_text(json.dumps(report, indent=2) + '\n')
        for path in stage.rglob('*'):
            if path.is_file():
                target = HERE / path.relative_to(stage)
                target.parent.mkdir(parents=True, exist_ok=True)
                os.replace(path, target)
    print('TOW 256/512 layers, pivots and motion preview published')


if __name__ == '__main__':
    main()
