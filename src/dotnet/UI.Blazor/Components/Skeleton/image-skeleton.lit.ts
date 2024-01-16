import { customElement, property } from 'lit/decorators.js';
import { createRef, Ref, ref } from 'lit/directives/ref.js';
import { css, html, LitElement, nothing } from 'lit';
import { delayAsync } from 'promises';

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
                part="image"
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
        for (let attempt = 0; attempt < 10; attempt++) {
            if (attempt >= 1) {
                const delay = Math.min(30, Math.pow(2, attempt - 1));
                await delayAsync(delay * 1000);
            }
            const response = await fetch(this.src, { mode: isSubDomain ? undefined : 'cors' });
            if (response.ok) {
                const blob = await response.blob();
                this._imageRef.value.src = URL.createObjectURL(blob);
                break;
            }
        }
    }

    async imageLoaded(): Promise<void> {
        this.classList.remove('loading');
    }

    isSubDomain(url: string): boolean {
        return url.indexOf('actual.chat') > -1;
    }
}
