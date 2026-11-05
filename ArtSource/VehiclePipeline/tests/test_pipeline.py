import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pipeline import (read_obj, transform, partition, paint_levels, paint_rgb,
                      components, dot, cross, read_json, prepare, ROOT)
from images import resize_linear, composite_linear
from PIL import Image


class PipelineTests(unittest.TestCase):
    def test_obj_only_intake_does_not_invent_blueprint_or_mark_review_complete(self):
        staging = ROOT.parents[1]/".codex-staging"
        staging.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=staging) as directory:
            source = Path(directory)/"source.obj"
            source.write_text("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", encoding="utf-8")
            target = Path(directory)/"project"
            with patch("images.parts_atlas"):
                prepare(SimpleNamespace(output=target, obj=source, blueprint=None,
                                        name="OBJ-only test", forward="-y", up="z", scale=1))
            profile = read_json(target/"vehicle.json")
            self.assertEqual(set(profile["sources"]), {"obj"})
            self.assertFalse(profile["review"]["blueprint"])
            self.assertIn("미제공", (target/"ART_BRIEF.md").read_text(encoding="utf-8"))
            self.assertEqual(source.read_text().count("f 1 2 3"), 1)

    def test_obj_negative_indices_uv_seams_repeated_groups_and_multiple_membership(self):
        staging = ROOT.parents[1]/".codex-staging"
        staging.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=staging) as directory:
            obj = Path(directory)/"source.obj"
            obj.write_text("""o hull
g alpha beta
v 0 0 0
v 1 0 0
v 0 1 0
vt 0 0
vt 1 0
vt 0 1
vn 0 0 1
usemtl tan
f -3/-3/1 -2/-2/1 -1/-1/1
g other
f 1 2 3
g alpha beta
f 1//1 3//1 2//1
""", encoding="utf-8")
            model = read_obj(obj)
        self.assertEqual(len(model["faces"]), 3)
        self.assertEqual(model["groups"][0]["faces"], [0, 2])
        self.assertEqual(model["groups"][0]["group"], "alpha beta")
        self.assertEqual(model["faces"][0]["vertices"], [0, 1, 2])
        self.assertEqual(model["faces"][0]["uv"], [0, 1, 2])

    def test_axes_match_bradley_and_do_not_reflect_winding(self):
        axes = {"forward": "x", "up": "y", "scale": 1}
        self.assertEqual(transform((2, 3, 4), axes), (-4, -2, 3))
        basis = [transform(v, axes) for v in [(1, 0, 0), (0, 1, 0), (0, 0, 1)]]
        self.assertEqual(dot(cross(basis[0], basis[1]), basis[2]), 1)
        with self.assertRaises(ValueError):
            transform((0, 0, 0), {"forward": "x", "up": "-x", "scale": 1})

    def test_exact_face_partition_keeps_unknown_and_rejects_double_extraction(self):
        model = {"groups": [{"id": "g0000", "faces": [0, 1, 2]}], "faces": [{}, {}, {}]}
        profile = {"parts": [{"id": "unknown", "source_group": "g0000"},
                             {"id": "smoke_launcher", "source_group": "g0000", "faces": [2]}]}
        self.assertEqual(partition(model, profile), {"smoke_launcher": [2], "unknown": [0, 1]})
        profile["parts"].append({"id": "duplicate", "source_group": "g0000", "faces": [2]})
        with self.assertRaises(ValueError):
            partition(model, profile)
        profile["parts"].pop()
        profile["parts"][0].update(action="omit", confirmed=False, reason="small")
        with self.assertRaises(ValueError):
            partition(model, profile)

    def test_contact_graph_not_absolute_height_and_no_silent_palette_clamping(self):
        style = read_json(ROOT/"presets/rimthunder_tan.json")
        profile = {"parts": [{"id": k, "role": "paint"} for k in ["roof", "hatch", "handle", "independent"]],
                   "contacts": [{"lower": "roof", "upper": "hatch"}, {"lower": "hatch", "upper": "handle"}]}
        self.assertEqual(paint_levels(profile, style), {"roof": 168, "hatch": 192, "handle": 216, "independent": 168})
        for y in [48, 108, 132, 144, 168, 192, 216]:
            rgb = paint_rgb(y, "paint", style)
            self.assertAlmostEqual(dot(rgb, (.2126, .7152, .0722)), y)
        profile["contacts"].append({"lower": "handle", "upper": "roof"})
        with self.assertRaises(ValueError):
            paint_levels(profile, style)
        profile["contacts"].pop()
        profile["parts"][0]["top"] = 220
        with self.assertRaises(ValueError):
            paint_levels(profile, style)

    def test_contact_directions_follow_rimthunder_rule(self):
        style = read_json(ROOT/"presets/rimthunder_tan.json")
        parts = [{"id": k, "role": "paint"} for k in ["roof", "hatch", "sight", "panel"]]
        parts[0]["top"] = 216
        profile = {"parts": parts, "contacts": [
            {"lower": "roof", "upper": "hatch", "direction": "down"},
            {"lower": "hatch", "upper": "sight", "direction": "same"},
            {"lower": "hatch", "upper": "panel"}]}
        # Base at the top of the ladder: mounted hatch one step darker, sight equal,
        # default edges keep the previous upward behaviour.
        self.assertEqual(paint_levels(profile, style),
                         {"roof": 216, "hatch": 192, "sight": 192, "panel": 216})
        profile["parts"][2]["top"] = 200
        with self.assertRaises(ValueError):
            paint_levels(profile, style)
        profile["parts"][2].pop("top")
        profile["contacts"].append({"lower": "roof", "upper": "panel", "direction": "down"})
        with self.assertRaises(ValueError):
            paint_levels(profile, style)
        profile["contacts"].pop()
        profile["contacts"][0]["direction"] = "darker"
        with self.assertRaises(ValueError):
            paint_levels(profile, style)
        profile["contacts"][0]["direction"] = "down"
        profile["parts"][0]["top"] = 40
        with self.assertRaises(ValueError):
            paint_levels(profile, style)

    def test_position_component_selection_across_uv_seams_preserves_original_data(self):
        model = {"vertices": [(0, 0, 0), (1, 0, 0), (0, 1, 0),
                               (0, 0, 0), (0, 1, 0), (-1, 0, 0), (8, 8, 8)],
                 "faces": [{"vertices": [0, 1, 2]}, {"vertices": [3, 4, 5]}, {"vertices": [6, 6, 6]}]}
        before = json.dumps(model)
        self.assertEqual(components(model, [0, 1, 2], 1e-6), [[0, 1], [2]])
        self.assertEqual(json.dumps(model), before)

    def test_linear_alpha_resize_and_composition(self):
        energy = Image.new("RGBA", (2, 1))
        energy.putdata([(0, 0, 0, 255), (255, 255, 255, 255)])
        self.assertAlmostEqual(resize_linear(energy, 1).getpixel((0, 0))[0], 188, delta=1)
        edge = Image.new("RGBA", (2, 1))
        edge.putdata([(0, 0, 0, 0), (255, 255, 255, 255)])
        self.assertEqual(resize_linear(edge, 1).getpixel((0, 0)), (255, 255, 255, 128))
        output = composite_linear(Image.new("RGBA", (1, 1), (0, 0, 0, 255)),
                                  Image.new("RGBA", (1, 1), (255, 255, 255, 128)), (0, 0))
        self.assertAlmostEqual(output.getpixel((0, 0))[0], 188, delta=1)


if __name__ == "__main__":
    unittest.main()
