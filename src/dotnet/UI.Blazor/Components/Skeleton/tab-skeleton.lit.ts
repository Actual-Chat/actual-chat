import { customElement } from 'lit/decorators.js';
import { html, LitElement } from 'lit';
import { range } from 'lit/directives/range.js';
import { map } from 'lit/directives/map.js';

@customElement('tab-skeleton')
export class TabSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    render(): unknown {
        return html`
            ${map(range(4), () => html`
                <div class="c-line" />
            `)}
        `;
    }
}
