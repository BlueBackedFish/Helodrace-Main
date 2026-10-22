"""Production entry point: role-driven turret rebuild over the frozen hull."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).resolve().parent/'rebuild_stage2_turret.py'),run_name='__main__')
