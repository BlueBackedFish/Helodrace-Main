"""Production entry point: preserve pre-stage-two geometry and recolor only."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).resolve().parent/'recolor_bradley.py'),run_name='__main__')
