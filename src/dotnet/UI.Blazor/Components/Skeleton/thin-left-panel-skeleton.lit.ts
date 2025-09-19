import {css, html, LitElement} from 'lit';
import {customElement, property} from 'lit/decorators.js';
import '../../../../nodejs/styles/index.css';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';

@customElement('thin-left-panel-skeleton')
export class ThinLeftPanelSkeletonLit extends LitElement {
    @property()
    count = 2;

    static styles = css`
    :host {
        --button-margin-left: 0px;
        --button-margin-right: 0px;
        --delimiter-width: 3rem;

        display: flex;
        flex-direction: column;
        align-items: end;
        row-gap: 0.375rem;
        height: 100%;
        padding-left: 0.25rem;
        padding-right: 0.25rem;
        background-color: var(--background-04);
    }

    .button {
        width: 2.5rem;
        height: 2.5rem;
        margin-bottom: 0.25rem;
        margin-left: var(--button-margin-left);
        margin-right: var(--button-margin-right);
        border-radius: 0.75rem;
        background: var(--background-03);
        animation: pulse 2s infinite;
    }
    .footer-button {
        width: 2.5rem;
        height: 2.5rem;
        margin: auto 5px 0.75rem 5px;
        background-color: var(--background-03);
        border-radius: 9999px;
    }
    .c-delimiter {
        margin: 0 0.25rem 0.5rem 0.25rem;
        width: var(--delimiter-width);
        border-top: 1px solid var(--nav-separator);
        @apply mx-1 w-10 md:w-12 border-t border-nav-separator;
    }

    @media (min-width: 820px) {
        :host {
            row-gap: 0;
            width: 4.5rem;
            padding-left: 0.5rem;
            padding-right: 0.5rem;
        }
        .button {
            width: 3rem;
            height: 3rem;
            margin-bottom: 0.5rem;
            border-radius: 0.5rem;
        }
        .footer-button {
            width: 3rem;
            height: 3rem;
            margin: auto 0 0.5rem 0;
        }
    }

    @media (min-width: 1280px) {
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
        this.setProperties();
        return html`
            <voxt-skeleton></voxt-skeleton>
            ${[...new Array(Number(this.count))].map(() => html`
                <div class="button" />
            `)}
            <div class='c-delimiter'></div>
            ${[...new Array(Number(this.count))].map(() => html`
                <div class="button" />
            `)}
            <div class="footer-button"></div>
        `;
    }

    private setProperties() {
        if (ScreenSize.isNarrow()) {
            this.style.setProperty('--button-margin-left', '5px');
            this.style.setProperty('--button-margin-right', '5px');
            this.style.setProperty('--delimiter-width', '2.5rem');
        } else {
            this.style.setProperty('--button-margin-left', '0');
            this.style.setProperty('--button-margin-right', '0');
            this.style.setProperty('--delimiter-width', '3rem');
        }
    }
}
