import { customElement, property } from 'lit/decorators.js';
import { createRef, Ref, ref } from 'lit/directives/ref.js';
import { css, html, LitElement, nothing } from 'lit';
import { delayAsync } from 'promises';
import { PROD_HOST } from '_constants';

@customElement('image-skeleton')
class ImageSkeleton extends LitElement {
    static styles = css`
      :host {
          display: block;
      }

      :host(.show-image-skeleton) {
          animation: pulse 2s infinite;
          background-color: var(--background-05);
      }

      :host(.show-image-skeleton) .image,
      :host(.show-image-thumbnail) .image,
      :host(.show-image-skeleton) .image-thumbnail,
      :host(.show-image-original) .image-thumbnail {
          visibility: hidden;
      }

      :host(.show-image-original) .image {
          display: block;
      }
      :host(.show-image-thumbnail) .image-thumbnail {
          display: block;
      }

      :host(.image-cover) {
          height: 100%;
          width: 100%;
      }

      :host(.image-cover) .image {
          object-fit: cover;
      }

      .image,
      .image-thumbnail {
        display: none;
        width: 100%;
        height: 100%;
        border-radius: inherit;
        object-fit: cover;
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
    @property() thumbnailSrc: string;
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
        if (this.thumbnailSrc && this.thumbnailSrc != "") {
            return html`
                <img
                    part='image'
                    ${ref(this._imageRef)}
                    class='image'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    .src='${this.src}'
                    .title='${this.title}'
                    @load='${this.imageLoaded}'
                    @error='${this.reloadImage}'
                />
                <img
                    part='image-thumbnail'
                    class='image-thumbnail'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    .src='${this.thumbnailSrc}'
                    .title='${this.title}'
                    @load='${this.thumbnailLoaded}'
                />
            `;
        } else {
            return html`
                <img
                    part='image'
                    ${ref(this._imageRef)}
                    class='image'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    .src='${this.src}'
                    .title='${this.title}'
                    @load='${this.imageLoaded}'
                    @error='${this.reloadImage}'
                />
            `;
        }
    }

    async reloadImage(): Promise<void> {
        this.classList.remove('show-image-original');
        this.classList.add('show-image-skeleton');

        const isSubDomain = this.isSubDomain(this.src);
        for (let attempt = 0; attempt < 10; attempt++) {
            if (attempt >= 1) {
                const delay = Math.min(30, Math.pow(2, attempt - 1));
                await delayAsync(delay * 1000);
            }
            const response = await fetch(this.src, { mode: isSubDomain ? undefined : 'cors' });
            if (response.ok) {
                const blob = await response.blob();
                if (this._imageRef.value)
                    this._imageRef.value.src = URL.createObjectURL(blob);
                return;
            }
        }
        if (this._imageRef.value)
            this._imageRef.value.src = this.src;
    }

    async imageLoaded(): Promise<void> {
        this.classList.remove('show-image-skeleton');
        this.classList.remove('show-image-thumbnail');
        if (!this.classList.contains('show-image-original'))
            this.classList.add('show-image-original');
    }

    async thumbnailLoaded(): Promise<void> {
        this.classList.remove('show-image-skeleton');
        if (!this.classList.contains('show-image-thumbnail') && (!this.classList.contains('show-image-original')))
            this.classList.add('show-image-thumbnail');
    }

    isSubDomain(url: string): boolean {
        return url.indexOf(PROD_HOST) > -1;
    }
}
