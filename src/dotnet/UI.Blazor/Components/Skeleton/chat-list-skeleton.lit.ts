import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {MessageWidth, randomIntFromInterval} from "./helpers";
import {messageStyles} from "./styles.lit";

@customElement('chat-list-skeleton')
class ChatListSkeleton extends LitElement {
    @property({type: Boolean})
    visible = true;
    @property({type: String})
    rootCls = "left-panel";

    private observer: IntersectionObserver;

    static styles = [messageStyles, css`
        :host {
        }
        .avatar-wrapper {
            display: flex;
            flex: none;
            align-items: center;
            justify-content: center;
            width: 3rem;
            height: 3rem;
        }
        .avatar {
            width: 2.5rem;
            height: 2.5rem;
            border-radius: 9999px;
            background-color: var(--skeleton);
        }
    `];

    @property()
    class = "";

    @property()
    count = 1;

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    render() {
        let animatedCls = this.visible ? "animated-skeleton" : "";

        return html`
            ${[...new Array(Number(this.count))].map(() => html`
                <div class="message-skeleton ${animatedCls}">
                    <div class="avatar-wrapper">
                        <div class="avatar"></div>
                    </div>
                    <div class="c-container">
                        <div class="title message ${this.getMessageWidth(2, 5)}"></div>
                        <div class="message ${this.getMessageWidth(4, 10)}"></div>
                    </div>
                </div>
            `)}
        `;
    }

    connectedCallback() {
        super.connectedCallback();
        const root = document.querySelector(`.${this.rootCls}`);
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
