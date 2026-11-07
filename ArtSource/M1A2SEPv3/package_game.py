"""Package the M1A2 SEPv3 test vehicle for the Lobos Arsenal mod (plain Vehicle Framework).

Reads the built pipeline output (1024px master layers with boundary lines, intact damage overlays
composited back on: the plain test vehicle has no damage states yet), bakes the black
silhouette outline per layer, turns the top-down layers to face north (lossless 90 degree turn),
derives draw size / offsets from the M8 world scale and writes textures + generated defs.

python ArtSource/M1A2SEPv3/package_game.py --mod-root ../Helodrace-LobosArsenal
"""
from pathlib import Path
import argparse
import hashlib
import json
import sys
from PIL import Image

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
from outline_review import black_outline  # noqa: E402
from images import complete  # noqa: E402

# World scale shared with the M8 AGS runtime art: 16.2064 m camera width = 12.0615 tiles.
METRES_PER_TILE = 16.206403732299805/12.061538461538463
OUTLINE_WIDTH_256 = 2          # 8px at the 1024px master, same as the M3A3 runtime outline
DIRECTIONS = ("north", "east", "south", "west")
PREFIX = "HDA_M1A2SEPv3"
VEHICLE = PREFIX+"_MBT"  # ThingDef names must not end with a digit
TEXTURES = "Vehicles/M1A2SEPv3"


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def vec(*values):
    return "("+",".join(f"{v:.6f}" for v in values)+")"


def rotate_north_frame(x, z, direction):
    """Offset given in the north-facing frame (x right, z forward) for each vehicle facing."""
    return {"north": (x, z), "east": (z, -x), "south": (-x, -z), "west": (-z, x)}[direction]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mod-root", type=Path, required=True)
    args = parser.parse_args()
    out = ROOT/"output"
    report = json.loads((out/"report.json").read_text(encoding="utf-8"))
    if report["profile_sha256"] != sha(ROOT/"vehicle.json") or not report.get("postprocess_passed"):
        raise SystemExit("Build the current vehicle.json before packaging")
    render = report["render"]
    master = render["master"]
    draw_size = render["ortho_scale"]/METRES_PER_TILE
    tile = draw_size/master  # tiles per master pixel
    radius = OUTLINE_WIDTH_256*master/256
    dest = args.mod_root/"Textures"/TEXTURES
    dest.mkdir(parents=True, exist_ok=True)
    layout = {"draw_size": draw_size, "metres_per_tile": METRES_PER_TILE, "source_report_sha256": sha(out/"report.json"),
              "outline_px_at_1024": radius, "directions": {}, "textures": {}}

    def save(image, name):
        image.save(dest/(name+".png"))
        layout["textures"][name] = sha(dest/(name+".png"))

    for direction in DIRECTIONS:
        info = render["hull_directions"][direction]
        hull = black_outline(complete(out, f"hull_{direction}", render), radius)
        save(hull, f"vehicle_{direction}")
        hx, hy = info["hull_center_pixels"]
        ax, ay = info["turret_anchor_pixels"]
        layout["directions"][direction] = {
            # Shift the sprite so the hull body centre sits on the vehicle position.
            "draw_offset": ((master/2-hx)*tile, (hy-master/2)*tile),
            # Turret yaw axis relative to the vehicle position (screen x, z).
            "turret": ((ax-hx)*tile, (hy-ay)*tile)}
    # Top-down layers: east image (front to the right) turned 90 degrees CCW -> front up.
    for name, target in [("turret_full_east", "turret_north"), ("loader_mg_east", "loader_mg_north"),
                         ("crows_east", "crows_north")]:
        save(black_outline(complete(out, name, render), radius).transpose(Image.Transpose.ROTATE_90), target)
    preview = Image.open(out/"OutlinedReview"/str(master)/"preview_east.png").convert("RGBA")
    save(preview, "UI")
    # Child turrets: mount pixel in the east turret image -> north frame (x starboard, z forward).
    children = {}
    for key, spot in render["placements"].items():
        px, py = spot["pixels_in_turret_top_origin"]
        children[key] = ((py-master/2)*tile, (px-master/2)*tile)
    layout["children_north_frame"] = children
    (ROOT/"output"/"game_layout.json").write_text(json.dumps(layout, indent=2), encoding="utf-8")
    (args.mod_root/"Defs"/"Vehicles"/"M1A2SEPv3.xml").write_text(defs(layout), encoding="utf-8")
    print(f"M1A2SEPv3 packaged: drawSize {draw_size:.4f} tiles, textures -> {dest}")


def defs(layout):
    size = layout["draw_size"]
    d = layout["directions"]
    offsets = "\n".join(f"      <drawOffset{k.title()}>{vec(d[k]['draw_offset'][0], 0, d[k]['draw_offset'][1])}</drawOffset{k.title()}>"
                        for k in DIRECTIONS)
    turret_props = "\n".join(f"              <{k}>{vec(*d[k]['turret'])}</{k}>" for k in DIRECTIONS)

    def child_props(key):
        x, z = layout["children_north_frame"][key]
        return "\n".join(f"              <{k}>{vec(*rotate_north_frame(x, z, k))}</{k}>" for k in DIRECTIONS)

    graphic = lambda tex: f"""    <graphicData>
      <texPath>{TEXTURES}/{tex}</texPath>
      <graphicClass>Vehicles.Graphic_Turret</graphicClass>
      <shaderType>Cutout</shaderType>
      <drawSize>{vec(size, size)}</drawSize>
    </graphicData>"""
    return f"""<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by Helodrace-Main/ArtSource/M1A2SEPv3/package_game.py. Test vehicle: plain Vehicle
     Framework, no custom renderer. 105 mm rounds are reused until 120 mm ammunition exists. -->
<Defs>
  <Vehicles.VehicleDef ParentName="BaseVehiclePawn">
    <defName>{VEHICLE}</defName>
    <label>M1A2 SEPv3 (test)</label>
    <description>A test build of the M1A2 SEPv3 main battle tank with a 120 mm smoothbore gun, a loader's M240 on a shielded skate ring and a CROWS remote weapon station.</description>
    <graphicData>
      <texPath>{TEXTURES}/vehicle</texPath>
      <graphicClass>Vehicles.Graphic_Vehicle</graphicClass>
      <shaderType>Cutout</shaderType>
      <drawSize>{vec(size, size)}</drawSize>
{offsets}
    </graphicData>
    <uiIconPath>{TEXTURES}/UI</uiIconPath>
    <buildDef>{PREFIX}_Build</buildDef>
    <size>(3,6)</size>
    <fillPercent>1</fillPercent>
    <vehicleCategory>Combat</vehicleCategory>
    <type>Land</type>
    <techLevel>Spacer</techLevel>
    <combatPower>3000</combatPower>
    <statBases>
      <ArmorRating_Sharp>1.6</ArmorRating_Sharp>
      <ArmorRating_Blunt>0.8</ArmorRating_Blunt>
      <ArmorRating_Heat>0.8</ArmorRating_Heat>
      <MarketValue>90000</MarketValue>
    </statBases>
    <vehicleStats>
      <Mass>66000</Mass>
      <MoveSpeed>6</MoveSpeed>
      <CargoCapacity>800</CargoCapacity>
      <RepairRate>1</RepairRate>
    </vehicleStats>
    <properties>
      <diagonalRotation>true</diagonalRotation>
      <roles>
        <li>
          <key>driver</key>
          <label>driver</label>
          <slots>1</slots>
          <slotsToOperate>1</slotsToOperate>
          <handlingTypes>Movement</handlingTypes>
          <hitbox><cells><li>(0,2)</li></cells></hitbox>
        </li>
        <li>
          <key>gunner</key>
          <label>gunner</label>
          <slots>1</slots>
          <slotsToOperate>1</slotsToOperate>
          <handlingTypes>Turret</handlingTypes>
          <turretIds><li>main</li></turretIds>
          <hitbox><cells><li>(1,0)</li></cells></hitbox>
        </li>
        <li>
          <key>loader</key>
          <label>loader</label>
          <slots>1</slots>
          <slotsToOperate>1</slotsToOperate>
          <handlingTypes>Turret</handlingTypes>
          <turretIds><li>loaderMg</li></turretIds>
          <hitbox><cells><li>(-1,0)</li></cells></hitbox>
        </li>
        <li>
          <key>commander</key>
          <label>commander</label>
          <slots>1</slots>
          <slotsToOperate>1</slotsToOperate>
          <handlingTypes>Turret</handlingTypes>
          <turretIds><li>crows</li></turretIds>
          <hitbox><cells><li>(1,1)</li></cells></hitbox>
        </li>
      </roles>
    </properties>
    <components>
      <li>
        <key>hullFront</key>
        <label>front hull armor</label>
        <health>900</health>
        <depth>External</depth>
        <hitbox><from>(-1,2)</from><to>(1,2)</to></hitbox>
        <categories><li>BodyIntegrity</li></categories>
        <armor><ArmorRating_Sharp>1.9</ArmorRating_Sharp></armor>
      </li>
      <li>
        <key>hullSides</key>
        <label>side hull armor</label>
        <health>700</health>
        <depth>External</depth>
        <hitbox><from>(-1,-3)</from><to>(1,1)</to></hitbox>
        <categories><li>BodyIntegrity</li></categories>
        <armor><ArmorRating_Sharp>1.4</ArmorRating_Sharp></armor>
      </li>
      <li>
        <key>engine</key>
        <label>engine</label>
        <health>300</health>
        <depth>Internal</depth>
        <hitbox><from>(-1,-3)</from><to>(1,-2)</to></hitbox>
        <categories><li>MoveSpeed</li></categories>
      </li>
    </components>
    <comps>
      <li Class="Vehicles.CompProperties_VehicleTurrets">
        <turrets>
          <li>
            <key>main</key>
            <def>{PREFIX}_M256</def>
            <gizmoLabel>M256 120 mm</gizmoLabel>
            <drawLayer>1</drawLayer>
            <renderProperties>
{turret_props}
            </renderProperties>
          </li>
          <li>
            <key>loaderMg</key>
            <def>{PREFIX}_LoaderM240</def>
            <gizmoLabel>loader M240</gizmoLabel>
            <parentKey>main</parentKey>
            <drawLayer>2</drawLayer>
            <renderProperties>
{child_props("loader_mg")}
            </renderProperties>
          </li>
          <li>
            <key>crows</key>
            <def>{PREFIX}_CROWS</def>
            <gizmoLabel>CROWS M2</gizmoLabel>
            <parentKey>main</parentKey>
            <drawLayer>3</drawLayer>
            <renderProperties>
{child_props("crows")}
            </renderProperties>
          </li>
        </turrets>
      </li>
    </comps>
  </Vehicles.VehicleDef>

  <Vehicles.VehicleTurretDef>
    <defName>{PREFIX}_M256</defName>
    <label>M256 120 mm</label>
{graphic("turret_north")}
    <matchParentColor>false</matchParentColor>
    <turretType>Rotatable</turretType>
    <rotationSpeed>1</rotationSpeed>
    <maxRange>70</maxRange>
    <warmUpTimer>1.2</warmUpTimer>
    <reloadTimer>7</reloadTimer>
    <magazineCapacity>1</magazineCapacity>
    <ammunition>
      <thingDefs>
        <li>HDA_105mmAP_Round</li>
      </thingDefs>
    </ammunition>
    <genericAmmo>true</genericAmmo>
    <projectile>HDA_105mmAP_Projectile</projectile>
    <projectileOffset>0</projectileOffset>
    <shotSound>Shot_Autocannon</shotSound>
    <fireModes>
      <li>
        <label>single shot</label>
        <texPath>Vehicles/M3A3/Icons/FireBurst</texPath>
        <shotsPerBurst>1</shotsPerBurst>
        <ticksBetweenShots>420</ticksBetweenShots>
        <ticksBetweenBursts>420</ticksBetweenBursts>
      </li>
    </fireModes>
  </Vehicles.VehicleTurretDef>

  <Vehicles.VehicleTurretDef>
    <defName>{PREFIX}_LoaderM240</defName>
    <label>loader M240</label>
{graphic("loader_mg_north")}
    <matchParentColor>false</matchParentColor>
    <turretType>Rotatable</turretType>
    <rotationSpeed>2</rotationSpeed>
    <maxRange>35</maxRange>
    <warmUpTimer>0.6</warmUpTimer>
    <reloadTimer>4</reloadTimer>
    <magazineCapacity>200</magazineCapacity>
    <ammunition><thingDefs><li>HD_Ammunition_76251M80A1_Ammo</li></thingDefs></ammunition>
    <genericAmmo>true</genericAmmo>
    <projectile>HD_Bullet_76251M80A1_Proj</projectile>
    <projectileOffset>0</projectileOffset>
    <shotSound>Shot_AssaultRifle</shotSound>
    <fireModes>
      <li>
        <label>burst</label>
        <texPath>Vehicles/M3A3/Icons/FireBurst</texPath>
        <shotsPerBurst>8</shotsPerBurst>
        <ticksBetweenShots>6</ticksBetweenShots>
        <ticksBetweenBursts>30</ticksBetweenBursts>
      </li>
    </fireModes>
  </Vehicles.VehicleTurretDef>

  <Vehicles.VehicleTurretDef>
    <defName>{PREFIX}_CROWS</defName>
    <label>CROWS M2HB</label>
{graphic("crows_north")}
    <matchParentColor>false</matchParentColor>
    <turretType>Rotatable</turretType>
    <rotationSpeed>2</rotationSpeed>
    <maxRange>40</maxRange>
    <warmUpTimer>0.8</warmUpTimer>
    <reloadTimer>5</reloadTimer>
    <magazineCapacity>100</magazineCapacity>
    <ammunition><thingDefs><li>HDA_50BMG_Round</li></thingDefs></ammunition>
    <genericAmmo>true</genericAmmo>
    <projectile>HD_Bullet_12799M8_Proj</projectile>
    <projectileOffset>0</projectileOffset>
    <shotSound>Shot_AssaultRifle</shotSound>
    <fireModes>
      <li>
        <label>burst</label>
        <texPath>Vehicles/M3A3/Icons/FireBurst</texPath>
        <shotsPerBurst>8</shotsPerBurst>
        <ticksBetweenShots>8</ticksBetweenShots>
        <ticksBetweenBursts>30</ticksBetweenBursts>
      </li>
    </fireModes>
  </Vehicles.VehicleTurretDef>

  <Vehicles.VehicleBuildDef ParentName="VehicleBaseBuildable">
    <defName>{PREFIX}_Build</defName>
    <label>M1A2 SEPv3 (test)</label>
    <description>Build the M1A2 SEPv3 test vehicle.</description>
    <thingToSpawn>{VEHICLE}</thingToSpawn>
    <uiIconPath>{TEXTURES}/UI</uiIconPath>
    <graphicData>
      <texPath>{TEXTURES}/vehicle</texPath>
      <graphicClass>Graphic_Multi</graphicClass>
      <shaderType>Cutout</shaderType>
      <drawSize>{vec(size, size)}</drawSize>
{offsets}
    </graphicData>
    <size>(3,6)</size>
    <statBases>
      <MaxHitPoints>2400</MaxHitPoints>
      <WorkToBuild>240000</WorkToBuild>
      <Mass>66000</Mass>
      <Flammability>0</Flammability>
    </statBases>
    <costList>
      <Steel>1200</Steel>
      <Plasteel>300</Plasteel>
      <ComponentSpacer>12</ComponentSpacer>
    </costList>
  </Vehicles.VehicleBuildDef>
</Defs>
"""


if __name__ == "__main__":
    main()
