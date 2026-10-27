import { Injectable, signal } from '@angular/core';

export type Skin = 'classic' | 'glass';

const KEY = 'ssb.skin';

/**
 * Which surface treatment the app wears.
 *
 * Classic is the default and stays the default: the glass style trades contrast for
 * depth, and the people who read this all day should opt into that rather than be given
 * it. The choice is one attribute on <html>, so every page follows without knowing this
 * service exists.
 */
@Injectable({ providedIn: 'root' })
export class SkinService {
  private readonly current = signal<Skin>(read());

  readonly skin = this.current.asReadonly();

  constructor() {
    this.apply(this.current());
  }

  toggle(): void {
    this.set(this.current() === 'glass' ? 'classic' : 'glass');
  }

  set(skin: Skin): void {
    this.current.set(skin);
    this.apply(skin);

    try {
      localStorage.setItem(KEY, skin);
    } catch {
      // A private window refusing storage costs the preference, not the page.
    }
  }

  private apply(skin: Skin): void {
    document.documentElement.dataset['skin'] = skin;
  }
}

function read(): Skin {
  try {
    return localStorage.getItem(KEY) === 'glass' ? 'glass' : 'classic';
  } catch {
    return 'classic';
  }
}
