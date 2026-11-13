"""Re-fit the optional independent ERA kit onto the source-derived geometry."""
import os,runpy
HERE=os.path.dirname(os.path.abspath(__file__))
runpy.run_path(os.path.join(HERE,'..','build_era.py'),init_globals={
    'SOURCE_BLEND':os.path.join(HERE,'m3a3_bradley_refit.blend'),
    'OUTPUT_DIR':os.path.join(HERE,'ERA'),'RENDER_OUTPUT':False})
