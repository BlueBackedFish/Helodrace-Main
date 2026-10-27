# 1970s machining center

Original 3x3 vertical machining center, circa 1972-1978. A coarse six-position automatic tool-change carousel, broad T-slot table, vertical spindle, partial chip guards and compact CRT console distinguish it from the earlier tape-controlled mill. Tool sockets are represented by six large silhouettes; miniature tooling and enclosure segmentation are omitted.

`build_center.py` uses `../LaterIndustry/machine_common.py` and the established fixed industrial palette. It exports an editable `HD_MachiningCenter1970.blend`, four actual 64-degree orthographic cameras, 768px color/mask pairs and fixed 12px outlines. drawSize is (4.5,4.5); any camera headroom shift is compensated in `validation.json`. Geometry and fixed colors are identical in all views. Guards remain open in this sprite-proxy service presentation so the work zone is readable.

The active electric worktable uses 3500W and work-speed factor 3.5, retains the early NC mill's recipes and supplies a higher-tier milling facility to firearm benches. Research: industrial automation. No unattended production is simulated.

Period-family guidance: [Makino's machining-center history](https://ir.makino.co.jp/en/company/history/) and [Mazak's company history](https://www.mazak.com/uk-en/about-us/company-history/). This is an original compact variant rather than an exact catalog reconstruction. No in-game visual/play test has been performed.
