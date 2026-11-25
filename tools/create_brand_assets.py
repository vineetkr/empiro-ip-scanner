"""Prepare the user-provided Empiro logo art for the app and Windows icon."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Empiro.IpScanner.Wpf" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

mark_source = Image.open(ROOT / "tools" / "branding" / "empiro-emblem-source.jpeg").convert("RGB")
word_source = Image.open(ROOT / "tools" / "branding" / "empiro-wordmark-source.jpeg").convert("RGB")

# Remove the photographed white page while retaining antialiased black ink.
gray = mark_source.convert("L")
mark_alpha = gray.point(lambda value: 0 if value > 247 else min(255, max(0, round((255 - value) * 255 / 230))))
mark = Image.new("RGBA", mark_source.size, (20, 20, 20, 0))
mark.putalpha(mark_alpha)
mark.save(ASSETS / "empiro-mark.png", optimize=True)

# Keep the supplied red lettering and make its white JPEG background transparent.
red = (208, 12, 13)
word_alpha = word_source.getchannel("G").point(lambda value: min(255, max(0, round((255 - value) * 255 / 243))))
word = Image.new("RGBA", word_source.size, (*red, 0))
word.putalpha(word_alpha)
word.save(ASSETS / "empiro-wordmark.png", optimize=True)

# Use the emblem as the Windows application/taskbar icon on a red tile.
icon_size = 256
icon = Image.new("RGBA", (icon_size, icon_size), (0, 0, 0, 0))
draw = ImageDraw.Draw(icon)
draw.rounded_rectangle((5, 5, icon_size - 6, icon_size - 6), radius=44, fill=(208, 12, 13, 255))
mark_fit = mark.copy()
mark_fit.thumbnail((196, 196), Image.Resampling.LANCZOS)
alpha = mark_fit.getchannel("A")
white_mark = Image.new("RGBA", mark_fit.size, (255, 255, 255, 0))
white_mark.putalpha(alpha)
icon.alpha_composite(white_mark, ((icon_size - mark_fit.width) // 2, (icon_size - mark_fit.height) // 2))
icon.save(ASSETS / "empiro.ico", format="ICO", sizes=[(256, 256), (64, 64), (48, 48), (32, 32), (16, 16)])
print(f"Created brand assets in {ASSETS}")
