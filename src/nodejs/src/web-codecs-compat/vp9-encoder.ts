// VP9 encoding on libav.js for compat level `vp9`, adapted from
// libavjs-webcodecs-polyfill's video-encoder.ts (0BSD). It takes and returns the
// browser's own VideoFrame and EncodedVideoChunk — at this level WebCodecs is
// present — and sets `lag-in-frames=0`, without which libvpx holds 25 frames of
// lookahead and emits nothing until flush().

import { getLogs } from 'logging';
import { WebCodecsCompat } from './init';

const { infoLog, warnLog } = getLogs('WebCodecsCompat');

const LIBAV_CODEC = 'libvpx-vp9';
// Microseconds, matching WebCodecs timestamps exactly: on a millisecond timebase
// frames less than 1ms apart collapse onto one pts, which libvpx rejects.
const TIMEBASE_DEN = 1_000_000;

// Declared here rather than taken from @libav.js/types, which pins 6.7.7
// against our 6.10.9 build; only what this file calls is typed.
interface LibAvPacket {
    data: Uint8Array;
    pts?: number;
    ptshi?: number;
    flags?: number;
}

interface LibAvFrame {
    data: Uint8Array;
    layout: PlaneLayout[];
    format: number;
    pts: number;
    ptshi: number;
    width: number;
    height: number;
    key_frame: number;
    pict_type: number;
}

interface LibAvCodecContextProps {
    pix_fmt?: number;
    width?: number;
    height?: number;
    bit_rate?: number;
    bit_ratehi?: number;
    framerate_num?: number;
    framerate_den?: number;
    profile?: number;
}

interface LibAvEncoderDef {
    codec: string;
    ctx: LibAvCodecContextProps;
    options: Record<string, string>;
}

interface LibAv {
    readonly AV_PIX_FMT_YUV420P: number;
    readonly AV_PIX_FMT_YUVA420P: number;
    readonly AV_PIX_FMT_NV12: number;
    readonly AV_PIX_FMT_RGBA: number;
    readonly AV_PIX_FMT_BGRA: number;
    readonly AV_PIX_FMT_RGB0: number;
    readonly AV_PIX_FMT_BGR0: number;
    readonly EAGAIN: number;
    ff_init_encoder(codec: string, def: LibAvEncoderDef): Promise<[number, number, number, number]>;
    ff_encode_multi(c: number, frame: number, pkt: number, frames: LibAvFrame[], fin?: boolean): Promise<LibAvPacket[]>;
    ff_free_encoder(c: number, frame: number, pkt: number): Promise<void>;
    ff_copyout_packet(pkt: number): Promise<LibAvPacket>;
    ff_copyin_frame(frame: number, from: LibAvFrame): Promise<void>;
    AVCodecContext_time_base_s(c: number, num: number, den: number): Promise<void>;
    AVCodecContext_extradata(c: number): Promise<number>;
    AVCodecContext_extradata_size(c: number): Promise<number>;
    AVFrame_pts_s(frame: number, pts: number): Promise<void>;
    AVFrame_ptshi_s(frame: number, ptshi: number): Promise<void>;
    AVFrame_key_frame_s(frame: number, value: number): Promise<void>;
    AVFrame_pict_type_s(frame: number, value: number): Promise<void>;
    av_frame_alloc(): Promise<number>;
    av_frame_free_js(frame: number): Promise<void>;
    avcodec_send_frame(c: number, frame: number): Promise<number>;
    avcodec_receive_packet(c: number, pkt: number): Promise<number>;
    sws_getContext(
        sw: number, sh: number, sf: number, dw: number, dh: number, df: number,
        flags: number, a: number, b: number, c: number): Promise<number>;
    sws_freeContext(sws: number): Promise<void>;
    sws_scale_frame(sws: number, dst: number, src: number): Promise<number>;
    copyout_u8(ptr: number, len: number): Promise<Uint8Array>;
    i64tof64(lo: number, hi: number): number;
    terminate(): void;
    f64toi64(value: number): [number, number];
}

interface LibAvWrapper {
    LibAV(options: { nothreads?: boolean; noworker?: boolean }): Promise<LibAv>;
}

interface ScalerState {
    width: number;
    height: number;
    format: number;
}

interface FramePlanes {
    data: Uint8Array;
    layout: PlaneLayout[];
    width: number;
    height: number;
}

/** Whether this codec string is one this encoder handles at all. */
export function isVp9Codec(codec: string): boolean {
    return codec.startsWith('vp09') || codec === 'vp9';
}

/** Drop-in for the browser VideoEncoder, restricted to VP9. Serialises all work
 *  on one promise chain, so callers see native ordering semantics. */
export class Vp9Encoder {
    private readonly _output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => void;
    private readonly _error: (error: DOMException) => void;
    private _chain: Promise<void> = Promise.resolve();
    private _libav: LibAv | null = null;
    private _codec = 0;
    private _c = 0;
    private _frame = 0;
    private _pkt = 0;
    private _sws = 0;
    private _swsFrame = 0;
    private _swsIn: ScalerState | null = null;
    private _swsOut: ScalerState | null = null;
    private _metadata: EncodedVideoChunkMetadata | null = null;
    private _hasExtradata = false;
    // Bumped by configure/reset/close so work queued under an older configuration
    // can neither emit nor decrement the queue of the current one.
    private _generation = 0;

    state: CodecState = 'unconfigured';
    encodeQueueSize = 0;

    constructor(init: {
        output: (chunk: EncodedVideoChunk, metadata?: EncodedVideoChunkMetadata) => void;
        error: (error: DOMException) => void;
    }) {
        this._output = init.output;
        this._error = init.error;
    }

    static isConfigSupported(config: VideoEncoderConfig): Promise<VideoEncoderSupport> {
        const supported = isVp9Codec(config.codec) && WebCodecsCompat.level !== 'none';

        return Promise.resolve({ supported, config: { ...config } });
    }

    configure(config: VideoEncoderConfig): void {
        if (this.state === 'closed')
            throw new DOMException('Encoder is closed', 'InvalidStateError');

        if (this._libav)
            this._chain = this._chain.then(() => this.free());

        this._generation++;
        // Frames queued under the previous configuration bail without decrementing.
        this.encodeQueueSize = 0;
        this.state = 'configured';
        this._chain = this._chain.then(async () => {
            const def = buildEncoderDef(config);
            const libav = this._libav = await getLibAv();
            this._metadata = { decoderConfig: { codec: config.codec } };
            [this._codec, this._c, this._frame, this._pkt] = await libav.ff_init_encoder(def.codec, def);
            await libav.AVCodecContext_time_base_s(this._c, 1, TIMEBASE_DEN);
            this._hasExtradata = false;
            this._sws = 0;
            this._swsFrame = 0;
            this._swsIn = null;
            this._swsOut = { width: config.width, height: config.height, format: def.ctx.pix_fmt! };
            infoLog?.log(`Vp9Encoder: configured ${config.width}x${config.height} @ ${config.bitrate ?? 0}bps`);
        }).catch((error: unknown) => this.fail(error));
    }

    encode(frame: VideoFrame, options: VideoEncoderEncodeOptions = {}): void {
        if (this.state !== 'configured')
            throw new DOMException('Unconfigured', 'InvalidStateError');

        // The frame must be read before this returns: WebCodecs lets the caller
        // close it as soon as encode() does, so a copy deferred onto the chain
        // would race a closed frame.
        const keyFrame = options.keyFrame === true;
        const timestamp = frame.timestamp;
        const format = frame.format;
        const whenCopied = readFramePlanes(frame);
        const generation = this._generation;
        this.encodeQueueSize++;
        this._chain = this._chain.then(async () => {
            if (generation !== this._generation)
                return;

            this.encodeQueueSize--;
            const { data: buffer, layout, width, height } = await whenCopied;
            const libav = this._libav!;
            const pixelFormat = toLibAvFormat(libav, format);
            if (pixelFormat === null) {
                warnLog?.log(`Vp9Encoder: unsupported frame format ${String(format)}`);
                return;
            }

            const [pts, ptshi] = libav.f64toi64(Math.round(timestamp));
            const input: LibAvFrame = {
                data: buffer, layout, format: pixelFormat, pts, ptshi,
                width, height,
                key_frame: keyFrame ? 1 : 0,
                pict_type: keyFrame ? 1 : 0,
            };
            const packets = await this.encodeFrame(input, keyFrame);
            if (packets.length > 0 && !this._hasExtradata)
                await this.readExtradata();

            this.emit(packets);
        }).catch((error: unknown) => this.fail(error));
    }

    /** Drains submitted frames. Deliberately does NOT signal libav's end of stream:
     *  that would retire the encoder, where WebCodecs flush() leaves it usable. */
    flush(): Promise<void> {
        // With lag-in-frames=0 every frame's packet is emitted by the encode that
        // produced it, so awaiting the queue is the whole of a flush.
        this._chain = this._chain.catch(() => undefined);

        return this._chain;
    }

    reset(): void {
        // Same contract as WebCodecs: drop queued work and return to unconfigured.
        this._generation++;
        this.encodeQueueSize = 0;
        if (this.state !== 'closed')
            this.state = 'unconfigured';

        this._chain = this._chain.then(() => this.free()).catch(() => undefined);
    }

    close(): void {
        this._generation++;
        this.state = 'closed';
        this.encodeQueueSize = 0;
        this._chain = this._chain.then(() => this.free()).catch(() => undefined);
    }

    // Private methods

    private async encodeFrame(input: LibAvFrame, keyFrame: boolean): Promise<LibAvPacket[]> {
        const libav = this._libav!;
        const out = this._swsOut!;
        const needsScaler = input.width !== out.width
            || input.height !== out.height
            || input.format !== out.format;
        if (!needsScaler)
            return libav.ff_encode_multi(this._c, this._frame, this._pkt, [input]);

        if (!this._sws
            || this._swsIn === null
            || this._swsIn.width !== input.width
            || this._swsIn.height !== input.height
            || this._swsIn.format !== input.format) {
            if (this._sws)
                await libav.sws_freeContext(this._sws);

            this._swsIn = { width: input.width, height: input.height, format: input.format };
            // SWS_BILINEAR (2): the downscaler already sized the frame, so this
            // only ever converts colour space in practice.
            this._sws = await libav.sws_getContext(
                input.width, input.height, input.format,
                out.width, out.height, out.format, 2, 0, 0, 0);
            this._swsFrame ||= await libav.av_frame_alloc();
        }

        const [, scaled, , , , , sent] = await Promise.all([
            libav.ff_copyin_frame(this._frame, input),
            libav.sws_scale_frame(this._sws, this._swsFrame, this._frame),
            libav.AVFrame_pts_s(this._swsFrame, input.pts),
            libav.AVFrame_ptshi_s(this._swsFrame, input.ptshi),
            libav.AVFrame_key_frame_s(this._swsFrame, keyFrame ? 1 : 0),
            libav.AVFrame_pict_type_s(this._swsFrame, keyFrame ? 1 : 0),
            libav.avcodec_send_frame(this._c, this._swsFrame),
        ]);
        if (scaled < 0 || sent < 0) {
            throw new Error(
                `Vp9Encoder: scale=${scaled} send=${sent} `
                + `in=${input.width}x${input.height}/fmt${input.format}/planes${input.layout.length} `
                + `out=${out.width}x${out.height}/fmt${out.format} sws=${this._sws}`);
        }

        const packets: LibAvPacket[] = [];
        for (;;) {
            const received = await libav.avcodec_receive_packet(this._c, this._pkt);
            if (received === -libav.EAGAIN)
                break;
            if (received < 0)
                throw new Error('Vp9Encoder: encoding failed');

            packets.push(await libav.ff_copyout_packet(this._pkt));
        }

        return packets;
    }

    private async readExtradata(): Promise<void> {
        const libav = this._libav!;
        const ptr = await libav.AVCodecContext_extradata(this._c);
        const size = await libav.AVCodecContext_extradata_size(this._c);
        if (ptr && size && this._metadata?.decoderConfig)
            this._metadata.decoderConfig.description = await libav.copyout_u8(ptr, size);

        this._hasExtradata = true;
    }

    private emit(packets: LibAvPacket[]): void {
        const libav = this._libav!;
        for (const packet of packets) {
            const chunk = new EncodedVideoChunk({
                type: (packet.flags ?? 0) & 1 ? 'key' : 'delta',
                timestamp: libav.i64tof64(packet.pts ?? 0, packet.ptshi ?? 0),
                data: packet.data,
            });
            this._output(chunk, this._metadata ?? undefined);
        }
    }

    private async free(): Promise<void> {
        const libav = this._libav;
        if (!libav)
            return;

        this._libav = null;
        if (this._swsFrame)
            await libav.av_frame_free_js(this._swsFrame);
        if (this._sws)
            await libav.sws_freeContext(this._sws);

        this._sws = 0;
        this._swsFrame = 0;
        await libav.ff_free_encoder(this._c, this._frame, this._pkt);
        // Each instance owns a dedicated Worker holding a libvpx wasm heap, and a
        // Worker is never collected — without this, every configure() leaks one.
        libav.terminate();
    }

    private fail(error: unknown): void {
        if (this.state === 'closed')
            return;

        this.state = 'closed';
        const message = error instanceof Error ? error.message : String(error);
        this._error(new DOMException(`Vp9Encoder: ${message}`, 'EncodingError'));
        // The adapter skips close() once state is 'closed', so this is the last
        // chance to give the instance back.
        this._chain = this._chain.then(() => this.free()).catch(() => undefined);
    }
}

function getLibAv(): Promise<LibAv> {
    const wrapper = (globalThis as { LibAV?: LibAvWrapper }).LibAV;
    if (!wrapper?.LibAV)
        throw new Error('Vp9Encoder: libav.js is not loaded');

    return wrapper.LibAV({ nothreads: true });
}

function buildEncoderDef(config: VideoEncoderConfig): LibAvEncoderDef {
    const ctx: LibAvCodecContextProps = {
        pix_fmt: 0, // AV_PIX_FMT_YUV420P; the only format VP9 profile 0 takes
        width: config.width,
        height: config.height,
        bit_rate: config.bitrate ?? 0,
        framerate_num: Math.round(config.framerate ?? 30),
        framerate_den: 1,
    };
    const profile = Number(config.codec.split('.')[1]);
    if (Number.isInteger(profile) && profile >= 0 && profile <= 3)
        ctx.profile = profile;

    const options: Record<string, string> = {
        'deadline': 'realtime',
        'cpu-used': '8',
        // The whole reason this class exists: libvpx defaults to 25 frames of
        // lookahead, so a realtime encoder emits nothing until flush().
        'lag-in-frames': '0',
        'error-resilient': '1',
        'row-mt': '1',
        'tile-columns': '2',
    };

    return { codec: LIBAV_CODEC, ctx, options };
}

function toLibAvFormat(libav: LibAv, format: VideoPixelFormat | null): number | null {
    switch (format) {
    case 'I420':
        return libav.AV_PIX_FMT_YUV420P;
    case 'I420A':
        return libav.AV_PIX_FMT_YUVA420P;
    case 'NV12':
        return libav.AV_PIX_FMT_NV12;
    case 'RGBA':
        return libav.AV_PIX_FMT_RGBA;
    case 'RGBX':
        return libav.AV_PIX_FMT_RGB0;
    case 'BGRA':
        return libav.AV_PIX_FMT_BGRA;
    case 'BGRX':
        return libav.AV_PIX_FMT_BGR0;
    default:
        return null;
    }
}

/**
 * The VideoEncoder implementation for `codec` in this realm: Vp9Encoder at compat
 * level `vp9` or `full` for VP9, the browser's own otherwise.
 */
export function getVideoEncoderClass(codec: string): typeof VideoEncoder {
    if (WebCodecsCompat.level === 'none' || !isVp9Codec(codec))
        return VideoEncoder;

    // Vp9Encoder implements what this app calls, not the full spec surface -
    // it is not an EventTarget and has no ondequeue.
    return Vp9Encoder as unknown as typeof VideoEncoder;
}

/**
 * Planes and layout from either frame realm: at `full` the pipeline mixes native
 * capture frames with polyfilled ones the app builds, and both reach the encoder.
 */
function readFramePlanes(frame: VideoFrame): Promise<FramePlanes> {
    const polyfilled = frame as unknown as {
        _libavGetData?: () => Uint8Array;
        _libavGetLayout?: () => PlaneLayout[];
    };
    if (typeof polyfilled._libavGetData === 'function' && typeof polyfilled._libavGetLayout === 'function') {
        return Promise.resolve({
            data: polyfilled._libavGetData(),
            layout: polyfilled._libavGetLayout(),
            width: frame.codedWidth,
            height: frame.codedHeight,
        });
    }

    // copyTo defaults to the visible rect, so the planes describe THAT. Declaring
    // the coded size instead makes libav read stale heap past the end of every row
    // - silently, because its copy clamps rather than throwing.
    const rect = frame.visibleRect;
    const width = rect?.width ?? frame.codedWidth;
    const height = rect?.height ?? frame.codedHeight;
    const data = new Uint8Array(frame.allocationSize());

    return frame.copyTo(data).then(layout => ({ data, layout, width, height }));
}
