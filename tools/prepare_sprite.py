"""Prepare the supplied transparent GIF as small, composited PNG frames.

Run: python tools/prepare_sprite.py SOURCE.gif
Pillow correctly applies GIF offsets/disposal before the shared crop/resize.
"""
from pathlib import Path
import json
import shutil
import sys
from PIL import Image, ImageSequence

root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1])
output = root / "Assets" / "default"
output.mkdir(parents=True, exist_ok=True)
shutil.copy2(source, root / "Assets" / "default-original.gif")

gif = Image.open(source)
frames = []
durations = []
for frame in ImageSequence.Iterator(gif):
    frames.append(frame.convert("RGBA").copy())
    durations.append(max(20, int(frame.info.get("duration", 100))))
boxes = [frame.getbbox() for frame in frames if frame.getbbox()]
crop = (min(box[0] for box in boxes), min(box[1] for box in boxes),
        max(box[2] for box in boxes), max(box[3] for box in boxes))
width, height = crop[2] - crop[0], crop[3] - crop[1]
scale = min(1.0, 384 / max(width, height))
size = (round(width * scale), round(height * scale))
names = []
for index, frame in enumerate(frames):
    name = f"frame-{index:03d}.png"
    frame.crop(crop).resize(size, Image.Resampling.LANCZOS).save(output / name, optimize=True)
    names.append(name)
manifest = {"width": size[0], "height": size[1], "durationsMs": durations, "frames": names}
(output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
frames[0].crop(crop).resize(size, Image.Resampling.LANCZOS).save(root / "Assets" / "preview.png")
icon = Image.new("RGBA", (256, 256))
icon_art = frames[0].crop(crop)
icon_art.thumbnail((244, 244), Image.Resampling.LANCZOS)
icon.alpha_composite(icon_art, ((256 - icon_art.width) // 2, (256 - icon_art.height) // 2))
icon.save(root / "Assets" / "bonbon.ico", sizes=[(16,16), (32,32), (48,48), (64,64), (128,128), (256,256)])
print(f"Prepared {len(frames)} frames, {size[0]}×{size[1]}, {sum(durations)} ms, transparency preserved.")
