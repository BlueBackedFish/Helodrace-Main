from pathlib import Path
import urllib.request
ROOT=Path(__file__).resolve().parent/'Reference';ROOT.mkdir(exist_ok=True)
url='https://upload.wikimedia.org/wikipedia/commons/2/27/Browning1917.jpg'
request=urllib.request.Request(url,headers={'User-Agent':'Helodrace-art-reference/1.0 (local game asset research)'})
with urllib.request.urlopen(request,timeout=45) as response:(ROOT/'M1917A1_USArmy.jpg').write_bytes(response.read())
print('Saved US Army Center for Military History public-domain exterior reference.')
