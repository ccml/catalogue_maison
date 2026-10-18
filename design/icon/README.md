# Icône CatalogueMaison — piste 1 « Toit & tranches »

Un toit ambre coiffant trois tranches posées sur une étagère, dont une appuyée contre sa voisine.

## Palette

Reprise des variables du frontend (`src/styles.scss`).

| Rôle | Hex | Variable |
|---|---|---|
| Encre (fond) | `#15211F` | `--ink` |
| Ocre (accent) | `#D8975C` | `--ochre` (valeur du thème sombre, plus contrastée sur l'encre) |
| Papier (tranches) | `#F2F4F1` | `--paper` |

## Les fichiers

### `svg/` — les maîtres

Tout part d'ici ; les PNG en sont des rendus. Grille de 128, à rééchelonner librement.

- `icon.svg` — fond encre carré. C'est la source de référence.
- `icon-rounded.svg` — mêmes dessins, coins arrondis (rayon 30/128).
- `icon-light.svg` — sur fond papier, pour les contextes clairs.
- `icon-mono.svg` — fond transparent, tout en encre : impression, filigrane, barre de menus.
- `icon-small.svg` — **version simplifiée** : les trois tranches reviennent à la même hauteur, la tranche penchée se redresse, l'étagère disparaît et le trait du toit s'épaissit. À utiliser sous 48 px, sans quoi le dessin se referme.
- `icon-foreground.svg` — avant-plan de l'icône adaptative Android (transparent, contenu dans la zone sûre).

### `ios/`

`AppIcon-1024.png` plus toutes les tailles de la grille iOS (180, 167, 152, 120, 87, 80, 76, 60, 58, 40, 29, 20). Carrés pleins : iOS applique lui-même le masque arrondi — ne pas pré-arrondir. Les tailles ≤ 40 utilisent la version simplifiée.

Avec Xcode 14 et au-delà, `AppIcon-1024.png` suffit dans l'asset catalog ; les autres tailles servent aux cibles plus anciennes.

### `android/`

- `mipmap-*/ic_launcher.png` — icônes historiques, pré-arrondies (48 → 192).
- `adaptive/mipmap-*/ic_launcher_foreground.png` — avant-plan transparent de l'icône adaptative, contenu dans la zone sûre de 72 dp.
- `adaptive/ic_launcher.xml` — à déposer dans `res/mipmap-anydpi-v26/`.
- `adaptive/colors.xml` — la couleur de fond `#1B2A41` à fusionner dans `res/values/colors.xml`.
- `ic_launcher-playstore.png` — 512 × 512, carré, pour la fiche Play.

### `web/`

- `favicon.ico` (16/32/48 en un seul fichier) et `favicon-16/32/48.png`
- `apple-touch-icon.png` (180, carré)
- `icon-192.png`, `icon-512.png`, `icon-maskable-512.png`
- `site.webmanifest` — à adapter si les icônes ne sont pas servies à la racine

```html
<link rel="icon" href="/favicon.ico" sizes="any">
<link rel="icon" href="/favicon-32.png" type="image/png" sizes="32x32">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
<link rel="manifest" href="/site.webmanifest">
<meta name="theme-color" content="#f2f4f1">
```

### `png/`

`icon-light-512.png` et `icon-mono-512.png` : aperçus des variantes claire et monochrome, pour un README, une présentation ou un en-tête.

## Règles d'emploi

- L'ambre ne marque qu'une chose à la fois : l'accent de l'icône, ou l'élément actif de l'écran — jamais les deux côté à côte.
- Garder au moins une demi-tranche de marge autour du dessin si l'icône est posée sur un fond quelconque.
- Ne pas recolorer les tranches média par type (livre, musique, film) : c'est le rôle des pictogrammes d'interface, pas celui de l'icône.

## Regénérer

`build_icons.py` (Pillow) produit tous les PNG, l'ICO et le manifeste ; `make_svg.py` écrit les SVG. Les deux partagent les mêmes coordonnées : modifier la géométrie dans l'un impose de la reporter dans l'autre.
