import {customElement} from "lit/decorators.js";
import {html, LitElement} from "lit";

@customElement('chat-view-footer-skeleton')
class ChatViewFooterSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    protected render(): unknown {
        return html`
            <div class="footer-container">
                <div class="footer-editor"></div>
                <div class="footer-button"></div>
                <div class="footer-button record"></div>
                <div class="footer-button"></div>
            </div>
            <div class="narrow-footer-container">
                <div class="panel-buttons">
                    <div class="footer-button record"></div>
                </div>
                <div class="panel-editor">

                </div>
            </div>
        `;
    }
}
