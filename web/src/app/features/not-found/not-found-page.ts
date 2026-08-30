import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `
    <section class="notfound">
      <h1>Page not found</h1>
      <p>That route does not exist.</p>
      <a routerLink="/portfolio">Back to the portfolio</a>
    </section>
  `,
  styles: `
    .notfound {
      max-width: 460px;
      margin: 96px auto;
      text-align: center;
    }
  `,
})
export class NotFoundPage {}
