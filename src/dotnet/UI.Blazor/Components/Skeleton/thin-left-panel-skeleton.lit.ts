import {css, html, LitElement} from 'lit';
import {customElement, property} from 'lit/decorators.js';

import '../../../../nodejs/styles/index.css';

@customElement('thin-left-panel-skeleton')
export class ThinLeftPanelSkeletonLit extends LitElement {
    @property()
    count = 3;

    static styles = css`
    :host {
        display: flex;
        flex-direction: column;
        align-items: end;
        row-gap: 0.375rem;
        padding-top: 4.5rem;
        padding-left: 0.5rem;
        padding-right: 0.5rem;
        background-color: var(--background-04);
    }

    .button {
        width: 2.5rem;
        height: 2.5rem;
        margin-bottom: 0.5rem;
        border-radius: 0.75rem;
        background: #F5F5F5;
        animation: pulse 2s infinite;
    }
    .footer-button {
        margin-top: auto;
        width: 2.5rem;
        height: 2.5rem;
        margin-bottom: 0.5rem;
        background-color: var(--background-03);
        border-radius: 9999px;
    }

    @media (min-width: 820px) {
        :host {
            row-gap: 0;
            padding-left: 0.5rem;
            padding-right: 0.5rem;
        }
        .button {
            width: 3rem;
            height: 3rem;
            border-radius: 0.5rem;
        }
        .footer-button {
            width: 3rem;
            height: 3rem;
        }
    }

    @media (min-width: 1024px) {
        :host {
            width: 7.5rem;
            row-gap: 0;
            padding-left: 1rem;
            padding-right: 1rem;
        }
        .footer-button {
            width: 3rem;
            height: 3rem;
        }
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: .5;
      }
    }
  `;

    protected render(): unknown {
        return html`
            ${[...new Array(Number(this.count))].map(() => html`
                <div class="button" />
            `)}
            <div class="footer-button"></div>
        `;
    }
}
