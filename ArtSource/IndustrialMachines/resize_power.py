"""Normalize one saved building, preserving its geometry and painted materials."""
import os,sys
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__)))
from normalize_density import ASSETS,normalize
name=sys.argv[sys.argv.index('--')+1]
normalize(next(entry for entry in ASSETS if name in entry[:2]))
