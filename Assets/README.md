# Application icon

`BeltTensionMeasurement.svg` is the editable source, drawn directly for this project.
It represents a drive belt around two pulleys and a cyan dimension mark on a navy tile.
No text is used so the silhouette remains readable at small sizes.

- `BeltTensionMeasurement.png`: transparent 1024 × 1024 preview/source raster.
- `BeltTensionMeasurement.ico`: Windows icon with 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames.

Design brief: “An industrial belt measurement icon: navy rounded tile, white belt and pulley
silhouette, cyan measurement mark, transparent corners, no text.” The built-in image generator
was unavailable in this session; the final artwork was created as SVG and rasterized locally.

To regenerate, use Python with PyMuPDF and Pillow:

```powershell
python Packaging/Build-Icon.py
```
