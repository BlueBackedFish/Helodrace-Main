"""Same-body-width reference comparison and alpha-background inspection."""
from pathlib import Path
import sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent/"VehiclePipeline"))
from images import font
from pipeline import read_json, write_json


def trim(image):
    image = image.convert("RGBA")
    box = image.getchannel("A").getbbox()
    return image.crop(box) if box else image


def fit_width(image, width):
    image = trim(image)
    # Review-only resize. Game outputs remain the pipeline's linear-light exports.
    return image.resize((width, round(image.height*width/image.width)), Image.Resampling.LANCZOS)


def main():
    profile = read_json(ROOT/"vehicle.json")
    output = ROOT/"output"
    reference = Path(profile["style_references"]["rimthunder"])
    board = Image.new("RGB", (1040, 840), (65, 68, 71))
    draw = ImageDraw.Draw(board)
    draw.text((20, 15), "M1A2 / RimThunder 형태·명도 참고 ↔ TAN 시험 렌더", font=font(21), fill="white")
    for x, title, hull, turret in [
        (20, "RimThunder (원본 무채색 텍스처)", Image.open(reference/"vehicle_east.png"),
         Image.open(reference/"vehicle_turret.png").rotate(-90, expand=True)),
        (540, "OBJ 기반 / 고정 Emission / TAN", Image.open(output/"hull_east.png"),
         Image.open(output/"turret_east.png"))]:
        draw.text((x, 58), title, font=font(), fill="white")
        hull = fit_width(hull, 460)
        turret = fit_width(turret, 360)
        board.paste(hull, (x+5, 100), hull)
        board.paste(turret, (x+45, 390), turret)
    draw.text((20, 760), "차체 외피 폭 460px / 포탑 외피 길이 360px로 각각 정렬한 검수용 비교", font=font(), fill="white")
    draw.text((20, 790), "직접 텍스처 복사 없음 · 청사진 미제공 · 피벗 일부 임시 · 게임 검수 미실시", font=font(), fill=(215, 215, 215))
    board.save(output/"style_comparison.png")
    sprite = Image.open(output/"512/preview_east.png").convert("RGBA")
    backgrounds = Image.new("RGB", (3*512, 550))
    draw = ImageDraw.Draw(backgrounds)
    for i, (color, title) in enumerate([((245, 245, 242), "밝은 배경"), ((125, 128, 130), "중간 배경"), ((30, 32, 35), "어두운 배경")]):
        backgrounds.paste(color, (i*512, 0, (i+1)*512, 550))
        backgrounds.paste(sprite, (i*512, 38), sprite)
        draw.text((i*512+12, 10), title, font=font(), fill=(10, 10, 10) if i < 2 else "white")
    backgrounds.save(output/"alpha_backgrounds.png")
    image = Image.open(output/"256/preview_east.png").convert("RGBA")
    alpha = np.asarray(image)[:, :, 3]
    invisible = np.asarray(image)[alpha == 0, :3]
    assert not invisible.any(), "Transparent output retains hidden RGB"
    report = read_json(output/"report.json")
    report["trial_review"] = {"native_sizes": [256, 512], "reference_comparison": "style_comparison.png",
                              "alpha_review": "alpha_backgrounds.png", "transparent_rgb_zero": True,
                              "blueprint": "not supplied", "game": "not tested",
                              "note": "First art trial; source detail density and local surface paint remain refinable"}
    write_json(output/"report.json", report)
    print("Comparison and alpha-background review images saved")


if __name__ == "__main__":
    main()
