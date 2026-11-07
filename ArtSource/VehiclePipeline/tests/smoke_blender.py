"""A second vehicle with unrelated names: end-to-end Blender integration fixture.

Run with the same external Python as pipeline.py. Generated files stay in ignored
.codex-staging; the script prints their path for visual review.
"""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import shutil

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from pipeline import read_json, write_json


def main():
    staging = ROOT.parents[1]/".codex-staging"
    staging.mkdir(exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="vehicle-pipeline-", dir=staging))
    vertices, groups = [], []

    def box(name, low, high):
        start = len(vertices)+1
        x, y, z = low
        X, Y, Z = high
        vertices.extend([(x, y, z), (X, y, z), (X, Y, z), (x, Y, z),
                         (x, y, Z), (X, y, Z), (X, Y, Z), (x, Y, Z)])
        faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4],
                 [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        groups.append((name, [[start+i for i in face] for face in faces]))

    box("chassis", (-1.3, -2.8, .3), (1.3, 2.6, 1.2))
    box("cupola", (-.8, -.4, 1.2), (.8, 1.0, 1.9))
    box("lid", (-.5, -.2, 1.9), (.1, .5, 2.0))
    box("grip", (-.4, 0, 2.0), (-.1, .1, 2.12))
    box("pipe", (-.12, -3.1, 1.5), (.12, -.4, 1.75))
    box("back_gate", (-1, 2.6, .4), (1, 2.7, 1.1))
    # One planar grille, copied UVs are also tested by the regular OBJ parser unit test.
    start = len(vertices)+1
    vertices += [(.3, 1.3, 1.201), (1.1, 1.3, 1.201), (1.1, 2.3, 1.201), (.3, 2.3, 1.201)]
    groups.append(("air_intake", [[start+i for i in range(4)]]))
    source = directory/"source.obj"
    text = ["v "+" ".join(map(str, v)) for v in vertices]
    for name, faces in groups:
        text.append("g "+name)
        text += ["f "+" ".join(map(str, f)) for f in faces]
    source.write_text("\n".join(text)+"\n", encoding="utf-8")
    blueprint = Image.new("RGB", (600, 300), "white")
    draw = ImageDraw.Draw(blueprint)
    draw.text((15, 15), "Synthetic test fixture / not a real vehicle blueprint", fill="black")
    draw.rectangle((60, 110, 540, 240), outline="black", width=3)
    draw.rectangle((240, 55, 400, 110), outline="black", width=3)
    blueprint.save(directory/"blueprint.png")
    project = directory/"Fixture"
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "prepare", "--obj", str(source),
                    "--blueprint", str(directory/"blueprint.png"), "--output", str(project),
                    "--forward=-y", "--up=z"], check=True)
    path = project/"vehicle.json"
    profile = read_json(path)
    profile["axes"]["confirmed"] = True
    for i, part in enumerate(profile["parts"]):
        part["confirmed"] = True
        part["layer"] = "turret" if i in {1, 2, 3} else "barrel" if i == 4 else "hull"
    profile["parts"][4].update(mode="cylinder", cylinder_axis=[0, 1, 0], side=84, cap=84, top=84, role="metal")
    profile["parts"][6].update(mode="flat", grid={"cell_pixels": 6, "bar_fraction": .3})
    profile["contacts"] = [{"lower": "g0001", "upper": "g0002"}, {"lower": "g0002", "upper": "g0003"}]
    profile["rigs"] = [
        {"id": "turret_yaw", "usage": "turret_yaw", "pivot": [0, .3, 1.2], "axis": "z",
         "parts": ["g0001", "g0002", "g0003"], "confirmed": True, "animate": True, "angle": 30},
        {"id": "elevation", "parent": "turret_yaw", "pivot": [0, -.4, 1.6], "axis": "x",
         "parts": ["g0004"], "confirmed": True, "animate": True, "angle": 12},
        {"id": "ramp", "pivot": [0, 2.6, .4], "axis": "x", "parts": ["g0005"],
         "confirmed": True, "animate": True, "angle": 65}]
    write_json(path, profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(path)], check=True)
    report = read_json(project/"output/report.json")
    assert report["source_faces"] == report["preserved_faces"] == 37
    assert report["mesh_count"] == 7
    assert report["paint"]["g0001"]["top"] == 168
    assert report["paint"]["g0002"]["top"] == 192
    assert report["paint"]["g0003"]["top"] == 216
    assert report["unlit_glb"] and report["postprocess_passed"]
    assert report["lighting_max_difference_255"] == 0
    assert report["glb_animation_count"] == 3 and report["rig_demo_angles_and_rest_return"]
    assert [edge["actual_upward_gap"] for edge in report["contact_checks"]] == [24, 24]
    # Mounted cupola/lid/grip get part-boundary lines on the turret roof below them.
    assert report["boundary_lines"]["turret_east"]["pixels"] > 0
    assert (project/"output/turret_east_flat.png").exists() and (project/"output/turret_east_ids.npz").exists()
    assert report["status"] == "draft" and not report["game_verified"]
    # Regression: authored top levels can be correct while an explicit surface override
    # reverses the actual roof relationship. This must fail before publishing a report.
    bad = directory/"BadContact"
    bad.mkdir()
    shutil.copyfile(project/"style.json", bad/"style.json")
    profile["parts"][2]["planes"] = {"roof": 120}
    write_json(bad/"vehicle.json", profile)
    rejected = subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(bad/"vehicle.json")],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    assert rejected.returncode != 0 and "Actual upward paint reverses/weakens contact" in rejected.stdout
    assert not (bad/"output/report.json").exists()
    print("Rejected an actual roof-color inversion despite valid authored top levels")
    # RimThunder-style contacts: base at the top of the ladder, darker mounted lid,
    # equal-top grip separated only by its darker walls.
    profile["parts"][2].pop("planes")
    profile["parts"][1]["top"] = 216
    profile["contacts"] = [{"lower": "g0001", "upper": "g0002", "direction": "down"},
                           {"lower": "g0002", "upper": "g0003", "direction": "same"}]
    directional = directory/"DirectionalContact"
    directional.mkdir()
    shutil.copyfile(project/"style.json", directional/"style.json")
    write_json(directional/"vehicle.json", profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(directional/"vehicle.json")], check=True)
    report = read_json(directional/"output/report.json")
    assert [report["paint"][k]["top"] for k in ["g0001", "g0002", "g0003"]] == [216, 192, 192]
    checks = report["contact_checks"]
    assert checks[0]["direction"] == "down" and checks[0]["actual_downward_gap"] == 24
    assert checks[1]["direction"] == "same" and checks[1]["actual_wall_gap"] >= 24
    weak = directory/"WeakSameWall"
    weak.mkdir()
    shutil.copyfile(project/"style.json", weak/"style.json")
    profile["parts"][3]["planes"] = {"front_wall": 180, "side_wall": 180}
    write_json(weak/"vehicle.json", profile)
    rejected = subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(weak/"vehicle.json")],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    assert rejected.returncode != 0 and "'same' contact wall is not a step darker" in rejected.stdout
    assert not (weak/"output/report.json").exists()
    print("Built down/same contacts and rejected an equal-top part without a darker wall")
    # Recorded simplification + highlight exception.
    profile["parts"][3].pop("planes")
    profile["contacts"] = [{"lower": "g0001", "upper": "g0002", "direction": "down"}]
    profile["parts"][3]["accent"] = {"value": 240, "over": "g0002", "reason": "small grip highlight"}
    profile["parts"][5].update(action="box", reason="test clean block", confirmed=True)
    simplified = directory/"BoxAccent"
    simplified.mkdir()
    shutil.copyfile(project/"style.json", simplified/"style.json")
    write_json(simplified/"vehicle.json", profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(simplified/"vehicle.json")], check=True)
    report = read_json(simplified/"output/report.json")
    assert report["box_replaced_faces"] == 6 and report["boxes"][0]["part"] == "g0005"
    assert report["preserved_faces"]+report["box_replaced_faces"]+report["explicitly_omitted_faces"] == 37
    assert report["accents"][0]["gap_over_base"] == 48 and report["paint"]["g0003"]["top"] == 240
    # Large curved surface: fixed corner gradient, brighter where the smoothed normal faces up.
    profile["parts"][4].update(mode="flat", smooth={"low": 96, "high": 192})
    profile["parts"][4].pop("cylinder_axis", None)
    write_json(simplified/"vehicle.json", profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(simplified/"vehicle.json")], check=True)
    report = read_json(simplified/"output/report.json")
    shades = report["paint"]["g0004"]["luminances"]
    assert min(map(float, shades)) >= 96 and max(map(float, shades)) <= 192 and len(shades) > 1
    # signed range reaches below the mid value on down-facing faces.
    profile["parts"][4]["smooth"]["signed"] = True
    # Separate weapon sprite + external-asset mount point; boundary lines stay one solid colour.
    profile["parts"][2]["layer"] = "lid_mg"
    profile["weapon_layers"] = [{"id": "lid_mg", "pivot": [-.2, .15, 1.95]}]
    profile["mounts"] = [{"id": "roof_rws", "pivot": [.5, .5, 1.9], "asset": "ArtSource/Example", "reason": "test"}]
    write_json(simplified/"vehicle.json", profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(simplified/"vehicle.json")], check=True)
    report = read_json(simplified/"output/report.json")
    assert min(map(float, report["paint"]["g0004"]["luminances"])) < 144
    placements = report["render"]["placements"]
    assert placements["lid_mg"]["kind"] == "weapon_layer" and placements["roof_rws"]["kind"] == "mount"
    for size in (256, 512):
        assert (simplified/f"output/{size}/lid_mg_east.png").exists()
    lines = Image.open(simplified/"output/turret_east_lines.png").convert("F").resize((256, 256), Image.Resampling.BOX)
    turret = Image.open(simplified/"output/256/turret_east.png").convert("RGBA")
    full = {turret.getpixel((x, y))[:3] for x in range(256) for y in range(256)
            if lines.getpixel((x, y)) >= 254.5 and turret.getpixel((x, y))[3] > 0}
    partial = [(x, y) for x in range(256) for y in range(256) if 10 < lines.getpixel((x, y)) < 245]
    # Fully covered pixels are exactly the one line colour; edges are anti-aliased, not stepped.
    assert len(full) <= 1, full
    assert partial and len({turret.getpixel(p)[:3] for p in partial}) > 1
    # An existing separately built model placed into a weapon layer with one uniform scale/yaw/move.
    profile["imported_models"] = [{"id": "rws", "blend": str(project/"output/vehicle.blend"), "layer": "lid_mg",
                                   "scale": .1, "rotation_z_degrees": 180, "location": [-.2, .15, 2.0],
                                   "reason": "test import"}]
    write_json(simplified/"vehicle.json", profile)
    subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(simplified/"vehicle.json")], check=True)
    report = read_json(simplified/"output/report.json")
    assert report["imported_models"]["objects"] and report["source_faces"] == 37
    profile.pop("imported_models")
    profile["parts"][3]["accent"]["value"] = 200
    write_json(simplified/"vehicle.json", profile)
    rejected = subprocess.run([sys.executable, str(ROOT/"pipeline.py"), "build", str(simplified/"vehicle.json")],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    assert rejected.returncode != 0 and "is not a full step above its base" in rejected.stdout
    print("Built a box replacement and an accent; rejected an accent without a full step")
    print("SMOKE_OUTPUT="+str(project))


if __name__ == "__main__":
    main()
