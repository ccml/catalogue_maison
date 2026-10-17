/**
 * Accès à un détecteur de codes-barres.
 *
 * L'API `BarcodeDetector` du navigateur est utilisée quand elle existe
 * (Chrome sur Android et macOS). Ailleurs — Safari, Firefox — on charge
 * l'implémentation WebAssembly de ZXing, et seulement à ce moment-là : le
 * reste de l'application ne paie pas son poids.
 *
 * Le moteur WebAssembly est servi par l'application elle-même (voir
 * `angular.json`, sortie `zxing/`). Par défaut il serait téléchargé depuis un
 * CDN tiers à chaque première lecture, ce qui ferait dépendre le scan d'un
 * service extérieur et lui signalerait chaque ouverture de la caméra.
 */

/** Formats imprimés sur les livres, disques, films et jeux. */
export const retailFormats = ['ean_13', 'ean_8', 'upc_a', 'upc_e'] as const;

export interface DetectedCode {
  readonly rawValue: string;
  readonly format: string;
}

export interface CodeDetector {
  detect(source: HTMLVideoElement): Promise<readonly DetectedCode[]>;
}

interface NativeDetectorConstructor {
  new (options: { formats: readonly string[] }): CodeDetector;
  getSupportedFormats(): Promise<readonly string[]>;
}

export async function createDetector(): Promise<CodeDetector> {
  const native = (globalThis as { BarcodeDetector?: NativeDetectorConstructor }).BarcodeDetector;

  if (native !== undefined) {
    try {
      const supported = await native.getSupportedFormats();
      if (retailFormats.every((format) => supported.includes(format))) {
        return new native({ formats: retailFormats });
      }
    } catch {
      // Détecteur natif présent mais inutilisable : on bascule sur ZXing.
    }
  }

  const { BarcodeDetector, prepareZXingModule } = await import('barcode-detector/ponyfill');

  prepareZXingModule({
    overrides: {
      locateFile: (path: string, prefix: string) =>
        path.endsWith('.wasm') ? new URL(`zxing/${path}`, document.baseURI).href : prefix + path,
    },
  });

  return new BarcodeDetector({ formats: [...retailFormats] });
}
