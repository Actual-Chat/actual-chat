import { customElement, property, state } from 'lit/decorators.js';
import { guard } from 'lit/directives/guard.js';
import { range } from 'lit/directives/range.js';
import { map } from 'lit/directives/map.js';
import { css, html, LitElement } from 'lit';
import { MessageWidth, randomIntFromInterval } from './helpers';
import { messageStyles } from './styles.lit';

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
    class = '';

    @property()
    count = 1;

    render() {
        const { count } = this;
        return html`
            ${guard([count], () => map(range(count), () => html`
                <div class='message-skeleton'>
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
            if (isVisible)
                this.classList.add('animated-skeleton');
            else
                this.classList.remove('animated-skeleton');
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
