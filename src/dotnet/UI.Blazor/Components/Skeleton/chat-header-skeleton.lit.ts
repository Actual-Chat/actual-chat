import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {MessageWidth, randomIntFromInterval} from "./helpers";
import {messageStyles} from "./styles.lit";

@customElement('chat-header-skeleton')
class ChatHeaderSkeletonLit extends LitElement {
    @property({type: Boolean})
    visible = true;

    private observer: IntersectionObserver;

    static styles = [messageStyles,
        ];

    render(): HTMLTemplateResult {
        let animatedCls = this.visible ? "animated-skeleton" : "";

        return html`
            <div class="message-skeleton header-skeleton ${animatedCls}">
                <div class="message-avatar-wrapper">
                    <div class="message-avatar"></div>
                </div>
                <div class="c-container">
                    <div class="title message"></div>
                </div>
            </div>
        `;
    }

    connectedCallback() {
        super.connectedCallback();
        const root = document.querySelector('.layout-header');
        this.observer = new IntersectionObserver((entries, observer) => {
            entries.some(e => {
                this.visible = e.isIntersecting;
            });
        }, {
            root: root,
        });
        this.observer.observe(this);
    }

    disconnectedCallback() {
        super.disconnectedCallback();
        this.observer.disconnect();
    }
}
