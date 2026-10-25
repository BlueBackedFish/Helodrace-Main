import unittest
import sys
from pathlib import Path
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from shape_grilles import fit_panel, project, artwork


class FittedGrilles(unittest.TestCase):
    def test_translation_does_not_shift_a_panel_pattern(self):
        points = np.array([[0, 0, 1], [2, 0, 1], [1.8, 1, 1], [0, 1, 1]])
        first = fit_panel(points)
        second = fit_panel(points+[11, -8, 3])
        np.testing.assert_allclose(project(points, first), project(points+[11, -8, 3], second), atol=1e-10)
        colors = [[50]*3, [110]*3, [150]*3]
        np.testing.assert_array_equal(artwork(first, dict(pattern="louvers"), colors),
                                      artwork(second, dict(pattern="louvers"), colors))

    def test_trapezoid_border_follows_the_actual_corner(self):
        frame = fit_panel([[0, 0, 0], [2, 0, 0], [1, 1, 0], [0, 1, 0]])
        image = artwork(frame, dict(pattern="slots", rows=3, columns=4), [[40]*3, [100]*3, [160]*3])
        # Upper-right is outside the sloping boundary, rather than a rectangular frame.
        np.testing.assert_allclose(image[240, 240, :3], np.array([40]*3)/255)
        self.assertTrue(np.any(np.isclose(image[2, :, 0], 160/255)))

    def test_round_fan_and_rectangular_vents_are_not_one_repeating_tile(self):
        angles = np.linspace(0, 2*np.pi, 48, endpoint=False)
        circle = fit_panel(np.c_[np.cos(angles), np.sin(angles), np.zeros(48)])
        colors = [[48]*3, [112]*3, [150]*3]
        radial = artwork(circle, dict(pattern="radial", spokes=8), colors)
        louvers = artwork(circle, dict(pattern="louvers", rows=4), colors)
        self.assertGreater(np.count_nonzero(radial != louvers), 10000)
        self.assertTrue(np.isfinite(radial).all())
        with self.assertRaises(ValueError):
            fit_panel([[0, 0, 0], [1, 0, 0], [2, 0, 0]])

    def test_display_rgb_rejects_double_255_conversion(self):
        frame = fit_panel([[0,0,0],[1,0,0],[1,1,0],[0,1,0]])
        with self.assertRaises(ValueError):
            artwork(frame,dict(pattern="louvers"),[[72*255]*3,[112*255]*3,[144*255]*3])


if __name__ == "__main__":
    unittest.main()
