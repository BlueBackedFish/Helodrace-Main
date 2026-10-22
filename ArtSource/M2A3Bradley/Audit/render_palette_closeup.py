"""New palette closeups, preserving the earlier geometry-repair audit images."""
import runpy,sys
from pathlib import Path
HERE=Path(__file__).resolve().parent
sys.argv.append('--after')
runpy.run_path(str(HERE/'inspect_turret_followup.py'),run_name='__main__',
    init_globals={'OUTPUT_DIR':HERE/'PaletteMatch'})
