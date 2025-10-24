import {html, LitElement} from 'lit';
import {customElement, property} from 'lit/decorators.js';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';

@customElement('thin-left-panel-skeleton')
export class ThinLeftPanelSkeletonLit extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property()
    count = 2;

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
        } else {
            this.style.setProperty('--button-margin-left', '0');
            this.style.setProperty('--button-margin-right', '0');
        }
    }
}
