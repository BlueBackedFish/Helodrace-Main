"""Create a periodic square lattice from the original grille's two colors."""
from pathlib import Path
import numpy as np
from PIL import Image
HERE=Path(__file__).resolve().parent
size=128;bar_fraction=.30
axis=(np.arange(size)+.5)/size
line=np.minimum(axis,1-axis)<bar_fraction/2
mask=line[:,None]|line[None,:]
linear=np.where(mask[:,:,None],np.array([.19,.18,.14]),np.array([.09,.085,.063]))
srgb=np.where(linear<=.0031308,linear*12.92,1.055*linear**(1/2.4)-.055)
Image.fromarray(np.round(srgb*255).astype(np.uint8)).save(HERE/'grille_square_tile.png')
print('SQUARE GRILLE TILE CREATED',size,'px, 30% lattice width')
