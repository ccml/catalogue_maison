#!/usr/bin/env python3
"""Écrit les SVG maîtres de l'icône CatalogueMaison (mêmes coordonnées que le rendu PNG)."""
import os

OUT = os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

NAVY, IVORY, AMBER = "#15211F", "#F2F4F1", "#D8975C"


def marks(roof, spine, accent, base=None, base_op="0.35"):
    s = (f'  <path d="M30 54 L64 28 L98 54" fill="none" stroke="{roof}" stroke-width="9" '
         f'stroke-linecap="round" stroke-linejoin="round"/>\n'
         f'  <rect x="38" y="62" width="15" height="36" rx="3" fill="{spine}"/>\n'
         f'  <rect x="57" y="54" width="15" height="44" rx="3" fill="{spine}"/>\n'
         f'  <rect x="78" y="66" width="15" height="32" rx="3" fill="{accent}" '
         f'transform="rotate(-11 93 98)"/>\n')
    if base:
        s += (f'  <rect x="34" y="100" width="60" height="6" rx="3" fill="{base}" '
              f'opacity="{base_op}"/>\n')
    return s


def marks_small(roof, spine, accent):
    return (f'  <path d="M24 58 L64 30 L104 58" fill="none" stroke="{roof}" stroke-width="12" '
            f'stroke-linecap="round" stroke-linejoin="round"/>\n'
            f'  <rect x="36" y="66" width="17" height="38" rx="3" fill="{spine}"/>\n'
            f'  <rect x="57" y="66" width="17" height="38" rx="3" fill="{spine}"/>\n'
            f'  <rect x="78" y="66" width="17" height="38" rx="3" fill="{accent}"/>\n')


def doc(title, body, bg=None, radius=0):
    head = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="1024" '
            f'height="1024" role="img" aria-label="{title}">\n')
    if bg:
        head += (f'  <rect width="128" height="128" rx="{radius}" fill="{bg}"/>\n'
                 if radius else f'  <rect width="128" height="128" fill="{bg}"/>\n')
    return head + body + "</svg>\n"


files = {
    "icon.svg": doc("Icône CatalogueMaison", marks(AMBER, IVORY, AMBER, IVORY), NAVY, 0),
    "icon-rounded.svg": doc("Icône CatalogueMaison, coins arrondis",
                            marks(AMBER, IVORY, AMBER, IVORY), NAVY, 30),
    "icon-light.svg": doc("Icône CatalogueMaison sur fond clair",
                          marks(AMBER, NAVY, AMBER, NAVY), IVORY, 30),
    "icon-mono.svg": doc("Icône CatalogueMaison monochrome",
                         marks(NAVY, NAVY, NAVY, None).replace(
                             f'fill="{NAVY}" transform', f'fill="{NAVY}" opacity="0.55" transform')),
    "icon-small.svg": doc("Icône CatalogueMaison, version petites tailles",
                          marks_small(AMBER, IVORY, AMBER), NAVY, 0),
    "icon-foreground.svg": doc(
        "Avant-plan de l'icône adaptative Android",
        '  <g transform="translate(64 64) scale(0.613) translate(-64 -64)">\n'
        + marks(AMBER, IVORY, AMBER, None) + "  </g>\n"),
}

for name, content in files.items():
    with open(f"{OUT}/{name}", "w") as f:
        f.write(content)
    print("écrit", name)
