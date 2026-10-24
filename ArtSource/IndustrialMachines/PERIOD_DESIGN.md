# 1900–1950 industrial redesign

User direction, 2026-10-25: redesign all five machines as equipment from roughly 1900–1950. Old sprite layouts are historical references, not shape constraints for this pass. Keep the five existing building identities and footprints; generator and motor remain separate. The large steam engine must have part of its flywheel below the floor.

| Asset | Chosen visual family | Main readable features |
|---|---|---|
| Small steam engine, 2×2 | c.1900–1920 workshop steam plant | compact horizontal riveted boiler, side-loading firebox, short stack, single side flywheel and small front working cylinder |
| Large steam engine, 4×3 | c.1900–1920 horizontal compound mill engine | tandem cylinders, low shaft, large wheel entering a recessed foundation pit, exposed crosshead, brass governor |
| Diesel engine, 4×3 | c.1920–1940 stationary multi-cylinder oil engine | three upright cylinder heads, common crankcase, end flywheel, overhead exhaust manifold, cooling-water tank and injection pipes |
| Generator, 3×3 | c.1900–1925 belt-driven DC dynamo | arched field yokes, copper field coils, exposed armature/commutator, carbon brushes and separate bearing pedestals |
| Motor, 3×3 | c.1920–1940 cast-frame electric motor | smooth cast stator, heavy vented end shields, outboard bearings, small side terminal box, belt-output pulley |

The finished objects are original sprite-proxy designs guided by these period families, not exact museum reconstructions. Fixed muted foundry paint, machined iron, brass and copper regions replace the earlier nearly-white scheme. Cast iron remains selected in material masks; concrete, deep pit interiors and accent materials keep their fixed colors. No PBR reflections, lighting, AO, rust noise, logos or fine realistic clutter. Shapes and functional groupings take priority over bolts.

Primary historical references consulted:

- [Museum Wales, Tangye horizontal single-cylinder engine](https://museum.wales/collections/online/object/07514a80-8bb5-3d29-be26-8d23c82223d0/Horizontal-single-cylinder-steam-engine/): stationary engine used for electricity generation around 1900; cylinder, crosshead, outboard bearing, flywheel and governor family.
- [Powerhouse, Bower horizontal engine](https://collection.powerhouse.com.au/object/208440): earlier 1880–1900 example documenting a slot in the cast base plate for a flywheel extending below it. This supports the requested pit construction; it is not claimed as a 1920 model.
- [Visvesvaraya Industrial and Technological Museum, Science Park](https://www.vismuseum.gov.in/science-park.php): 1926 Marshall tandem-compound stationary engine.
- [National Steam Centre, diesel collection](https://melbournesteam.com.au/exhibits/diesel/): c.1928 three-cylinder air-blast diesel and other stationary oil-engine examples.
- [Science Museum Group, Crompton archive finding aid](https://archives.sciencemuseumgroup.ac.uk/Documents/SCM/Finding%20Aids/Named%20Archives/CRO.pdf): dated catalog entries for open-type generators with pedestal bearings (1918–1924), protected motors and three-phase induction motors in the 1920s. These establish the period families; exact catalog dimensions are not inferred.

Presentation stays at 64° downward orthographic elevation, North/East/South, 512px transparent color/mask pairs and 256px review sheets. Each model is built and checked independently. Game behavior and footprint are unchanged; in-game testing remains separate from render/definition validation.
