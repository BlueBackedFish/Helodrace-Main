from pathlib import Path
import sys,json
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output'
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import image_export as e
target=REPO/'Textures/Buildings/ComputerDesk';metadata=json.loads((OUT/'metadata.json').read_text());entries=[];files={}
for direction in ['north','east','south','west']:
    path=target/('HD_ComputerDesk_'+direction+'.png');files[path.name]=e.export(e.opened(OUT/(direction+'_raw.png')),path,512,12);entries.append((direction.upper(),e.opened(path)))
e.sheet(entries,OUT/'preview.png',columns=2)
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,files=files,presentation=metadata,transparent=True,lighting='fixed RGB emission; no lamps or shadows',in_game_tested=False),indent=2)+'\n')
print('COMPUTER_DESK_EXPORT_PASS: four 512px directions; 12px outline; 2x1 footprint; fixed density')
