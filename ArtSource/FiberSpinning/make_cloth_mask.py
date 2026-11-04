"""Create the shared primary-color mask used over the vanilla Cloth stacks."""
from pathlib import Path
from PIL import Image
repo=Path(__file__).resolve().parent.parent.parent
path=repo/'Textures/Items/SyntheticFibers/ClothTintMask.png'
path.parent.mkdir(parents=True,exist_ok=True)
Image.new('RGBA',(2,2),(255,0,0,255)).save(path)
print(path)
