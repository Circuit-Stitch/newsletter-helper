# App icons

The four app icons are all generated from `MCAA_min.svg` — the square 2×2 mark,
not the full logo with the wordmark, because these are rendered as small as 16 px.

| File | From | Used by |
|---|---|---|
| `MCAANewsletter.ico` | `MCAA_min.svg` | the `.exe` itself — taskbar, alt-tab (`<ApplicationIcon>` in the csproj) |
| `Square44x44Logo.png` | `MCAA_min.svg` | MSIX: taskbar and app list |
| `Square150x150Logo.png` | `MCAA_min.svg` | MSIX: Start menu tile |
| `StoreLogo.png` | `MCAA_min.svg` | MSIX: the install prompt |
| `circuit-stitch.png` | `circuit-stitch.svg` | the Settings window, top right (`<EmbeddedResource>` in the csproj) |

`circuit-stitch.png` is 128 px for a 64 px slot: the app is not per-monitor DPI
aware, so Windows bitmap-scales the whole window on a high-DPI screen, and the
spare pixels are what stops the mark going soft when it does.

They are committed rather than generated at build time, so neither the build nor
CI needs an SVG rasterizer. To regenerate after editing an SVG, render it to a
PNG at 2–4× the size you need and resample down — there is no ImageMagick or
Inkscape here, but headless Edge is on every Windows 11 machine:

```powershell
# 1. Render the SVG. render.html inlines the <svg> element (rather than
#    <img src="...">) and sizes it — a separate fetch races the screenshot and
#    gets you a half-painted capture. --user-data-dir must be a fresh directory:
#    a second run against a locked profile silently writes no file at all.
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" `
    --headless=new --disable-gpu --no-sandbox --user-data-dir=.\profile `
    --default-background-color=00000000 `
    --force-device-scale-factor=1 --screenshot=logo512.png `
    --window-size=512,512 file:///full/path/to/render.html
```

```python
# 2a. The icon set. Requires Pillow.
from PIL import Image
im = Image.open("logo512.png").convert("RGBA")
im = im.crop(im.getbbox())                 # trim the bands the 476x459 viewBox leaves
side = max(im.size)
sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2))

for name, size in [("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)]:
    sq.resize((size, size), Image.LANCZOS).save(f"{name}.png")
sq.save("MCAANewsletter.ico", sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
```

```powershell
# 2b. circuit-stitch.png. No crop or squaring: that SVG is already a filled
#     100x100 square. Rendered at 256 and halved, which is just a resample.
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile("cs256.png")
$dst = New-Object System.Drawing.Bitmap 128,128
$g = [System.Drawing.Graphics]::FromImage($dst)
$g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0,0,128,128))
$g.Dispose(); $dst.Save("circuit-stitch.png", 'Png'); $dst.Dispose(); $src.Dispose()
```

Only base-size tiles are produced. If the Start menu tile ever looks soft on a
high-DPI screen, add `Square150x150Logo.scale-200.png` (300 px) and friends —
MSIX picks them up by file name, no manifest change.
