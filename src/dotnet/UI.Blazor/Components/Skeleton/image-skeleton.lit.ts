import { customElement, property } from 'lit/decorators.js';
import { createRef, Ref, ref } from 'lit/directives/ref.js';
import { css, html, LitElement, nothing } from 'lit';

@customElement('image-skeleton')
class ImageSkeleton extends LitElement {
    static styles = css`
      :host {
        display: block;
      }

      :host(.loading) {
        animation: pulse 2s infinite;
        background-color: var(--background-05);
      }

      :host(.loading) .image {
        visibility: hidden;
      }

      .image {
        width: 100%;
        height: 100%;
        object-fit: cover;
        border-radius: inherit;
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

    @property({ reflect: true }) class: string;
    @property() src: string;
    @property() title: string = "";

    private _imageRef: Ref<HTMLImageElement> = createRef();

    // for tests
    // willUpdate(changedProperties: any) {
    //     if (changedProperties.has("src")) {
    //         if (Math.floor(Math.random() * 100) % 2 === 0) {
    //             const original = this.src;
    //             this.src = "https://some.host/invalid.svg";
    //             setTimeout(() => {
    //                 this.src = original;
    //             }, 3000)
    //         }
    //     }
    // }

    render() {
        const isSubDomain = this.isSubDomain(this.src);
        return html`
            <img
                ${ref(this._imageRef)}
                class="image"
                crossorigin='${ isSubDomain ? nothing : 'anonymous' }'
                draggable="false"
                alt=""
                .src="${this.src}"
                .title="${this.title}"
                @load="${this.imageLoaded}"
                @error="${this.reloadImage}"
            />
        `;
    }

    async reloadImage(): Promise<void> {
        this.classList.add('loading');

        const isSubDomain = this.isSubDomain(this.src);
        let attempt = 0;
        while (true) {
            attempt++;
            const response = await fetch(this.src, { mode: isSubDomain ? undefined : 'cors' });
            if (response.ok) {
                const blob = await response.blob();
                this._imageRef.value.src = URL.createObjectURL(blob);
                break;
            }

            if (attempt > 5)
                break;

            const ms = Math.pow(2, attempt) * 1000;
            await this.timeout(ms);
        }
    }

    async imageLoaded(): Promise<void> {
        this.classList.remove('loading');
    }

    isSubDomain(url: string): boolean {
        return url.indexOf('actual.chat') > -1;
    }

    timeout(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }
}
