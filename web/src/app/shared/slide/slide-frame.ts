import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import type { BoardDetail } from '../../core/models/board.models';
import { SlideCanvas } from './slide-canvas';

const SLIDE_WIDTH = 1280;
const SLIDE_HEIGHT = 720;

/**
 * Scales the fixed 1280x720 <app-slide-canvas> down to whatever width it is given,
 * preserving 16:9.
 *
 * Scaling rather than reflowing is deliberate: the slide keeps one geometry, so the
 * editor preview, Present mode and the 2x export are the same layout at different
 * sizes instead of three layouts that can disagree.
 */
@Component({
  selector: 'app-slide-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SlideCanvas],
  template: `
    <div class="frame" [style.height.px]="scaledHeight()">
      <div class="frame__scaler" [style.transform]="'scale(' + scale() + ')'">
        <app-slide-canvas [board]="board()" />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      width: 100%;
    }

    .frame {
      position: relative;
      width: 100%;
      overflow: hidden;
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-slide);
      background: var(--ink);
    }

    .frame__scaler {
      transform-origin: top left;
      will-change: transform;
    }
  `,
})
export class SlideFrame implements OnDestroy {
  readonly board = input.required<BoardDetail>();

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly containerWidth = signal(SLIDE_WIDTH);
  private observer?: ResizeObserver;

  protected readonly scale = computed(() => this.containerWidth() / SLIDE_WIDTH);
  protected readonly scaledHeight = computed(() => SLIDE_HEIGHT * this.scale());

  constructor() {
    effect((onCleanup) => {
      const element = this.host.nativeElement as HTMLElement;

      // ResizeObserver is absent in some test and SSR environments; fall back to the
      // natural width rather than throwing.
      if (typeof ResizeObserver === 'undefined') {
        this.containerWidth.set(element.clientWidth || SLIDE_WIDTH);
        return;
      }

      this.observer = new ResizeObserver((entries) => {
        const width = entries[0]?.contentRect.width ?? 0;
        if (width > 0) {
          this.containerWidth.set(width);
        }
      });

      this.observer.observe(element);
      onCleanup(() => this.observer?.disconnect());
    });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
