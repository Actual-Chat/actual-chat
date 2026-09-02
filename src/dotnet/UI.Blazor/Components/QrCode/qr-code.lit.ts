import { customElement, property, state } from 'lit/decorators.js';
import { css, html, LitElement, nothing, PropertyValues } from 'lit';
import { createRef, ref, Ref } from 'lit/directives/ref.js';
import QRCodeStyling, { Gradient, Options } from 'qr-code-styling';

// Drawn in one palette in every theme: a QR code inverted for dark mode is unreadable by
// many phone cameras, so the card it sits on stays light instead.
const GradientFrom = '#3f6ef4';
const GradientTo = '#7231b0';
// 'H' recovers 30% of the code, which is what pays for the hole the avatar punches in it.
const ErrorCorrectionLevel = 'H';
const DrawSize = 280;
const ImageLoadTimeoutMs = 3000;
// The centre takes a third of the code, per the design. It is drawn over the code by this
// component, not handed to qr-code-styling: the frame around the avatar is a card rather than a
// picture, and the library's own image path is unusable here anyway - it fetches whatever URL it
// is given to measure it, which CSP refuses for a data: URL and ORB refuses for the media proxy.
// An opaque card hides the dots it covers as surely as the library's own hideBackgroundDots, and
// error correction level H is what pays for them either way.
const CentreSize = 0.33;

@customElement('qr-code')
export class QrCode extends LitElement {
    static styles = css`
        :host {
            position: relative;
            display: block;
            width: var(--qr-code-size, 17.5rem);
            height: var(--qr-code-size, 17.5rem);
        }
        .c-container, .c-container svg {
            width: 100%;
            height: 100%;
        }
        .c-centre {
            position: absolute;
            left: 50%;
            top: 50%;
            transform: translate(-50%, -50%);
            display: flex;
            align-items: center;
            justify-content: center;
            width: calc(var(--c-centre-size) * 100%);
            height: calc(var(--c-centre-size) * 100%);
            border-radius: 22%;
            background: white;
        }
        /* Sized rather than inset by padding: a percentage padding resolves against the code's
           width, not the card's, and left the avatar barely half the card. */
        .c-centre img {
            display: block;
            width: 86%;
            height: 86%;
            border-radius: 18%;
            object-fit: cover;
        }
    `;

    private readonly containerRef: Ref<HTMLDivElement> = createRef();
    private qr?: QRCodeStyling;
    private drawVersion = 0;

    @property() url = '';
    @property() image = '';
    @state() private centreImage = '';

    protected render(): unknown {
        const centre = this.centreImage
            ? html`<div class="c-centre" style="--c-centre-size: ${CentreSize}">
                <img src=${this.centreImage} alt=""/>
            </div>`
            : nothing;
        return html`<div class="c-container" ${ref(this.containerRef)}></div>${centre}`;
    }

    protected updated(changed: PropertyValues): void {
        if (!changed.has('url') && !changed.has('image'))
            return;

        void this.draw();
    }

    // Private methods

    private async draw(): Promise<void> {
        const container = this.containerRef.value;
        if (!container || !this.url)
            return;

        const version = ++this.drawVersion;
        const image = await this.loadableImage();
        if (version !== this.drawVersion)
            return;

        this.centreImage = image ?? '';
        const options = this.getOptions();
        if (this.qr) {
            this.qr.update(options);
            return;
        }
        this.qr = new QRCodeStyling(options);
        this.qr.append(container);
    }

    // An avatar the browser refuses - a CDN hiccup, a blocked cross-origin read - would leave an
    // empty white card in the middle of the code; probing it first leaves the code bare instead.
    private loadableImage(): Promise<string | undefined> {
        const url = this.image;
        if (!url)
            return Promise.resolve(undefined);

        return new Promise<string | undefined>(resolve => {
            const probe = new Image();
            const done = (result: string | undefined) => {
                clearTimeout(timeout);
                probe.onload = probe.onerror = null;
                resolve(result);
            };
            const timeout = setTimeout(() => done(undefined), ImageLoadTimeoutMs);
            probe.onload = () => done(url);
            probe.onerror = () => done(undefined);
            probe.src = url;
        });
    }

    private getOptions(): Partial<Options> {
        const gradient: Gradient = {
            type: 'linear',
            rotation: Math.PI / 2,
            colorStops: [
                { offset: 0, color: GradientFrom },
                { offset: 1, color: GradientTo },
            ],
        };
        return {
            type: 'svg',
            width: DrawSize,
            height: DrawSize,
            margin: 0,
            data: this.url,
            qrOptions: { errorCorrectionLevel: ErrorCorrectionLevel },
            dotsOptions: { type: 'rounded', gradient },
            cornersSquareOptions: { type: 'extra-rounded', gradient },
            cornersDotOptions: { type: 'dot', gradient },
            backgroundOptions: { color: 'transparent' },
        };
    }
}
