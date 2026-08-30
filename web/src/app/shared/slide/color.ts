/**
 * Converts a token hex colour to `rgba(r, g, b, a)`.
 *
 * The slide deliberately avoids `color-mix()`: html2canvas cannot parse the
 * `color(srgb …)` value it computes to, which broke PNG export with
 * "Attempting to parse an unsupported color function". The prototype solved the same
 * problem by appending an alpha suffix to the hex; this does it explicitly so the
 * result is a plain rgba() that every renderer understands.
 *
 * Falls back to the input untouched if it is not a hex colour, so a CSS variable or
 * named colour still renders rather than throwing.
 */
export function withAlpha(color: string, alpha: number): string {
  const hex = color.trim();

  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex);
  if (!match) return hex;

  const digits = match[1];
  const full =
    digits.length === 3
      ? digits
          .split('')
          .map((d) => d + d)
          .join('')
      : digits;

  const r = parseInt(full.slice(0, 2), 16);
  const g = parseInt(full.slice(2, 4), 16);
  const b = parseInt(full.slice(4, 6), 16);

  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
