from pathlib import Path
import sys,json
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import image_export as e
out=REPO/'Textures/Items/HD_ClayPigeon.png'
result=e.export(e.opened(ROOT/'clay_raw.png'),out,256,12)
(ROOT/'clay_validation.json').write_text(json.dumps(dict(source='clay_pigeon.blend',export=result),indent=2))
print('CLAY_ITEM_EXPORT_PASS')
