#!/usr/bin/env python3
"""Génère les fichiers d'icône CatalogueMaison (piste 1 : toit & tranches)."""
import os, json
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "build")

NAVY = (21, 33, 31)        # --ink
IVORY = (242, 244, 241)    # --paper
AMBER = (216, 151, 92)     # --ochre (variante claire)
WHITE = (255, 255, 255)

def blend(fg, bg, a):
    return tuple(int(round(fg[i] * a + bg[i] * (1 - a))) for i in range(3))

# ---------------------------------------------------------------- géométrie
# Tout est décrit dans une grille de 128, identique au SVG maître.
ROOF_FULL = [(30, 54), (64, 28), (98, 54)]
ROOF_SMALL = [(24, 58), (64, 30), (104, 58)]
# (x, y, largeur, hauteur, inclinaison en degrés, pivot au pied)
SPINES_FULL = [(38, 62, 15, 36, 0), (57, 54, 15, 44, 0), (78, 66, 15, 32, 11)]
SPINES_SMALL = [(36, 66, 17, 38, 0), (57, 66, 17, 38, 0), (78, 66, 17, 38, 0)]
BASE = (34, 100, 60, 6)


def draw_icon(size, bg=None, radius_ratio=0.0, small=False, k=1.0,
              c_roof=AMBER, c_spine=IVORY, c_accent=AMBER, c_base=None, ss=4):
    """Rend l'icône. bg=None -> fond transparent."""
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = S / 128.0

    def T(x, y):
        return ((64 + (x - 64) * k) * u, (64 + (y - 64) * k) * u)

    if bg is not None:
        r = radius_ratio * S
        if r > 0:
            d.rounded_rectangle([0, 0, S - 1, S - 1], radius=r, fill=bg)
        else:
            d.rectangle([0, 0, S - 1, S - 1], fill=bg)

    # trait du toit, extrémités et angle arrondis
    pts = ROOF_SMALL if small else ROOF_FULL
    w = (12 if small else 9) * k * u
    tp = [T(*p) for p in pts]
    d.line([tp[0], tp[1]], fill=c_roof, width=max(1, int(round(w))))
    d.line([tp[1], tp[2]], fill=c_roof, width=max(1, int(round(w))))
    for px, py in tp:
        d.ellipse([px - w / 2, py - w / 2, px + w / 2, py + w / 2], fill=c_roof)

    # tranches
    spines = SPINES_SMALL if small else SPINES_FULL
    for i, (x, y, sw, sh, tilt) in enumerate(spines):
        col = c_accent if i == 2 else c_spine
        x0, y0 = T(x, y)
        x1, y1 = T(x + sw, y + sh)
        if tilt:
            layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
            ImageDraw.Draw(layer).rounded_rectangle([x0, y0, x1, y1],
                                                    radius=3 * k * u, fill=col)
            pivot = (x1, y1) if tilt > 0 else (x0, y1)
            layer = layer.rotate(tilt, resample=Image.BICUBIC, center=pivot)
            img.alpha_composite(layer)
        else:
            d.rounded_rectangle([x0, y0, x1, y1], radius=3 * k * u, fill=col)

    # tablette d'étagère (seulement sur la version complète)
    if c_base is not None and not small:
        x, y, bw, bh = BASE
        x0, y0 = T(x, y)
        x1, y1 = T(x + bw, y + bh)
        d.rounded_rectangle([x0, y0, x1, y1], radius=3 * k * u, fill=c_base)

    return img.resize((size, size), Image.LANCZOS)


def save(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG")
    return path


# profils de couleur
DARK = dict(bg=NAVY, c_roof=AMBER, c_spine=IVORY, c_accent=AMBER,
            c_base=blend(IVORY, NAVY, 0.35))
LIGHT = dict(bg=IVORY, c_roof=AMBER, c_spine=NAVY, c_accent=AMBER,
             c_base=blend(NAVY, IVORY, 0.35))
MONO = dict(bg=None, c_roof=NAVY, c_spine=NAVY, c_accent=blend(NAVY, WHITE, 0.55),
            c_base=None)
FG = dict(bg=None, c_roof=AMBER, c_spine=IVORY, c_accent=AMBER, c_base=None)

written = []

# ------------------------------------------------------------------- iOS
# Carré plein : iOS applique lui-même le masque arrondi.
for s in [1024, 180, 167, 152, 120, 87, 80, 76, 60, 58, 40, 29, 20]:
    img = draw_icon(s, small=(s <= 40), **DARK)
    written.append(save(img, f"{OUT}/ios/AppIcon-{s}.png"))

# --------------------------------------------------------------- Android
LEGACY = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}
for dpi, s in LEGACY.items():
    img = draw_icon(s, radius_ratio=30 / 128, small=(s <= 40), **DARK)
    written.append(save(img, f"{OUT}/android/mipmap-{dpi}/ic_launcher.png"))

# icône adaptative : avant-plan transparent, contenu dans la zone sûre (72/108)
for dpi, s in {"mdpi": 108, "hdpi": 162, "xhdpi": 216, "xxhdpi": 324, "xxxhdpi": 432}.items():
    img = draw_icon(s, k=72 / 108 * 0.92, **FG)
    written.append(save(img, f"{OUT}/android/adaptive/mipmap-{dpi}/ic_launcher_foreground.png"))

written.append(save(draw_icon(512, **DARK), f"{OUT}/android/ic_launcher-playstore.png"))

os.makedirs(f"{OUT}/android/adaptive", exist_ok=True)
with open(f"{OUT}/android/adaptive/ic_launcher.xml", "w") as f:
    f.write('<?xml version="1.0" encoding="utf-8"?>\n'
            '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n'
            '    <background android:drawable="@color/ic_launcher_background" />\n'
            '    <foreground android:drawable="@mipmap/ic_launcher_foreground" />\n'
            '</adaptive-icon>\n')
written.append(f"{OUT}/android/adaptive/ic_launcher.xml")
with open(f"{OUT}/android/adaptive/colors.xml", "w") as f:
    f.write('<?xml version="1.0" encoding="utf-8"?>\n<resources>\n'
            '    <color name="ic_launcher_background">#1B2A41</color>\n</resources>\n')
written.append(f"{OUT}/android/adaptive/colors.xml")

# ------------------------------------------------------------------- web
written.append(save(draw_icon(180, **DARK), f"{OUT}/web/apple-touch-icon.png"))
for s in [192, 512]:
    written.append(save(draw_icon(s, radius_ratio=30 / 128, **DARK), f"{OUT}/web/icon-{s}.png"))
written.append(save(draw_icon(512, k=0.8, **DARK), f"{OUT}/web/icon-maskable-512.png"))
for s in [16, 32, 48]:
    written.append(save(draw_icon(s, radius_ratio=20 / 128, small=True, **DARK),
                        f"{OUT}/web/favicon-{s}.png"))

ico = draw_icon(256, radius_ratio=20 / 128, small=True, **DARK)
ico.save(f"{OUT}/web/favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
written.append(f"{OUT}/web/favicon.ico")

manifest = {
    "name": "CatalogueMaison",
    "short_name": "Catalogue",
    "theme_color": "#1B2A41",
    "background_color": "#F4F1EA",
    "display": "standalone",
    "icons": [
        {"src": "/icon-192.png", "sizes": "192x192", "type": "image/png"},
        {"src": "/icon-512.png", "sizes": "512x512", "type": "image/png"},
        {"src": "/icon-maskable-512.png", "sizes": "512x512", "type": "image/png",
         "purpose": "maskable"},
    ],
}
with open(f"{OUT}/web/site.webmanifest", "w") as f:
    json.dump(manifest, f, indent=2, ensure_ascii=False)
    f.write("\n")
written.append(f"{OUT}/web/site.webmanifest")

# --------------------------------------------------------------- aperçus
written.append(save(draw_icon(512, radius_ratio=30 / 128, **LIGHT), f"{OUT}/png/icon-light-512.png"))
written.append(save(draw_icon(512, **MONO), f"{OUT}/png/icon-mono-512.png"))

print(f"{len(written)} fichiers générés")
for p in sorted(written):
    print(" ", p.replace(OUT + "/", ""))
