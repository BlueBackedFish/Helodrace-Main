"""Native-size exports and review sheets; requires Pillow and numpy outside Blender."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from pipeline import read_json, write_json


def parts_atlas(model, axes, target):
    """Labeled, per-group projections: discover opaque source names without Blender clicking."""
    from pipeline import transform
    target = Path(target)
    target.mkdir(exist_ok=True)
    points = [transform(co, axes) for co in model["vertices"]]
    page = None
    for index, group in enumerate(model["groups"]):
        if index % 12 == 0:
            page = Image.new("RGB", (1000, 4*230), (64, 67, 70))
        cell_x, cell_y = index % 3*332, (index % 12)//3*230
        draw = ImageDraw.Draw(page)
        draw.text((cell_x+6, cell_y+4), group["id"]+" "+(group["group"] or group["object"])[:30],
                  font=font(13), fill="white")
        draw.text((cell_x+6, cell_y+24), f"{len(group['faces'])} faces / top, side, front", font=font(11), fill=(205, 205, 205))
        # Each projection fits its own panel: this is an identification atlas, not a ratio comparison.
        vertex_ids = {v for face_id in group["faces"] for v in model["faces"][face_id]["vertices"]}
        for column, (horizontal, vertical, depth) in enumerate([(0, 1, 2), (1, 2, 0), (0, 2, 1)]):
            coords = [points[v] for v in vertex_ids]
            low = [min(p[a] for p in coords) for a in (horizontal, vertical)]
            high = [max(p[a] for p in coords) for a in (horizontal, vertical)]
            scale = min(96/max(high[0]-low[0], 1e-9), 164/max(high[1]-low[1], 1e-9))
            x = cell_x+column*108+6
            y = cell_y+54
            for face_id in sorted(group["faces"], key=lambda i: sum(points[v][depth] for v in model["faces"][i]["vertices"])/len(model["faces"][i]["vertices"])):
                polygon = [(x+50+(points[v][horizontal]-(low[0]+high[0])/2)*scale,
                            y+82-(points[v][vertical]-(low[1]+high[1])/2)*scale)
                           for v in model["faces"][face_id]["vertices"]]
                draw.polygon(polygon, fill=(185, 177, 151), outline=(135, 132, 120))
        if index % 12 == 11 or index == len(model["groups"])-1:
            page.save(target/(f"parts_{index//12+1:02d}.png"))


def resize_linear(source, size):
    rgba = np.asarray(source.convert("RGBA"), dtype=np.float32)/255
    rgb, alpha = rgba[:, :, :3], rgba[:, :, 3:4]
    rgb = np.where(rgb <= .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4)
    premul = np.concatenate((rgb*alpha, alpha), axis=2)
    filtered = np.stack([np.asarray(Image.fromarray(premul[:, :, i]).resize(
        (size, size), Image.Resampling.LANCZOS)) for i in range(4)], axis=2)
    alpha = np.clip(filtered[:, :, 3:4], 0, 1)
    rgb = np.clip(filtered[:, :, :3]/np.maximum(alpha, 1e-8), 0, 1)
    rgb = np.where(rgb <= .0031308, rgb*12.92, 1.055*rgb**(1/2.4)-.055)
    result = np.concatenate((rgb, alpha), axis=2)
    result[alpha[:, :, 0] < 1/510] = 0
    return Image.fromarray(np.round(np.clip(result, 0, 1)*255).astype(np.uint8))


def composite_linear(hull, turret, offset):
    """Source-over in linear light; use the same integer pivot in PNG and metadata."""
    shifted = Image.new("RGBA", hull.size)
    shifted.paste(turret, offset)  # No mask here: preserve original straight RGBA.
    a = np.asarray(hull.convert("RGBA"), dtype=np.float32)/255
    b = np.asarray(shifted, dtype=np.float32)/255
    decode = lambda rgb: np.where(rgb <= .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4)
    alpha = b[:, :, 3:4]+a[:, :, 3:4]*(1-b[:, :, 3:4])
    rgb = (decode(b[:, :, :3])*b[:, :, 3:4] +
           decode(a[:, :, :3])*a[:, :, 3:4]*(1-b[:, :, 3:4]))/np.maximum(alpha, 1e-8)
    rgb = np.where(rgb <= .0031308, rgb*12.92, 1.055*np.maximum(rgb, 0)**(1/2.4)-.055)
    rgba = np.concatenate((rgb, alpha), axis=2)
    rgba[alpha[:, :, 0] < 1/510] = 0
    return Image.fromarray(np.round(np.clip(rgba, 0, 1)*255).astype(np.uint8))


def check_margin(image, name):
    box = image.getchannel("A").getbbox()
    if box is None:
        return None
    if min(box[0], box[1], image.width-box[2], image.height-box[3]) < 2:
        raise ValueError(f"Clipped or insufficient margin: {name}")
    return list(box)


def font(size=16):
    path = Path("C:/Windows/Fonts/malgun.ttf")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def sheet(cells, target, columns=3, cell_size=(380, 320)):
    rows = (len(cells)+columns-1)//columns
    board = Image.new("RGB", (columns*cell_size[0], rows*cell_size[1]), (66, 69, 72))
    draw = ImageDraw.Draw(board)
    for i, (title, source) in enumerate(cells):
        x, y = i % columns*cell_size[0], i//columns*cell_size[1]
        image = source.convert("RGBA").copy()
        image.thumbnail((cell_size[0]-20, cell_size[1]-44), Image.Resampling.LANCZOS)
        board.paste(image, (x+(cell_size[0]-image.width)//2, y+36), image)
        draw.text((x+10, y+8), title, font=font(), fill=(235, 235, 235))
    board.save(target)


def line_settings(style, name):
    settings = dict(style.get("boundary_lines", {}))
    settings.update(settings.pop("layers", {}).get(name, {}))
    return settings


def line_rgb(style, settings):
    from pipeline import paint_rgb
    return np.array(paint_rgb(settings.get("value", 56), "paint", style))


def _shift(array, dy, dx, fill):
    out = np.full_like(array, fill)
    h, w = array.shape
    out[max(dy, 0):h+min(dy, 0), max(dx, 0):w+min(dx, 0)] = array[max(-dy, 0):h+min(-dy, 0), max(-dx, 0):w+min(-dx, 0)]
    return out


def surface_line_colour(image, settings):
    """Line colour that follows the surrounding paint: a regional average of the flat layer
    (box blur over a few pixels, both sides of the edge), darkened by one shared ratio."""
    rgba = np.asarray(image).astype(np.float64)
    alpha = rgba[..., 3:4]/255
    radius = max(1, round(settings.get("colour_radius_1024", 3)*image.width/1024))
    size = 2*radius+1
    weighted = Image.fromarray(np.clip(rgba[..., :3]*alpha, 0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(radius))
    weight = Image.fromarray(np.clip(alpha[..., 0]*255, 0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(radius))
    base = np.asarray(weighted).astype(np.float64)/np.maximum(np.asarray(weight).astype(np.float64)[..., None]/255, 1e-3)
    return np.clip(base*settings.get("darken_ratio", .55), 0, 255)


def _mix_lines(image, coverage, colour):
    """Mix the line colour (one RGB or a per-pixel array) by area coverage in linear light."""
    rgba = np.asarray(image).astype(np.float64)
    to_linear = lambda c: np.where(c <= .04045, c/12.92, ((c+.055)/1.055)**2.4)
    to_display = lambda c: np.where(c <= .0031308, c*12.92, 1.055*np.clip(c, 0, None)**(1/2.4)-.055)
    mix = np.clip(coverage, 0, 1)[..., None]*(rgba[..., 3:4] > 0)
    rgb = to_linear(rgba[..., :3]/255)*(1-mix)+to_linear(colour/255)*mix
    rgba[..., :3] = np.round(to_display(rgb)*255)
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8))


def boundary_lines(root, name, style, render):
    """One-colour part outlines on the base next to a nearer, different part.

    The part-ID + depth pass is supersampled (style boundary_lines.supersample, default 4), so the
    line mask (*_lines.png) is computed at e.g. 4096px and every size - the 1024px master included -
    mixes the line colour by the mask's exact area coverage. Only real overlaps (depth step) get a
    line; coplanar neighbours and flush grilles do not. The flat render is kept as *_flat.png.
    """
    ids_path = root/(name+"_ids.npz")
    if not ids_path.exists():
        return None
    settings = line_settings(style, name)
    if settings.get("enabled", True) is False:
        return None
    data = np.load(ids_path)
    ident, depth = data["ident"].astype(np.int64), data["depth"].astype(np.float64)
    factor = ident.shape[0]/render["master"]
    width = settings.get("width_256", 1)*render["master"]/256*factor
    step = settings.get("depth_step", .02)
    radius = int(np.ceil(width))
    # Only pixels within `radius` of an ID change can be line pixels: evaluate that band only.
    change = np.zeros(ident.shape, dtype=bool)
    for dy, dx in ((0, 1), (1, 0)):
        other = _shift(ident, -dy, -dx, 0)
        diff = (ident != other) & (ident > 0) & (other > 0)
        change |= diff | _shift(diff, dy, dx, False)
    band = change.copy()
    for _ in range(radius):
        band = band | _shift(band, 1, 0, False) | _shift(band, -1, 0, False) | _shift(band, 0, 1, False) | _shift(band, 0, -1, False)
    ys, xs = np.nonzero(band & (ident > 0))
    here, far = ident[ys, xs], depth[ys, xs]
    hit = np.zeros(len(ys), dtype=bool)
    height, cols = ident.shape
    for dy in range(-radius, radius+1):
        for dx in range(-radius, radius+1):
            if (dx, dy) == (0, 0) or dx*dx+dy*dy > width*width:
                continue
            yy, xx = ys+dy, xs+dx
            ok = (yy >= 0) & (yy < height) & (xx >= 0) & (xx < cols)
            other = np.zeros(len(ys), dtype=np.int64)
            near = np.full(len(ys), np.inf)
            other[ok], near[ok] = ident[yy[ok], xx[ok]], depth[yy[ok], xx[ok]]
            hit |= (other > 0) & (other != here) & (near < far-step)
    line = np.zeros(ident.shape, dtype=bool)
    line[ys[hit], xs[hit]] = True
    line = clean_line_mask(line, factor, settings)
    flat_path = root/(name+"_flat.png")
    if not flat_path.exists():  # fresh render; re-running the postprocess keeps the original
        Image.open(root/(name+".png")).convert("RGBA").save(flat_path)
    Image.fromarray((line*255).astype(np.uint8)).save(root/(name+"_lines.png"))
    master = layer_at(root, name, render["master"], render["master"], style)
    master.save(root/(name+".png"))
    return {"pixels_at_mask": int(line.sum()), "mask_supersample": factor, "width_256": settings.get("width_256", 1),
            "depth_step": step, "value": settings.get("value", 56), "anti_aliased": True}


def clean_line_mask(line, factor, settings):
    """Close tiny gaps, fill pin-holes and drop speck fragments (areas in 1024px pixels)."""
    from scipy import ndimage
    close = max(1, round(settings.get("close_radius_1024", .5)*factor))
    disc = np.add.outer(np.arange(-close, close+1)**2, np.arange(-close, close+1)**2) <= close*close
    line = ndimage.binary_closing(line, structure=disc)
    holes = ndimage.binary_fill_holes(line) & ~line
    labels, count = ndimage.label(holes)
    if count:
        sizes = ndimage.sum(holes, labels, range(1, count+1))
        line |= np.isin(labels, 1+np.nonzero(sizes <= settings.get("max_hole_1024", 1.5)*factor*factor)[0])
    labels, count = ndimage.label(line, structure=np.ones((3, 3)))
    if count:
        sizes = ndimage.sum(line, labels, range(1, count+1))
        line &= np.isin(labels, 1+np.nonzero(sizes >= settings.get("min_area_1024", 12)*factor*factor)[0])
    return line


def layer_at(root, name, size, master, style):
    """Layer at an output size; boundary lines mixed in by exact coverage after the resize."""
    flat_path = root/(name+"_flat.png")
    source = Image.open(flat_path if flat_path.exists() else root/(name+".png")).convert("RGBA")
    image = source if size == source.width else resize_linear(source, size)
    mask_path = root/(name+"_lines.png")
    if mask_path.exists():
        coverage = np.asarray(Image.open(mask_path).convert("F").resize((size, size), Image.Resampling.BOX))/255
        settings = line_settings(style, name)
        colour = (surface_line_colour(image, settings) if settings.get("colour", "surface") == "surface"
                  else line_rgb(style, settings))
        image = _mix_lines(image, coverage, colour)
    return image


def finish(profile_path):
    profile_path = Path(profile_path)
    profile = read_json(profile_path)
    root = profile_path.parent/"output"
    report = read_json(root/"report.json")
    render = report["render"]
    source = Image.open(root/"source_east.png").convert("RGBA")
    lit = Image.open(root/"lighting_check.png").convert("RGBA")
    difference = np.abs(np.asarray(source, dtype=np.int16)-np.asarray(lit, dtype=np.int16))
    if difference.max() > 1:
        raise ValueError(f"Emission lighting invariance failed: {difference.max()}/255")
    report["lighting_max_difference_255"] = int(difference.max())
    style = read_json(profile_path.parent/profile["style"])
    report["boundary_lines"] = {}
    weapon_names = [w["id"]+"_east" for w in profile.get("weapon_layers", [])]
    hull_names = ["hull_"+d for d in render.get("hull_directions", {}) if d != "east"]
    for name in ("hull_east", *hull_names, "turret_east", "barrel_east", "turret_full_east", *weapon_names):
        if (root/(name+".png")).exists():
            report["boundary_lines"][name] = boundary_lines(root, name, style, render)
    hull = Image.open(root/"hull_east.png").convert("RGBA")
    turret_path = root/"turret_full_east.png"
    active_layers = {p["layer"] for p in profile["parts"] if p.get("action", "keep") != "omit"}
    anchor = [round(v) for v in render["turret_anchor_pixels_top_origin"]]
    if active_layers & {"turret", "barrel"}:
        turret = Image.open(turret_path).convert("RGBA")
        offset = (anchor[0]-turret.width//2, anchor[1]-turret.height//2)
        # Verify the shifted sprite still fits the canvas before cropping could conceal it.
        box = turret.getchannel("A").getbbox()
        if box and (box[0]+offset[0] < 0 or box[1]+offset[1] < 0 or
                    box[2]+offset[0] > hull.width or box[3]+offset[1] > hull.height):
            raise ValueError("Turret composition clips; increase padding or set an explicit pivot")
        preview = composite_linear(hull, turret, offset)
        for name in weapon_names:
            # Weapon sprites sit at their mount point inside the turret image (review composite only).
            spot = render["placements"][name[:-5]]["pixels_in_turret_top_origin"]
            weapon_offset = (offset[0]+round(spot[0])-render["master"]//2, offset[1]+round(spot[1])-render["master"]//2)
            preview = composite_linear(preview, Image.open(root/(name+".png")).convert("RGBA"), weapon_offset)
    else:
        offset = (0, 0)
        preview = hull.copy()
    preview.save(root/"preview_east.png")
    names = ["hull_east", "preview_east"]
    if active_layers & {"turret", "barrel"}:
        names += ["turret_east", "barrel_east", "turret_full_east"]
    names += weapon_names+hull_names
    report["alpha_bounds"] = {}
    for size in render["sizes"]:
        directory = root/str(size)
        directory.mkdir(exist_ok=True)
        factor = size/render["master"]
        layers = {name: layer_at(root, name, size, render["master"], style) for name in names if name != "preview_east"}
        if "turret_full_east" in layers:
            layers["preview_east"] = composite_linear(layers["hull_east"], layers["turret_full_east"],
                                                      tuple(round(v*factor) for v in offset))
            for name in weapon_names:
                spot = render["placements"][name[:-5]]["pixels_in_turret_top_origin"]
                shift = (round((offset[0]+spot[0]-render["master"]/2)*factor), round((offset[1]+spot[1]-render["master"]/2)*factor))
                layers["preview_east"] = composite_linear(layers["preview_east"], layers[name], shift)
        else:
            layers["preview_east"] = layers["hull_east"].copy()
        for name, image in layers.items():
            report["alpha_bounds"][f"{size}/{name}"] = check_margin(image, name)
            image.save(directory/(name+".png"))
        write_json(directory/"pivots.json", {"size": [size, size], "turret_top_pivot": [size/2]*2,
                   "hull_turret_anchor": [v*factor for v in anchor],
                   "raw_hull_turret_anchor": [v*factor for v in render["turret_anchor_pixels_top_origin"]],
                   "hull_elevation": render["hull_elevation"], "turret_elevation": 90,
                   "placements_in_turret": {k: {**v, "pixels_in_turret_top_origin": [c*factor for c in v["pixels_in_turret_top_origin"]]}
                                            for k, v in render.get("placements", {}).items()},
                   "filter": "linear-light premultiplied-alpha Lanczos",
                   "turret_full_includes_barrel": True})
    blueprint = (Image.open(profile["sources"]["blueprint"]["path"]).convert("RGBA")
                 if "blueprint" in profile["sources"] else None)
    cells = [("청사진 전체 / 자동 비율 판정 안 함", blueprint)] if blueprint else []
    if blueprint is None:
        notice = Image.new("RGB", (360, 240), (66, 69, 72))
        ImageDraw.Draw(notice).text((15, 80), "청사진 미제공\nOBJ 기반 시험 렌더\n청사진 비율 검수 미실시", font=font(), fill="white")
        cells.append(("OBJ 초안 / 청사진 미확인", notice))
    for view in ("top", "side", "front"):
        if blueprint is not None and view in profile["blueprint_views"]:
            crop = profile["blueprint_views"][view]
            if len(crop) != 4 or not (0 <= crop[0] < crop[2] <= blueprint.width and
                                      0 <= crop[1] < crop[3] <= blueprint.height):
                raise ValueError("Blueprint crop must be [left, top, right, bottom] in source pixels")
            cells.append(("청사진 "+view, blueprint.crop(crop)))
        cells.append(("OBJ "+view, Image.open(root/("source_"+view+".png"))))
    for name in ("iso_front", "iso_rear", "preview_east"):
        cells.append((name, Image.open(root/(name+".png"))))
    sheet(cells, root/"review_sheet.png")
    # Keep the two native-size examples truly native (no thumbnail enlargement).
    native = Image.new("RGB", (sum(render["sizes"])+16*(len(render["sizes"])+1), max(render["sizes"])+78), (68, 71, 74))
    draw = ImageDraw.Draw(native)
    x = 16
    for size in render["sizes"]:
        image = Image.open(root/str(size)/"preview_east.png").convert("RGBA")
        native.paste(image, (x, 42), image)
        draw.text((x, 12), str(size)+"px 원본 크기", font=font(), fill="white")
        x += size+16
    native.save(root/"native_sizes.png")
    # Human review flags are not inferred from successful rendering or a saved blend.
    report["postprocess_passed"] = True
    report["status"] = "draft" if report["blockers"] else "reviewed"
    report["model_sha256"] = __import__("pipeline").sha256(root/"vehicle.blend")
    write_json(root/"report.json", report)
    print(f"Native-size exports passed. Status: {report['status']}; blockers: {len(report['blockers'])}")


if __name__ == "__main__":
    import sys
    finish(Path(sys.argv[1]).resolve())
