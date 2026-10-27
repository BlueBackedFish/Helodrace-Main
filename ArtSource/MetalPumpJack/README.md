# Conventional steel pumpjack

Original circa-1950 steel pumping unit, active as `HD_MetalPumpJack`: 6x3 footprint, 1536px North/East/South/West color/mask pairs, drawSize (9,9), zero drawOffset, 64-degree downward orthographic cameras and fixed 12px outline. One model unit is one game tile. The curved horsehead, bridle, Sampson frame, walking beam, twin pitmans, counterweights, reducer and belt-driven electric motor distinguish it from the basic timber pump. Neutral iron with limited brass matches neighboring machines.

Gameplay is in `Defs/WildWest/Buildings/AdvancedOilExtraction.xml`: 1800W electric supply, nominal 100 crude oil per day before field quality/pressure, existing pressure depletion and developed-field placement requirement. Research: high oil development and basic electronics. It does not require a mechanical shaft network. English and Korean labels/descriptions are supplied.

Build in Blender with `--python ArtSource/MetalPumpJack/build_pumpjack.py`, then publish using `--python ArtSource/IndustrialMachines/normalize_density.py -- HD_MetalPumpJack`. Scene: `HD_MetalPumpJack.blend`; prefix: `Buildings/HD_MetalPumpJack`. See `validation.json` and `preview_four_views.png`. Source geometry and painted colors were retained when enabling the building. No in-game visual/play test has been performed.

Period-family guidance: [The History Center's circa-1944 Lufkin pumping-unit collection](https://www.thehistorycenteronline.com/collections/1944-pumping-unit-gear-assembly-floor-photographic-department-lufkin-foundry-machine-co-lufkin-texas-ca) and [ETHW pumpjack history](https://ethw.org/Pump_Jacks). This is an original game variant rather than a specific catalog-unit reconstruction.
