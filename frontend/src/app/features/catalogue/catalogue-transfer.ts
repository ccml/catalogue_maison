import { Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CatalogueApi } from '../../core/api/catalogue-api.service';
import { describeHttpError } from '../../core/api/http-error';
import {
  duplicateReasonLabels,
  mediaTypeLabels,
  type CatalogueExport,
  type ImportReport,
} from '../../core/api/media.models';
import { Icon } from '../../shared/icon';
import { exportFileName, importErrors, parseExport } from './transfer';

/**
 * Export et import du catalogue du ménage, en un fichier JSON.
 *
 * L'import n'ajoute que ce qui manque : rien n'est supprimé ni écrasé, les
 * doublons sont sautés et listés. C'est tout ou rien — l'API refuse le
 * fichier entier si une seule fiche est invalide.
 */
@Component({
  selector: 'app-catalogue-transfer',
  imports: [Icon],
  templateUrl: './catalogue-transfer.html',
  styleUrl: './catalogue-transfer.scss',
})
export class CatalogueTransfer {
  private readonly api = inject(CatalogueApi);

  protected readonly reasonLabels = duplicateReasonLabels;
  protected readonly typeLabels = mediaTypeLabels;

  protected readonly exporting = signal(false);
  protected readonly exportDone = signal<string | null>(null);
  protected readonly exportProblem = signal<string | null>(null);

  /** Fichier choisi, déjà reconnu comme un export, en attente de confirmation. */
  protected readonly chosen = signal<{ readonly name: string; readonly file: CatalogueExport } | null>(null);
  protected readonly importing = signal(false);
  protected readonly report = signal<ImportReport | null>(null);
  protected readonly importProblem = signal<string | null>(null);
  protected readonly importRefusals = signal<readonly string[]>([]);

  protected async exportCatalogue(): Promise<void> {
    this.exporting.set(true);
    this.exportDone.set(null);
    this.exportProblem.set(null);
    try {
      const file = await firstValueFrom(this.api.exportCatalogue());
      const name = exportFileName(new Date());
      download(name, JSON.stringify(file, null, 2));
      this.exportDone.set(
        `${file.medias.length} ${file.medias.length === 1 ? 'média exporté' : 'médias exportés'} dans ${name}.`,
      );
    } catch (error) {
      this.exportProblem.set(describeHttpError(error).message);
    } finally {
      this.exporting.set(false);
    }
  }

  protected async choose(input: HTMLInputElement): Promise<void> {
    this.chosen.set(null);
    this.report.set(null);
    this.importProblem.set(null);
    this.importRefusals.set([]);

    const selected = input.files?.[0];
    // Vider le champ : rechoisir le même fichier, corrigé, doit redéclencher.
    input.value = '';
    if (selected === undefined) {
      return;
    }

    const parsed = parseExport(await selected.text());
    if (!parsed.ok) {
      this.importProblem.set(parsed.message);
      return;
    }

    this.chosen.set({ name: selected.name, file: parsed.file });
  }

  protected cancel(): void {
    this.chosen.set(null);
  }

  protected async importCatalogue(): Promise<void> {
    const chosen = this.chosen();
    if (chosen === null) {
      return;
    }

    this.importing.set(true);
    this.importProblem.set(null);
    this.importRefusals.set([]);
    try {
      this.report.set(await firstValueFrom(this.api.importCatalogue(chosen.file)));
      this.chosen.set(null);
    } catch (error) {
      const refusals = importErrors(error);
      if (refusals.length > 0) {
        this.importProblem.set('Le fichier a été refusé, rien n’a été importé :');
        this.importRefusals.set(refusals);
      } else {
        this.importProblem.set(describeHttpError(error).message);
      }
    } finally {
      this.importing.set(false);
    }
  }
}

/** Fait télécharger un texte au navigateur, sans aller-retour serveur. */
function download(name: string, content: string): void {
  const url = URL.createObjectURL(new Blob([content], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  // Laisser au navigateur le temps de lancer le téléchargement.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
