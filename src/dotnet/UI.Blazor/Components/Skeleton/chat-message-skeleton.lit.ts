import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {MessageWidth, randomIntFromInterval} from "./helpers";
import {messageStyles} from "./styles.lit";

@customElement('chat-message-skeleton')
class ChatMessageSkeletonLit extends LitElement {
    @property()
    count = 1;
    @property({type: Boolean})
    visible = true;

    private observer: IntersectionObserver;

    static styles = [messageStyles,
        ];

    render(): HTMLTemplateResult {
        // noinspection JSMismatchedCollectionQueryUpdate
        const messages: HTMLTemplateResult[] = [];
        const messageCount = randomIntFromInterval(0, 4);
        let animatedCls = this.visible ? "animated-skeleton" : "";
        for (let i = 0; i < messageCount; i++) {
            messages.push(html`
                <div class="message-wrapper">
                    <div class="message ${this.getMessageWidth(4, 10)}"></div>
                </div>`);
        }
        return html`
            <div class="message-skeleton ${animatedCls}">
                <div class="message-avatar-wrapper">
                    <div class="message-avatar"></div>
                </div>
                <div class="c-container">
                    <div class="title message ${this.getMessageWidth(2, 5)}"></div>
                    <div class="message ${this.getMessageWidth(4, 10)}"></div>
                </div>
            </div>
            <div class="message-list ${animatedCls}">
                ${messages}
            </div>
        `;
    }

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    connectedCallback() {
        super.connectedCallback();
        const root = document.querySelector('.layout-body');
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
