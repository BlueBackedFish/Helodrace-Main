"""Reproducible native-size before/after board from the preceding committed art."""
from pathlib import Path
from io import BytesIO
import subprocess
import sys
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
sys.path.insert(0, str(REPO / "ArtSource/VehiclePipeline"))
from images import font, resize_linear


def previous(relative):
    data = subprocess.check_output(["git", "-c", f"safe.directory={REPO.as_posix()}",
        "-C", str(REPO), "show", "7e2139ea:" + relative])
    return Image.open(BytesIO(data)).convert("RGBA")


def main():
    root = REPO / "ArtSource/M2A3Bradley/TextureExport/output"
    prefix = "ArtSource/M2A3Bradley/TextureExport/output/"
    hull_before = previous(prefix + "512/Standard/Hull/hull_roof_closed_ramp_closed_east.png")
    hull_after = resize_linear(Image.open(root / "1024/Standard/ReferenceInstalled/hull_roof_closed_ramp_closed_east.png"),512)
    ramp_before = previous(prefix + "1024/Standard/Parts/rear_ramp_closed_north.png")
    ramp_after = Image.open(root / "1024/Standard/Parts/rear_ramp_closed_north.png").convert("RGBA")
    a, b = ramp_before.getchannel("A").getbbox(), ramp_after.getchannel("A").getbbox()
    box = (min(a[0],b[0]),min(a[1],b[1]),max(a[2],b[2]),max(a[3],b[3]))
    board = Image.new("RGB", (1080, 900), (65,68,71))
    draw = ImageDraw.Draw(board)
    draw.text((20,12),"M3A3 / 그릴 경계 맞춤 · 후방 램프 로프 단색",font=font(22),fill="white")
    for column, (hull, ramp, title) in enumerate([(hull_before,ramp_before,"수정 전"),(hull_after,ramp_after,"수정 후")]):
        x = 12 + column*540
        draw.text((x+12,55),title,font=font(20),fill="white")
        board.paste(hull,(x,85),hull)
        detail=ramp.crop(box)
        factor=min(3,500/detail.width)
        detail=detail.resize((round(detail.width*factor),round(detail.height*factor)),Image.Resampling.NEAREST)
        board.paste(detail,(x+(520-detail.width)//2,640),detail)
    draw.text((20,600),"차체 512px 실제 크기 / 램프는 동일 영역 최대 3배 확대",font=font(),fill="white")
    draw.text((20,858),"형상·기존 UV·피벗·다른 부품 색 유지. 전조등은 별도 레이어/차량 부품.",font=font(),fill="white")
    board.save(HERE / "before_after.png")


if __name__ == "__main__":
    main()
