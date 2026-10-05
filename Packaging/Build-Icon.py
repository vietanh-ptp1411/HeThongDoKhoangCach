"""Render the repo-native SVG and encode a multi-resolution Windows ICO."""
from pathlib import Path
from io import BytesIO
import pymupdf
from PIL import Image

assets = Path(__file__).resolve().parent.parent / "Assets"
source = assets / "BeltTensionMeasurement.svg"
with pymupdf.open(source) as vector:
    pixmap = vector[0].get_pixmap(matrix=pymupdf.Matrix(4, 4), alpha=True)
    raster = Image.open(BytesIO(pixmap.tobytes("png"))).convert("RGBA")
    raster.save(assets / "BeltTensionMeasurement.png")
    raster.save(assets / "BeltTensionMeasurement.ico", format="ICO",
                sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print(assets / "BeltTensionMeasurement.ico")
