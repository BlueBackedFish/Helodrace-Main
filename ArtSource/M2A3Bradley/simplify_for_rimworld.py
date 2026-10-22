"""Production entry point: targeted source-part restoration, flat cover and height-led paint."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).resolve().parent/'recolor_bradley.py'),run_name='__main__')
