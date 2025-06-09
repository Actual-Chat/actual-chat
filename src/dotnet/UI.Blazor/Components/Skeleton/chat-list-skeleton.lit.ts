import { customElement, property, state } from 'lit/decorators.js';
import { guard } from 'lit/directives/guard.js';
import { range } from 'lit/directives/range.js';
import { map } from 'lit/directives/map.js';
import { css, html, LitElement } from 'lit';
import { MessageWidth, randomIntFromInterval } from './helpers';
import { messageStyles } from './styles.lit';
import { fastRaf } from 'fast-raf';

@customElement('chat-list-skeleton')
class ChatListSkeleton extends LitElement {
    private observer: IntersectionObserver;

    static styles = [
        messageStyles, css`
            :host {
                display: flex;
                flex-direction: column;
            }

            :host(.animated-skeleton) {
                animation: pulse 2s infinite;
            }
            .message-skeleton.thread-skeleton {
                column-gap: 1rem;
                height: 2.5rem;
                align-items: flex-start;
            }
            .message-skeleton.thread-skeleton + .message-skeleton.thread-skeleton {
                margin-top: 0.5rem;
            }

            .avatar-wrapper {
                display: flex;
                flex: none;
                align-items: center;
                justify-content: center;
                width: 3rem;
                height: 3rem;
            }
            .thread-skeleton .avatar-wrapper {
                width: 2rem;
                height: 2rem;
            }

            .avatar {
                width: 2.5rem;
                height: 2.5rem;
                border-radius: 9999px;
                background-color: var(--skeleton);
            }
            .thread-skeleton .avatar {
                width: 2rem;
                height: 2rem;
            }
            .message-skeleton.thread-skeleton .c-container {
                margin-top: 0.25rem;
            }
        `];

    @property()
    class = '';

    @property()
    messageCls = '';

    @property()
    count = 1;

    render() {
        const { count } = this;
        return html`
            ${guard([count], () => map(range(count), () => html`
                <div class='message-skeleton ${this.messageCls}'>
                    <div class='avatar-wrapper'>
                        <div class='avatar'></div>
                    </div>
                    <div class='c-container'>
                        <div class='title message ${this.getMessageWidth(2, 5)}'></div>
                        <div class='message ${this.getMessageWidth(4, 10)}'></div>
                    </div>
                </div>
            `))}
        `;
    }

    connectedCallback() {
        super.connectedCallback();

        this.observer = new IntersectionObserver(entries => {
            const isVisible = entries.some(e => e.isIntersecting);
            // console.warn('isVisible', isVisible, entries);
            fastRaf({
                write: () => this.classList.toggle('animated-skeleton', isVisible && this.count > 0)
            })
        });
        this.observer.observe(this);
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.observer.disconnect();
    }

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }
}
