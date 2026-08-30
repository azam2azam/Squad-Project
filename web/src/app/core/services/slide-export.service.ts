import { Injectable } from '@angular/core';

/**
 * Client-side PNG capture of a rendered slide, as the prototype does with html2canvas.
 *
 * html2canvas is loaded on demand rather than bundled into the initial chunk: it is
 * ~200kB and most sessions never export.
 */
@Injectable({ providedIn: 'root' })
export class SlideExportService {
  /**
   * Captures the given element at 2x and triggers a download.
   * Returns the filename used, so the caller can confirm what happened.
   */
  async downloadPng(element: HTMLElement, title: string): Promise<string> {
    const { default: html2canvas } = await import('html2canvas');

    const canvas = await html2canvas(element, {
      // Matches the slide surface, so rounded corners composite against --ink
      // rather than white.
      backgroundColor: '#0E1520',
      scale: 2,
      useCORS: true,
      logging: false,
    });

    const filename = `${slugify(title)}-status.png`;
    const dataUrl = canvas.toDataURL('image/png');

    const link = document.createElement('a');
    link.href = dataUrl;
    link.download = filename;
    link.click();

    return filename;
  }

  /** Downloads arbitrary JSON as a file — used by the board/roster export. */
  downloadJson(data: unknown, filename: string): void {
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();

    // Released on the next tick so the click has been handled.
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }
}

/** "OPD Screen Revamp" -> "opd-screen-revamp", matching the prototype and the server. */
function slugify(value: string): string {
  return (
    value
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'squad'
  );
}
