"""Publish the saved radio through the fixed tile-density building pipeline."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
from normalize_density import ASSETS,normalize
normalize(next(entry for entry in ASSETS if entry[0]=='HD_Radio'))
