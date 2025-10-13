import {customElement, property} from "lit/decorators.js";
import {html, LitElement} from "lit";

@customElement('round-skeleton')
class RoundSkeletonLit extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property()
    radius = 10;
    @property()
    rootCls = "layout-body";

    render(): unknown {
        return html`
            <div class="animated-skeleton round-skeleton radius-${this.radius}"></div>
        `;
    }
}
