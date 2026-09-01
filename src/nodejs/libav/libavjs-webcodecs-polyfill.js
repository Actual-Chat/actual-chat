(function (global, factory) {
    typeof exports === 'object' && typeof module !== 'undefined' ? factory(exports) :
    typeof define === 'function' && define.amd ? define(['exports'], factory) :
    (global = typeof globalThis !== 'undefined' ? globalThis : global || self, factory(global.LibAVWebCodecs = {}));
})(this, (function (exports) { 'use strict';

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    let EncodedAudioChunk$1 = class EncodedAudioChunk {
        constructor(init) {
            /* 1. If init.transfer contains more than one reference to the same
             *    ArrayBuffer, then throw a DataCloneError DOMException. */
            // 2. For each transferable in init.transfer:
            /* 1. If [[Detached]] internal slot is true, then throw a
             *    DataCloneError DOMException. */
            // (not worth checking in a polyfill)
            /* 3. Let chunk be a new EncodedAudioChunk object, initialized as
             *    follows */
            {
                // 1. Assign init.type to [[type]].
                this.type = init.type;
                // 2. Assign init.timestamp to [[timestamp]].
                this.timestamp = init.timestamp;
                /* 3. If init.duration exists, assign it to [[duration]], or assign
                 *    null otherwise. */
                if (typeof init.duration === "number")
                    this.duration = init.duration;
                else
                    this.duration = null;
                // 4. Assign init.data.byteLength to [[byte length]];
                this.byteLength = init.data.byteLength;
                /* 5. If init.transfer contains an ArrayBuffer referenced by
                 *    init.data the User Agent MAY choose to: */
                let transfer = false;
                if (init.transfer) {
                    /* 1. Let resource be a new media resource referencing sample
                     *    data in init.data. */
                    let inBuffer;
                    if (init.data.buffer)
                        inBuffer = init.data.buffer;
                    else
                        inBuffer = init.data;
                    let t;
                    if (init.transfer instanceof Array)
                        t = init.transfer;
                    else
                        t = Array.from(init.transfer);
                    for (const b of t) {
                        if (b === inBuffer) {
                            transfer = true;
                            break;
                        }
                    }
                }
                // 6. Otherwise:
                // 1. Assign a copy of init.data to [[internal data]].
                const data = new Uint8Array(init.data.buffer || init.data, init.data.byteOffset || 0, init.data.BYTES_PER_ELEMENT
                    ? (init.data.BYTES_PER_ELEMENT * init.data.length)
                    : init.data.byteLength);
                if (transfer)
                    this._data = data;
                else
                    this._data = data.slice(0);
            }
            // 4. For each transferable in init.transfer:
            // 1. Perform DetachArrayBuffer on transferable
            // (already done by transferring)
            // 5. Return chunk.
        }
        // Internal
        _libavGetData() { return this._data; }
        copyTo(destination) {
            (new Uint8Array(destination.buffer || destination, destination.byteOffset || 0)).set(this._data);
        }
    };

    (function (Object) {
      typeof globalThis !== 'object' && (
        this ?
          get() :
          (Object.defineProperty(Object.prototype, '_T_', {
            configurable: true,
            get: get
          }), _T_)
      );
      function get() {
        var global = this || self;
        global.globalThis = global;
        delete Object.prototype._T_;
      }
    }(Object));

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    let AudioData$1 = class AudioData {
        constructor(init) {
            // 1. If init is not a valid AudioDataInit, throw a TypeError.
            AudioData._checkValidAudioDataInit(init);
            /* 2. If init.transfer contains more than one reference to the same
             *    ArrayBuffer, then throw a DataCloneError DOMException. */
            // 3. For each transferable in init.transfer:
            // 1. If [[Detached]] internal slot is true, then throw a DataCloneError DOMException.
            // (Not worth doing in polyfill)
            // 4. Let frame be a new AudioData object, initialized as follows:
            {
                // 1. Assign false to [[Detached]].
                // (not doable in polyfill)
                // 2. Assign init.format to [[format]].
                this.format = init.format;
                // 3. Assign init.sampleRate to [[sample rate]].
                this.sampleRate = init.sampleRate;
                // 4. Assign init.numberOfFrames to [[number of frames]].
                this.numberOfFrames = init.numberOfFrames;
                // 5. Assign init.numberOfChannels to [[number of channels]].
                this.numberOfChannels = init.numberOfChannels;
                // 6. Assign init.timestamp to [[timestamp]].
                this.timestamp = init.timestamp;
                /* 7. If init.transfer contains an ArrayBuffer referenced by
                 * init.data the User Agent MAY choose to: */
                let transfer = false;
                if (init.transfer) {
                    // 1. Let resource be a new media resource referencing sample data in data.
                    let inBuffer;
                    if (init.data.buffer)
                        inBuffer = init.data.buffer;
                    else
                        inBuffer = init.data;
                    let t;
                    if (init.transfer instanceof Array)
                        t = init.transfer;
                    else
                        t = Array.from(init.transfer);
                    for (const b of t) {
                        if (b === inBuffer) {
                            transfer = true;
                            break;
                        }
                    }
                }
                // 8. Otherwise:
                // 1. Let resource be a media resource containing a copy of init.data.
                // 9. Let resourceReference be a reference to resource.
                let inData, byteOffset = 0;
                if (transfer) {
                    inData = init.data;
                    byteOffset = init.data.byteOffset || 0;
                }
                else {
                    inData = init.data.slice(0);
                }
                const resourceReference = audioView(init.format, inData.buffer || inData, byteOffset);
                // 10. Assign resourceReference to [[resource reference]].
                this._data = resourceReference;
            }
            // 5. For each transferable in init.transfer:
            // 1. Perform DetachArrayBuffer on transferable
            // (Already done by transferring)
            // 6. Return frame.
            // Duration not calculated in spec?
            this.duration = init.numberOfFrames / init.sampleRate * 1000000;
        }
        /**
         * Convert a polyfill AudioData to a native AudioData.
         * @param opts  Conversion options
         */
        toNative(opts = {}) {
            const ret = new globalThis.AudioData({
                data: this._data,
                format: this.format,
                sampleRate: this.sampleRate,
                numberOfFrames: this.numberOfFrames,
                numberOfChannels: this.numberOfChannels,
                timestamp: this.timestamp,
                transfer: opts.transfer ? [this._data.buffer] : []
            });
            if (opts.transfer)
                this.close();
            return ret;
        }
        /**
         * Convert a native AudioData to a polyfill AudioData. WARNING: Inefficient,
         * as the data cannot be transferred out.
         * @param from  AudioData to copy in
         */
        static fromNative(from /* native AudioData */) {
            const ad = from;
            const isInterleaved_ = isInterleaved(ad.format);
            const planes = isInterleaved_ ? 1 : ad.numberOfChannels;
            const sizePerPlane = ad.allocationSize({
                format: ad.format,
                planeIndex: 0
            });
            const data = new Uint8Array(sizePerPlane * planes);
            for (let p = 0; p < planes; p++) {
                ad.copyTo(data.subarray(p * sizePerPlane), {
                    format: ad.format,
                    planeIndex: p
                });
            }
            return new AudioData({
                data,
                format: ad.format,
                sampleRate: ad.sampleRate,
                numberOfFrames: ad.numberOfFrames,
                numberOfChannels: ad.numberOfChannels,
                timestamp: ad.timestamp,
                transfer: [data.buffer]
            });
        }
        // Internal
        _libavGetData() { return this._data; }
        static _checkValidAudioDataInit(init) {
            // 1. If sampleRate less than or equal to 0, return false.
            if (init.sampleRate <= 0)
                throw new TypeError(`Invalid sample rate ${init.sampleRate}`);
            // 2. If numberOfFrames = 0, return false.
            if (init.numberOfFrames <= 0)
                throw new TypeError(`Invalid number of frames ${init.numberOfFrames}`);
            // 3. If numberOfChannels = 0, return false.
            if (init.numberOfChannels <= 0)
                throw new TypeError(`Invalid number of channels ${init.numberOfChannels}`);
            // 4. Verify data has enough data by running the following steps:
            {
                // 1. Let totalSamples be the product of multiplying numberOfFrames by numberOfChannels.
                const totalSamples = init.numberOfFrames * init.numberOfChannels;
                // 2. Let bytesPerSample be the number of bytes per sample, as defined by the format.
                const bytesPerSample_ = bytesPerSample(init.format);
                // 3. Let totalSize be the product of multiplying bytesPerSample with totalSamples.
                const totalSize = bytesPerSample_ * totalSamples;
                // 4. Let dataSize be the size in bytes of data.
                const dataSize = init.data.byteLength;
                // 5. If dataSize is less than totalSize, return false.
                if (dataSize < totalSize)
                    throw new TypeError(`This audio data must be at least ${totalSize} bytes`);
            }
            // 5. Return true.
        }
        allocationSize(options) {
            // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
            if (this._data === null)
                throw new DOMException("Detached", "InvalidStateError");
            /* 2. Let copyElementCount be the result of running the Compute Copy
             * Element Count algorithm with options. */
            const copyElementCount = this._computeCopyElementCount(options);
            // 3. Let destFormat be the value of [[format]].
            let destFormat = this.format;
            // 4. If options.format exists, assign options.format to destFormat.
            if (options.format)
                destFormat = options.format;
            /* 5. Let bytesPerSample be the number of bytes per sample, as defined
             * by the destFormat. */
            const bytesPerSample_ = bytesPerSample(destFormat);
            /* 6. Return the product of multiplying bytesPerSample by
             * copyElementCount. */
            return bytesPerSample_ * copyElementCount;
        }
        _computeCopyElementCount(options) {
            // 1. Let destFormat be the value of [[format]].
            let destFormat = this.format;
            // 2. If options.format exists, assign options.format to destFormat.
            if (options.format)
                destFormat = options.format;
            /* 3. If destFormat describes an interleaved AudioSampleFormat and
             * options.planeIndex is greater than 0, throw a RangeError. */
            const isInterleaved_ = isInterleaved(destFormat);
            if (isInterleaved_) {
                if (options.planeIndex > 0)
                    throw new RangeError("Invalid plane");
            }
            /* 4. Otherwise, if destFormat describes a planar AudioSampleFormat and
             * if options.planeIndex is greater or equal to [[number of channels]],
             * throw a RangeError. */
            else if (options.planeIndex >= this.numberOfChannels)
                throw new RangeError("Invalid plane");
            /* 5. If [[format]] does not equal destFormat and the User Agent does
             * not support the requested AudioSampleFormat conversion, throw a
             * NotSupportedError DOMException. Conversion to f32-planar MUST always
             * be supported. */
            if (this.format !== destFormat &&
                destFormat !== "f32-planar")
                throw new DOMException("Only conversion to f32-planar is supported", "NotSupportedError");
            /* 6. Let frameCount be the number of frames in the plane identified by
             * options.planeIndex. */
            const frameCount = this.numberOfFrames; // All planes have the same number of frames
            /* 7. If options.frameOffset is greater than or equal to frameCount,
             * throw a RangeError. */
            const frameOffset = options.frameOffset || 0;
            if (frameOffset >= frameCount)
                throw new RangeError("Frame offset out of range");
            /* 8. Let copyFrameCount be the difference of subtracting
             * options.frameOffset from frameCount. */
            let copyFrameCount = frameCount - frameOffset;
            // 9. If options.frameCount exists:
            if (typeof options.frameCount === "number") {
                /* 1. If options.frameCount is greater than copyFrameCount, throw a
                 * RangeError. */
                if (options.frameCount >= copyFrameCount)
                    throw new RangeError("Frame count out of range");
                // 2. Otherwise, assign options.frameCount to copyFrameCount.
                copyFrameCount = options.frameCount;
            }
            // 10. Let elementCount be copyFrameCount.
            let elementCount = copyFrameCount;
            /* 11. If destFormat describes an interleaved AudioSampleFormat,
             * mutliply elementCount by [[number of channels]] */
            if (isInterleaved_)
                elementCount *= this.numberOfChannels;
            // 12. return elementCount.
            return elementCount;
        }
        copyTo(destination, options) {
            // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
            if (this._data === null)
                throw new DOMException("Detached", "InvalidStateError");
            /* 2. Let copyElementCount be the result of running the Compute Copy
             * Element Count algorithm with options. */
            const copyElementCount = this._computeCopyElementCount(options);
            // 3. Let destFormat be the value of [[format]].
            let destFormat = this.format;
            // 4. If options.format exists, assign options.format to destFormat.
            if (options.format)
                destFormat = options.format;
            /* 5. Let bytesPerSample be the number of bytes per sample, as defined
             * by the destFormat. */
            const bytesPerSample_ = bytesPerSample(destFormat);
            /* 6. If the product of multiplying bytesPerSample by copyElementCount
             * is greater than destination.byteLength, throw a RangeError. */
            if (bytesPerSample_ * copyElementCount > destination.byteLength)
                throw new RangeError("Buffer too small");
            /* 7. Let resource be the media resource referenced by [[resource
             * reference]]. */
            const resource = this._data;
            /* 8. Let planeFrames be the region of resource corresponding to
             * options.planeIndex. */
            const planeFrames = resource.subarray(options.planeIndex * this.numberOfFrames);
            const frameOffset = options.frameOffset || 0;
            const numberOfChannels = this.numberOfChannels;
            /* 9. Copy elements of planeFrames into destination, starting with the
             * frame positioned at options.frameOffset and stopping after
             * copyElementCount samples have been copied. If destFormat does not
             * equal [[format]], convert elements to the destFormat
             * AudioSampleFormat while making the copy. */
            if (this.format === destFormat) {
                const dest = audioView(destFormat, destination.buffer || destination, destination.byteOffset || 0);
                if (isInterleaved(destFormat)) {
                    dest.set(planeFrames.subarray(frameOffset * numberOfChannels, frameOffset * numberOfChannels + copyElementCount));
                }
                else {
                    dest.set(planeFrames.subarray(frameOffset, frameOffset + copyElementCount));
                }
            }
            else {
                // Actual conversion necessary. Always to f32-planar.
                const out = audioView(destFormat, destination.buffer || destination, destination.byteOffset || 0);
                // First work out the conversion
                let sub = 0;
                let div = 1;
                switch (this.format) {
                    case "u8":
                    case "u8-planar":
                        sub = 0x80;
                        div = 0x80;
                        break;
                    case "s16":
                    case "s16-planar":
                        div = 0x8000;
                        break;
                    case "s32":
                    case "s32-planar":
                        div = 0x80000000;
                        break;
                }
                // Then do it
                if (isInterleaved(this.format)) {
                    for (let i = options.planeIndex + frameOffset * numberOfChannels, o = 0; o < copyElementCount; i += numberOfChannels, o++)
                        out[o] = (planeFrames[i] - sub) / div;
                }
                else {
                    for (let i = frameOffset, o = 0; o < copyElementCount; i++, o++)
                        out[o] = (planeFrames[i] - sub) / div;
                }
            }
        }
        clone() {
            // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
            if (this._data === null)
                throw new DOMException("Detached", "InvalidStateError");
            /* 2. Return the result of running the Clone AudioData algorithm with
             * this. */
            return new AudioData({
                format: this.format,
                sampleRate: this.sampleRate,
                numberOfFrames: this.numberOfFrames,
                numberOfChannels: this.numberOfChannels,
                timestamp: this.timestamp,
                data: this._data
            });
        }
        close() {
            this._data = null;
        }
    };
    /**
     * Construct the appropriate type of ArrayBufferView for the given sample
     * format and buffer.
     * @param format  Sample format
     * @param buffer  ArrayBuffer (NOT view)
     * @param byteOffset  Offset into the buffer
     */
    function audioView(format, buffer, byteOffset) {
        switch (format) {
            case "u8":
            case "u8-planar":
                return new Uint8Array(buffer, byteOffset);
            case "s16":
            case "s16-planar":
                return new Int16Array(buffer, byteOffset);
            case "s32":
            case "s32-planar":
                return new Int32Array(buffer, byteOffset);
            case "f32":
            case "f32-planar":
                return new Float32Array(buffer, byteOffset);
            default:
                throw new TypeError("Invalid AudioSampleFormat");
        }
    }
    /**
     * Number of bytes per sample of this format.
     * @param format  Sample format
     */
    function bytesPerSample(format) {
        switch (format) {
            case "u8":
            case "u8-planar":
                return 1;
            case "s16":
            case "s16-planar":
                return 2;
            case "s32":
            case "s32-planar":
            case "f32":
            case "f32-planar":
                return 4;
            default:
                throw new TypeError("Invalid AudioSampleFormat");
        }
    }
    /**
     * Is this format interleaved?
     * @param format  Sample format
     */
    function isInterleaved(format) {
        switch (format) {
            case "u8":
            case "s16":
            case "s32":
            case "f32":
                return true;
            case "u8-planar":
            case "s16-planar":
            case "s32-planar":
            case "f32-planar":
                return false;
            default:
                throw new TypeError("Invalid AudioSampleFormat");
        }
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    /* Unfortunately, browsers don't let us extend EventTarget. So, we implement an
     * EventTarget interface with a “has-a” relationship instead of an “is-a”
     * relationship. We have an event target, and expose its event functions as our
     * own. */
    class HasAEventTarget {
        constructor() {
            const ev = this._eventer = new EventTarget();
            this.addEventListener = ev.addEventListener.bind(ev);
            this.removeEventListener = ev.removeEventListener.bind(ev);
            this.dispatchEvent = ev.dispatchEvent.bind(ev);
        }
    }
    class DequeueEventTarget extends HasAEventTarget {
        constructor() {
            super();
            this.addEventListener("dequeue", ev => {
                if (this.ondequeue)
                    this.ondequeue(ev);
            });
        }
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$8 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    // Wrapper function to use
    let LibAVWrapper = null;
    // Currently available libav instances
    const libavs = [];
    // Options required to create a LibAV instance
    let libavOptions = {};
    /**
     * Supported decoders.
     */
    let decoders = null;
    /**
     * Supported encoders.
     */
    let encoders = null;
    /**
     * Set the libav wrapper to use.
     */
    function setLibAV(to) {
        LibAVWrapper = to;
    }
    /**
     * Set the libav loading options.
     */
    function setLibAVOptions(to) {
        libavOptions = to;
    }
    /**
     * Get a libav instance.
     */
    function get() {
        return __awaiter$8(this, void 0, void 0, function* () {
            if (libavs.length)
                return libavs.shift();
            return yield LibAVWrapper.LibAV(libavOptions);
        });
    }
    /**
     * Free a libav instance for later reuse.
     */
    function free(libav) {
        libavs.push(libav);
    }
    /**
     * Get the list of encoders/decoders supported by libav (which are also
     * supported by this polyfill)
     * @param encoders  Check for encoders instead of decoders
     */
    function codecs(encoders) {
        return __awaiter$8(this, void 0, void 0, function* () {
            const libav = yield get();
            const ret = [];
            for (const [avname, codec] of [
                ["flac", "flac"],
                ["libopus", "opus"],
                ["libvorbis", "vorbis"],
                ["libaom-av1", "av01"],
                ["libvpx-vp9", "vp09"],
                ["libvpx", "vp8"]
            ]) {
                if (encoders) {
                    if (yield libav.avcodec_find_encoder_by_name(avname))
                        ret.push(codec);
                }
                else {
                    if (yield libav.avcodec_find_decoder_by_name(avname))
                        ret.push(codec);
                }
            }
            free(libav);
            return ret;
        });
    }
    /**
     * Load the lists of supported decoders and encoders.
     */
    function load$2() {
        return __awaiter$8(this, void 0, void 0, function* () {
            LibAVWrapper = LibAVWrapper || LibAV;
            decoders = yield codecs(false);
            encoders = yield codecs(true);
        });
    }
    /**
     * Convert a decoder from the codec registry (or libav.js-specific parameters)
     * to libav.js. Returns null if unsupported.
     */
    function decoder(codec, config) {
        if (typeof codec === "string") {
            codec = codec.replace(/\..*/, "");
            let outCodec = codec;
            switch (codec) {
                // Audio
                case "flac":
                    if (typeof config.description === "undefined") {
                        // description is required per spec, but one can argue, if this limitation makes sense
                        return null;
                    }
                    break;
                case "opus":
                    if (typeof config.description !== "undefined") {
                        // ogg bitstream is not supported by the current implementation
                        return null;
                    }
                    outCodec = "libopus";
                    break;
                case "vorbis":
                    if (typeof config.description === "undefined") {
                        // description is required per spec, but one can argue, if this limitation makes sense
                        return null;
                    }
                    outCodec = "libvorbis";
                    break;
                // Video
                case "av01":
                    outCodec = "libaom-av1";
                    break;
                case "vp09":
                    outCodec = "libvpx-vp9";
                    break;
                case "vp8":
                    outCodec = "libvpx";
                    break;
                // Unsupported
                case "mp3":
                case "mp4a":
                case "ulaw":
                case "alaw":
                case "avc1":
                case "avc3":
                case "hev1":
                case "hvc1":
                    return null;
                // Unrecognized
                default:
                    throw new TypeError("Unrecognized codec");
            }
            // Check whether we actually support this codec
            if (!(decoders.indexOf(codec) >= 0))
                return null;
            return { codec: outCodec };
        }
        else {
            return codec.libavjs;
        }
    }
    /**
     * Convert an encoder from the codec registry (or libav.js-specific parameters)
     * to libav.js. Returns null if unsupported.
     */
    function encoder(codec, config) {
        if (typeof codec === "string") {
            const codecParts = codec.split(".");
            codec = codecParts[0];
            let outCodec = codec;
            const ctx = {};
            const options = {};
            let video = false;
            switch (codec) {
                // Audio
                case "flac":
                    ctx.sample_fmt = 2 /* S32 */;
                    ctx.bit_rate = 0;
                    if (typeof config.flac === "object" &&
                        config.flac !== null) {
                        const flac = config.flac;
                        // FIXME: Check block size
                        if (typeof flac.blockSize === "number")
                            ctx.frame_size = flac.blockSize;
                        if (typeof flac.compressLevel === "number") {
                            // Not supported
                            return null;
                        }
                    }
                    break;
                case "opus":
                    outCodec = "libopus";
                    ctx.sample_fmt = 3 /* FLT */;
                    ctx.sample_rate = 48000;
                    if (typeof config.opus === "object" &&
                        config.opus !== null) {
                        const opus = config.opus;
                        // FIXME: Check frame duration
                        if (typeof opus.frameDuration === "number")
                            options.frame_duration = "" + (opus.frameDuration / 1000);
                        if (typeof opus.complexity !== "undefined") {
                            // We don't support the complexity option
                            return null;
                        }
                        if (typeof opus.packetlossperc === "number") {
                            if (opus.packetlossperc < 0 || opus.packetlossperc > 100)
                                return null;
                            options.packet_loss = "" + opus.packetlossperc;
                        }
                        if (typeof opus.useinbandfec === "boolean")
                            options.fec = opus.useinbandfec ? "1" : "0";
                        if (typeof opus.usedtx === "boolean") {
                            // We don't support the usedtx option
                            return null;
                        }
                        if (typeof opus.format === "string") {
                            // ogg bitstream is not supported
                            if (opus.format !== "opus")
                                return null;
                        }
                    }
                    break;
                case "vorbis":
                    outCodec = "libvorbis";
                    ctx.sample_fmt = 8 /* FLTP */;
                    break;
                // Video
                case "av01":
                    video = true;
                    outCodec = "libaom-av1";
                    if (config.latencyMode === "realtime") {
                        options.usage = "realtime";
                        options["cpu-used"] = "8";
                    }
                    // Check for advanced options
                    if (!av1Advanced(codecParts, ctx))
                        return null;
                    break;
                case "vp09":
                    video = true;
                    outCodec = "libvpx-vp9";
                    if (config.latencyMode === "realtime") {
                        options.quality = "realtime";
                        options["cpu-used"] = "8";
                    }
                    // Check for advanced options
                    if (!vp9Advanced(codecParts, ctx))
                        return null;
                    break;
                case "vp8":
                    video = true;
                    outCodec = "libvpx";
                    if (config.latencyMode === "realtime") {
                        options.quality = "realtime";
                        options["cpu-used"] = "8";
                    }
                    break;
                // Unsupported
                case "mp3":
                case "mp4a":
                case "ulaw":
                case "alaw":
                case "avc1":
                    return null;
                // Unrecognized
                default:
                    throw new TypeError("Unrecognized codec");
            }
            // Check whether we actually support this codec
            if (!(encoders.indexOf(codec) >= 0))
                return null;
            if (video) {
                if (typeof ctx.pix_fmt !== "number")
                    ctx.pix_fmt = 0 /* YUV420P */;
                const width = ctx.width = config.width;
                const height = ctx.height = config.height;
                if (config.framerate) {
                    /* FIXME: We need this as a rational, not a floating point, and
                     * this is obviously not the right way to do it */
                    ctx.framerate_num = Math.round(config.framerate);
                    ctx.framerate_den = 1;
                }
                // Check for non-square pixels
                const dWidth = config.displayWidth || config.width;
                const dHeight = config.displayHeight || config.height;
                if (dWidth !== width || dHeight !== height) {
                    ctx.sample_aspect_ratio_num = dWidth * height;
                    ctx.sample_aspect_ratio_den = dHeight * width;
                }
            }
            else {
                if (!ctx.sample_rate)
                    ctx.sample_rate = config.sampleRate || 48000;
                if (config.numberOfChannels) {
                    const n = config.numberOfChannels;
                    ctx.channel_layout = (n === 1) ? 4 : ((1 << n) - 1);
                }
            }
            if (typeof ctx.bit_rate !== "number" && config.bitrate) {
                // NOTE: CBR requests are, quite rightly, ignored
                ctx.bit_rate = config.bitrate;
            }
            return {
                codec: outCodec,
                ctx, options
            };
        }
        else {
            return codec.libavjs;
        }
    }
    /**
     * Handler for advanced options for AV1.
     * @param codecParts  .-separated parts of the codec string.
     * @param ctx  Context to populate with advanced options.
     */
    function av1Advanced(codecParts, ctx) {
        if (codecParts[1]) {
            const profile = +codecParts[1];
            if (profile >= 0 && profile <= 2)
                ctx.profile = profile;
            else
                throw new TypeError("Invalid AV1 profile");
        }
        if (codecParts[2]) {
            const level = +codecParts[2];
            if (level >= 0 && level <= 23)
                ctx.level = level;
            else
                throw new TypeError("Invalid AV1 level");
        }
        if (codecParts[3]) {
            switch (codecParts[3]) {
                case "M":
                    // Default
                    break;
                case "H":
                    if (ctx.level && ctx.level >= 8) {
                        // Valid but unsupported
                        return false;
                    }
                    else {
                        throw new TypeError("The AV1 high tier is only available for level 4.0 and up");
                    }
                default:
                    throw new TypeError("Invalid AV1 tier");
            }
        }
        if (codecParts[4]) {
            const depth = +codecParts[3];
            if (depth === 10 || depth === 12) {
                // Valid but unsupported
                return false;
            }
            else if (depth !== 8) {
                throw new TypeError("Invalid AV1 bit depth");
            }
        }
        if (codecParts[5]) {
            // Monochrome
            switch (codecParts[5]) {
                case "0":
                    // Default
                    break;
                case "1":
                    // Valid but unsupported
                    return false;
                default:
                    throw new TypeError("Invalid AV1 monochrome flag");
            }
        }
        if (codecParts[6]) {
            // Subsampling mode
            switch (codecParts[6]) {
                case "000": // YUV444
                    ctx.pix_fmt = 5 /* YUV444P */;
                    break;
                case "100": // YUV422
                    ctx.pix_fmt = 4 /* YUV422P */;
                    break;
                case "110": // YUV420P (default)
                    ctx.pix_fmt = 0 /* YUV420P */;
                    break;
                case "111": // Monochrome
                    return false;
                default:
                    throw new TypeError("Invalid AV1 subsampling mode");
            }
        }
        /* The remaining values have to do with color formats, which we don't
         * support correctly anyway */
        return true;
    }
    /**
     * Handler for advanced options for VP9.
     * @param codecParts  .-separated parts of the codec string.
     * @param ctx  Context to populate with advanced options.
     */
    function vp9Advanced(codecParts, ctx) {
        if (codecParts[1]) {
            const profile = +codecParts[1];
            if (profile >= 0 && profile <= 3)
                ctx.profile = profile;
            else
                throw new TypeError("Invalid VP9 profile");
        }
        if (codecParts[2]) {
            const level = [+codecParts[2][0], +codecParts[2][1]];
            if (level[0] >= 1 && level[0] <= 4) {
                if (level[1] >= 0 && level[1] <= 1) ;
                else {
                    throw new TypeError("Invalid VP9 level");
                }
            }
            else if (level[0] >= 5 && level[0] <= 6) {
                if (level[1] >= 0 && level[1] <= 2) ;
                else {
                    throw new TypeError("Invalid VP9 level");
                }
            }
            else {
                throw new TypeError("Invalid VP9 level");
            }
            ctx.level = +codecParts[2];
        }
        if (codecParts[3]) {
            const depth = +codecParts[3];
            if (depth === 10 || depth === 12) {
                // Valid but unsupported
                return false;
            }
            else if (depth !== 8) {
                throw new TypeError("Invalid VP9 bit depth");
            }
        }
        if (codecParts[4]) {
            const chromaMode = +codecParts[4];
            switch (chromaMode) {
                case 0:
                case 1:
                    // FIXME: These are subtly different YUV420P modes, but we treat them the same
                    ctx.pix_fmt = 0 /* YUV420P */;
                    break;
                case 2: // YUV422
                    ctx.pix_fmt = 4 /* YUV422P */;
                    break;
                case 3: // YUV444
                    ctx.pix_fmt = 5 /* YUV444P */;
                    break;
                default:
                    throw new TypeError("Invalid VP9 chroma subsampling format");
            }
        }
        /* The remaining values have to do with color formats, which we don't
         * support correctly anyway */
        return true;
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    /**
     * Clone this configuration. Just copies over the supported/recognized fields.
     */
    function cloneConfig(config, fields) {
        const ret = {};
        for (const field of fields) {
            if (field in config)
                ret[field] = config[field];
        }
        return ret;
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$7 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    let AudioDecoder$1 = class AudioDecoder extends DequeueEventTarget {
        constructor(init) {
            super();
            // 1. Let d be a new AudioDecoder object.
            // 2. Assign a new queue to [[control message queue]].
            this._p = Promise.all([]);
            // 3. Assign false to [[message queue blocked]].
            // (unused in polyfill)
            // 4. Assign null to [[codec implementation]].
            this._libav = null;
            this._codec = this._c = this._pkt = this._frame = 0;
            // 5. Assign the result of starting a new parallel queue to [[codec work queue]].
            // (shared with control message queue)
            // 6. Assign false to [[codec saturated]].
            // (codec is never saturated)
            // 7. Assign init.output to [[output callback]].
            this._output = init.output;
            // 8. Assign init.error to [[error callback]].
            this._error = init.error;
            // 9. Assign true to [[key chunk required]].
            // (implicit part of the underlying codec)
            // 10. Assign "unconfigured" to [[state]]
            this.state = "unconfigured";
            // 11. Assign 0 to [[decodeQueueSize]].
            this.decodeQueueSize = 0;
            // 12. Assign a new list to [[pending flush promises]].
            // (shared with control message queue)
            // 13. Assign false to [[dequeue event scheduled]].
            // (shared with control message queue)
            // 14. Return d.
        }
        configure(config) {
            // 1. If config is not a valid AudioDecoderConfig, throw a TypeError.
            // NOTE: We don't support sophisticated codec string parsing (yet)
            // 2. If [[state]] is “closed”, throw an InvalidStateError DOMException.
            if (this.state === "closed")
                throw new DOMException("Decoder is closed", "InvalidStateError");
            // Free any internal state
            if (this._libav)
                this._p = this._p.then(() => this._free());
            // 3. Set [[state]] to "configured".
            this.state = "configured";
            // 4. Set [[key chunk required]] to true.
            // (implicit part of underlying codecs)
            // 5. Queue a control message to configure the decoder with config.
            this._p = this._p.then(() => __awaiter$7(this, void 0, void 0, function* () {
                /* 1. Let supported be the result of running the Check
                 * Configuration Support algorithm with config. */
                let udesc = void 0;
                if (config.description) {
                    if (ArrayBuffer.isView(config.description)) {
                        const descView = config.description;
                        udesc = new Uint8Array(descView.buffer, descView.byteOffset, descView.byteLength);
                    }
                    else {
                        const descBuf = config.description;
                        udesc = new Uint8Array(descBuf);
                    }
                }
                const supported = decoder(config.codec, config);
                /* 2. If supported is false, queue a task to run the Close
                 *    AudioDecoder algorithm with NotSupportedError and abort these
                 *    steps. */
                if (!supported) {
                    this._closeAudioDecoder(new DOMException("Unsupported codec", "NotSupportedError"));
                    return;
                }
                /* 3. If needed, assign [[codec implementation]] with an
                 *    implementation supporting config. */
                const libav = this._libav = yield get();
                const codecpara = yield libav.avcodec_parameters_alloc();
                const ps = [
                    libav.AVCodecParameters_channels_s(codecpara, config.numberOfChannels),
                    libav.AVCodecParameters_sample_rate_s(codecpara, config.sampleRate),
                    libav.AVCodecParameters_codec_type_s(codecpara, 1 /*  AVMEDIA_TYPE_AUDIO */)
                ];
                let extraDataPtr = 0;
                if (!udesc) {
                    ps.push(libav.AVCodecParameters_extradata_s(codecpara, 0));
                    ps.push(libav.AVCodecParameters_extradata_size_s(codecpara, 0));
                }
                else {
                    ps.push(libav.AVCodecParameters_extradata_size_s(codecpara, udesc.byteLength));
                    extraDataPtr = yield libav.calloc(udesc.byteLength + 64 /* AV_INPUT_BUFFER_PADDING_SIZE */, 1);
                    ps.push(libav.copyin_u8(extraDataPtr, udesc));
                    ps.push(libav.AVCodecParameters_extradata_s(codecpara, extraDataPtr));
                }
                yield Promise.all(ps);
                // 4. Configure [[codec implementation]] with config.
                [this._codec, this._c, this._pkt, this._frame] =
                    yield libav.ff_init_decoder(supported.codec, codecpara);
                const fps = [
                    libav.AVCodecContext_time_base_s(this._c, 1, 1000),
                    libav.avcodec_parameters_free_js(codecpara)
                ];
                if (extraDataPtr)
                    fps.push(libav.free(extraDataPtr));
                yield Promise.all(fps);
                // 5. queue a task to run the following steps:
                // 1. Assign false to [[message queue blocked]].
                // 2. Queue a task to Process the control message queue.
                // (shared queue)
            })).catch(this._error);
        }
        // Our own algorithm, close libav
        _free() {
            return __awaiter$7(this, void 0, void 0, function* () {
                if (this._c) {
                    yield this._libav.ff_free_decoder(this._c, this._pkt, this._frame);
                    this._codec = this._c = this._pkt = this._frame = 0;
                }
                if (this._libav) {
                    free(this._libav);
                    this._libav = null;
                }
            });
        }
        _closeAudioDecoder(exception) {
            // 1. Run the Reset AudioDecoder algorithm with exception.
            this._resetAudioDecoder(exception);
            // 2. Set [[state]] to "closed".
            this.state = "closed";
            /* 3. Clear [[codec implementation]] and release associated system
             * resources. */
            this._p = this._p.then(() => this._free());
            /* 4. If exception is not an AbortError DOMException, queue a task on
             * the control thread event loop to invoke the [[error callback]] with
             * exception. */
            if (exception.name !== "AbortError")
                this._p = this._p.then(() => { this._error(exception); });
        }
        _resetAudioDecoder(exception) {
            // 1. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Decoder closed", "InvalidStateError");
            // 2. Set [[state]] to "unconfigured".
            this.state = "unconfigured";
            // ... really, we're just going to free it now
            this._p = this._p.then(() => this._free());
        }
        decode(chunk) {
            // 1. If [[state]] is not "configured", throw an InvalidStateError.
            if (this.state !== "configured")
                throw new DOMException("Unconfigured", "InvalidStateError");
            // 2. If [[key chunk required]] is true:
            //    1. If chunk.[[type]] is not key, throw a DataError.
            /*    2. Implementers SHOULD inspect the chunk’s [[internal data]] to
             *    verify that it is truly a key chunk. If a mismatch is detected,
             *    throw a DataError. */
            //    3. Otherwise, assign false to [[key chunk required]].
            // (handled within the codec)
            // 3. Increment [[decodeQueueSize]].
            this.decodeQueueSize++;
            // 4. Queue a control message to decode the chunk.
            this._p = this._p.then(() => __awaiter$7(this, void 0, void 0, function* () {
                const libav = this._libav;
                const c = this._c;
                const pkt = this._pkt;
                const frame = this._frame;
                let decodedOutputs = null;
                // (1. and 2. relate to saturation)
                // 3. Decrement [[decodeQueueSize]] and run the Schedule Dequeue Event algorithm.
                this.decodeQueueSize--;
                this.dispatchEvent(new CustomEvent("dequeue"));
                // 1. Attempt to use [[codec implementation]] to decode the chunk.
                try {
                    // Convert to a libav packet
                    const ptsFull = Math.floor(chunk.timestamp / 1000);
                    const [pts, ptshi] = libav.f64toi64(ptsFull);
                    const packet = {
                        data: chunk._libavGetData(),
                        pts,
                        ptshi,
                        dts: pts,
                        dtshi: ptshi
                    };
                    if (chunk.duration) {
                        packet.duration = Math.floor(chunk.duration / 1000);
                        packet.durationhi = 0;
                    }
                    decodedOutputs = yield libav.ff_decode_multi(c, pkt, frame, [packet]);
                    /* 2. If decoding results in an error, queue a task to run the Close
                     *    AudioDecoder algorithm with EncodingError and return. */
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeAudioDecoder(ex);
                    });
                    return;
                }
                /* 3. If [[codec saturated]] equals true and
                 *    [[codec implementation]] is no longer saturated, queue a task
                 *    to perform the following steps: */
                // 1. Assign false to [[codec saturated]].
                // 2. Process the control message queue.
                // (no saturation)
                /* 4. Let decoded outputs be a list of decoded audio data outputs
                 *    emitted by [[codec implementation]]. */
                /* 5. If decoded outputs is not empty, queue a task to run the
                 *    Output AudioData algorithm with decoded outputs. */
                if (decodedOutputs)
                    this._outputAudioData(decodedOutputs);
            })).catch(this._error);
        }
        _outputAudioData(outputs) {
            const libav = this._libav;
            for (const frame of outputs) {
                // 1. format
                let format;
                let planar = false;
                switch (frame.format) {
                    case libav.AV_SAMPLE_FMT_U8:
                        format = "u8";
                        break;
                    case libav.AV_SAMPLE_FMT_S16:
                        format = "s16";
                        break;
                    case libav.AV_SAMPLE_FMT_S32:
                        format = "s32";
                        break;
                    case libav.AV_SAMPLE_FMT_FLT:
                        format = "f32";
                        break;
                    case libav.AV_SAMPLE_FMT_U8P:
                        format = "u8";
                        planar = true;
                        break;
                    case libav.AV_SAMPLE_FMT_S16P:
                        format = "s16";
                        planar = true;
                        break;
                    case libav.AV_SAMPLE_FMT_S32P:
                        format = "s32";
                        planar = true;
                        break;
                    case libav.AV_SAMPLE_FMT_FLTP:
                        format = "f32";
                        planar = true;
                        break;
                    default:
                        throw new DOMException("Unsupported libav format!", "EncodingError");
                }
                // 2. sampleRate
                const sampleRate = frame.sample_rate;
                // 3. numberOfFrames
                const numberOfFrames = frame.nb_samples;
                // 4. numberOfChannels
                const numberOfChannels = frame.channels;
                // 5. timestamp
                const timestamp = libav.i64tof64(frame.pts, frame.ptshi) * 1000;
                // 6. data
                let raw;
                if (planar) {
                    let ct = 0;
                    for (let i = 0; i < frame.data.length; i++)
                        ct += frame.data[i].length;
                    raw = new (frame.data[0].constructor)(ct);
                    ct = 0;
                    for (let i = 0; i < frame.data.length; i++) {
                        const part = frame.data[i];
                        raw.set(part, ct);
                        ct += part.length;
                    }
                }
                else {
                    raw = frame.data;
                }
                const data = new AudioData$1({
                    format, sampleRate, numberOfFrames, numberOfChannels,
                    timestamp, data: raw
                });
                this._output(data);
            }
        }
        flush() {
            /* 1. If [[state]] is not "configured", return a promise rejected with
             *    InvalidStateError DOMException. */
            if (this.state !== "configured")
                throw new DOMException("Invalid state", "InvalidStateError");
            // 2. Set [[key chunk required]] to true.
            // (part of the codec)
            // 3. Let promise be a new Promise.
            // 4. Append promise to [[pending flush promises]].
            // 5. Queue a control message to flush the codec with promise.
            // 6. Process the control message queue.
            // 7. Return promise.
            const ret = this._p.then(() => __awaiter$7(this, void 0, void 0, function* () {
                // 1. Signal [[codec implementation]] to emit all internal pending outputs.
                if (!this._c)
                    return;
                // Make sure any last data is flushed
                const libav = this._libav;
                const c = this._c;
                const pkt = this._pkt;
                const frame = this._frame;
                let decodedOutputs = null;
                try {
                    decodedOutputs = yield libav.ff_decode_multi(c, pkt, frame, [], true);
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeAudioDecoder(ex);
                    });
                }
                /* 2. Let decoded outputs be a list of decoded audio data outputs
                 *    emitted by [[codec implementation]]. */
                // 3. Queue a task to perform these steps:
                {
                    /* 1. If decoded outputs is not empty, run the Output AudioData
                     *    algorithm with decoded outputs. */
                    if (decodedOutputs)
                        this._outputAudioData(decodedOutputs);
                    // 2. Remove promise from [[pending flush promises]].
                    // 3. Resolve promise.
                }
            }));
            this._p = ret;
            return ret;
        }
        reset() {
            this._resetAudioDecoder(new DOMException("Reset", "AbortError"));
        }
        close() {
            this._closeAudioDecoder(new DOMException("Close", "AbortError"));
        }
        static isConfigSupported(config) {
            return __awaiter$7(this, void 0, void 0, function* () {
                const dec = decoder(config.codec, config);
                let supported = false;
                if (dec) {
                    const libav = yield get();
                    try {
                        const [, c, pkt, frame] = yield libav.ff_init_decoder(dec.codec);
                        yield libav.ff_free_decoder(c, pkt, frame);
                        supported = true;
                    }
                    catch (ex) { }
                    yield free(libav);
                }
                return {
                    supported,
                    config: cloneConfig(config, ["codec", "sampleRate", "numberOfChannels"])
                };
            });
        }
    };

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$6 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    let AudioEncoder$1 = class AudioEncoder extends DequeueEventTarget {
        constructor(init) {
            super();
            // Metadata argument for output
            this._outputMetadata = null;
            this._outputMetadataFilled = false;
            this._pts = null;
            // 1. Let e be a new AudioEncoder object.
            // 2. Assign a new queue to [[control message queue]].
            this._p = Promise.all([]);
            // 3. Assign false to [[message queue blocked]].
            // (unused in polyfill)
            // 4. Assign null to [[codec implementation]].
            this._libav = null;
            this._codec = this._c = this._frame = this._pkt = 0;
            this._filter_in_ctx = this._filter_out_ctx = null;
            this._filter_graph = this._buffersrc_ctx = this._buffersink_ctx = 0;
            /* 5. Assign the result of starting a new parallel queue to
             *    [[codec work queue]]. */
            // (shared queue)
            // 6. Assign false to [[codec saturated]].
            // (saturation unneeded in the polyfill)
            // 7. Assign init.output to [[output callback]].
            this._output = init.output;
            // 8. Assign init.error to [[error callback]].
            this._error = init.error;
            // 9. Assign null to [[active encoder config]].
            // 10. Assign null to [[active output config]].
            // (both part of the codec)
            // 11. Assign "unconfigured" to [[state]]
            this.state = "unconfigured";
            // 12. Assign 0 to [[encodeQueueSize]].
            this.encodeQueueSize = 0;
            // 13. Assign a new list to [[pending flush promises]].
            // 14. Assign false to [[dequeue event scheduled]].
            // (shared queue)
            // 15. Return e.
        }
        configure(config) {
            const self = this;
            // 1. If config is not a valid AudioEncoderConfig, throw a TypeError.
            // NOTE: We don't support sophisticated codec string parsing (yet)
            // 2. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Encoder is closed", "InvalidStateError");
            // Free any internal state
            if (this._libav)
                this._p = this._p.then(() => this._free());
            // 3. Set [[state]] to "configured".
            this.state = "configured";
            // 4. Queue a control message to configure the encoder using config.
            this._p = this._p.then(function () {
                return __awaiter$6(this, void 0, void 0, function* () {
                    /* 1. Let supported be the result of running the Check
                     * Configuration Support algorithm with config. */
                    const supported = encoder(config.codec, config);
                    // Get the output metadata now
                    self._outputMetadata = { decoderConfig: {
                            codec: config.codec,
                            // Rest will be filled in when we get data
                            sampleRate: 0,
                            numberOfChannels: 0
                        } };
                    self._outputMetadataFilled = false;
                    /* 2. If supported is false, queue a task to run the Close
                     *    AudioEncoder algorithm with NotSupportedError and abort these
                     *    steps. */
                    if (!supported) {
                        self._closeAudioEncoder(new DOMException("Unsupported codec", "NotSupportedError"));
                        return;
                    }
                    /* 3. If needed, assign [[codec implementation]] with an
                     *    implementation supporting config. */
                    // 4. Configure [[codec implementation]] with config.
                    const libav = self._libav = yield get();
                    // And initialize
                    let frame_size;
                    [self._codec, self._c, self._frame, self._pkt, frame_size] =
                        yield libav.ff_init_encoder(supported.codec, supported);
                    self._pts = null;
                    yield libav.AVCodecContext_time_base_s(self._c, 1, supported.ctx.sample_rate);
                    // Be ready to set up the filter
                    self._filter_out_ctx = {
                        sample_rate: supported.ctx.sample_rate,
                        sample_fmt: supported.ctx.sample_fmt,
                        channel_layout: supported.ctx.channel_layout,
                        frame_size
                    };
                    // 5. queue a task to run the following steps:
                    // 1. Assign false to [[message queue blocked]].
                    // 2. Queue a task to Process the control message queue.
                    // (shared queue)
                });
            }).catch(this._error);
        }
        // Our own algorithm, close libav
        _free() {
            return __awaiter$6(this, void 0, void 0, function* () {
                if (this._filter_graph) {
                    yield this._libav.avfilter_graph_free_js(this._filter_graph);
                    this._filter_in_ctx = this._filter_out_ctx = null;
                    this._filter_graph = this._buffersrc_ctx = this._buffersink_ctx =
                        0;
                }
                if (this._c) {
                    yield this._libav.ff_free_encoder(this._c, this._frame, this._pkt);
                    this._codec = this._c = this._frame = this._pkt = 0;
                }
                if (this._libav) {
                    free(this._libav);
                    this._libav = null;
                }
            });
        }
        _closeAudioEncoder(exception) {
            // 1. Run the Reset AudioEncoder algorithm with exception.
            this._resetAudioEncoder(exception);
            // 2. Set [[state]] to "closed".
            this.state = "closed";
            /* 3. Clear [[codec implementation]] and release associated system
             * resources. */
            this._p = this._p.then(() => this._free());
            /* 4. If exception is not an AbortError DOMException, invoke the
             *    [[error callback]] with exception. */
            if (exception.name !== "AbortError")
                this._p = this._p.then(() => { this._error(exception); });
        }
        _resetAudioEncoder(exception) {
            // 1. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Encoder closed", "InvalidStateError");
            // 2. Set [[state]] to "unconfigured".
            this.state = "unconfigured";
            // ... really, we're just going to free it now
            this._p = this._p.then(() => this._free());
        }
        encode(data) {
            /* 1. If the value of data’s [[Detached]] internal slot is true, throw
             * a TypeError. */
            if (data._libavGetData() === null)
                throw new TypeError("Detached");
            // 2. If [[state]] is not "configured", throw an InvalidStateError.
            if (this.state !== "configured")
                throw new DOMException("Unconfigured", "InvalidStateError");
            /* 3. Let dataClone hold the result of running the Clone AudioData
             *    algorithm with data. */
            const dataClone = data.clone();
            // 4. Increment [[encodeQueueSize]].
            this.encodeQueueSize++;
            // 5. Queue a control message to encode dataClone.
            this._p = this._p.then(() => __awaiter$6(this, void 0, void 0, function* () {
                const libav = this._libav;
                const c = this._c;
                const pkt = this._pkt;
                const framePtr = this._frame;
                let encodedOutputs = null;
                /* 3. Decrement [[encodeQueueSize]] and run the Schedule Dequeue
                 *    Event algorithm. */
                this.encodeQueueSize--;
                this.dispatchEvent(new CustomEvent("dequeue"));
                /* 1. Attempt to use [[codec implementation]] to encode the media
                 * resource described by dataClone. */
                try {
                    // Arrange the data
                    let raw = dataClone._libavGetData();
                    const nb_samples = dataClone.numberOfFrames;
                    if (!isInterleaved(dataClone.format)) {
                        let split = [];
                        for (let i = 0; i < dataClone.numberOfChannels; i++)
                            split.push(raw.subarray(i * nb_samples, (i + 1) * nb_samples));
                        raw = split;
                    }
                    // Convert the format
                    let format;
                    switch (dataClone.format) {
                        case "u8":
                            format = libav.AV_SAMPLE_FMT_U8;
                            break;
                        case "s16":
                            format = libav.AV_SAMPLE_FMT_S16;
                            break;
                        case "s32":
                            format = libav.AV_SAMPLE_FMT_S32;
                            break;
                        case "f32":
                            format = libav.AV_SAMPLE_FMT_FLT;
                            break;
                        case "u8-planar":
                            format = libav.AV_SAMPLE_FMT_U8P;
                            break;
                        case "s16-planar":
                            format = libav.AV_SAMPLE_FMT_S16P;
                            break;
                        case "s32-planar":
                            format = libav.AV_SAMPLE_FMT_S32P;
                            break;
                        case "f32-planar":
                            format = libav.AV_SAMPLE_FMT_FLTP;
                            break;
                        default:
                            throw new TypeError("Invalid AudioSampleFormat");
                    }
                    // Convert the timestamp
                    const ptsFull = Math.floor(dataClone.timestamp / 1000);
                    const [pts, ptshi] = libav.f64toi64(ptsFull);
                    // Convert the channel layout
                    const cc = dataClone.numberOfChannels;
                    const channel_layout = (cc === 1) ? 4 : ((1 << cc) - 1);
                    // Make the frame
                    const sample_rate = dataClone.sampleRate;
                    const frame = {
                        data: raw,
                        format, pts, ptshi, channel_layout, sample_rate
                    };
                    // Check if the filter needs to be reconfigured
                    let preOutputs = null;
                    if (this._filter_in_ctx) {
                        const filter_ctx = this._filter_in_ctx;
                        if (filter_ctx.sample_fmt !== frame.format ||
                            filter_ctx.channel_layout !== frame.channel_layout ||
                            filter_ctx.sample_rate !== frame.sample_rate) {
                            // Need a new filter! First, get anything left in the filter
                            let fframes = yield this._filter([], true);
                            // Can't send partial frames through the encoder
                            fframes = fframes.filter(x => {
                                let frame_size;
                                if (x.data[0].length) {
                                    // Planar
                                    frame_size = x.data[0].length;
                                }
                                else {
                                    frame_size = x.data.length / x.channels;
                                }
                                return frame_size === this._filter_out_ctx.frame_size;
                            });
                            if (fframes.length) {
                                preOutputs =
                                    yield libav.ff_encode_multi(c, framePtr, pkt, fframes);
                            }
                            yield libav.avfilter_graph_free_js(this._filter_graph);
                            this._filter_in_ctx = null;
                            this._filter_graph = this._buffersrc_ctx =
                                this._buffersink_ctx = 0;
                        }
                    }
                    // Set up the filter
                    if (!this._filter_graph) {
                        const filter_ctx = this._filter_in_ctx = {
                            sample_rate: frame.sample_rate,
                            sample_fmt: frame.format,
                            channel_layout: frame.channel_layout
                        };
                        [this._filter_graph, this._buffersrc_ctx, this._buffersink_ctx] =
                            yield libav.ff_init_filter_graph("aresample", filter_ctx, this._filter_out_ctx);
                    }
                    // Filter
                    const fframes = yield this._filter([frame]);
                    // And encode
                    encodedOutputs =
                        yield libav.ff_encode_multi(c, framePtr, pkt, fframes);
                    if (preOutputs)
                        encodedOutputs = preOutputs.concat(encodedOutputs);
                    if (encodedOutputs.length && !this._outputMetadataFilled &&
                        fframes && fframes.length)
                        yield this._getOutputMetadata(fframes[0]);
                    /* 2. If encoding results in an error, queue a task on the control
                     * thread event loop to run the Close AudioEncoder algorithm with
                     * EncodingError. */
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeAudioEncoder(ex);
                    });
                }
                /* 3. If [[codec saturated]] equals true and
                 *    [[codec implementation]] is no longer saturated, queue a task
                 *    to perform the following steps: */
                // 1. Assign false to [[codec saturated]].
                // 2. Process the control message queue.
                // (no saturation)
                /* 4. Let encoded outputs be a list of encoded audio data outputs
                 *    emitted by [[codec implementation]]. */
                /* 5. If encoded outputs is not empty, queue a task to run the
                 *    Output EncodedAudioChunks algorithm with encoded outputs. */
                if (encodedOutputs)
                    this._outputEncodedAudioChunks(encodedOutputs);
            })).catch(this._error);
        }
        // Internal: Filter the given audio
        _filter(frames, fin = false) {
            return __awaiter$6(this, void 0, void 0, function* () {
                /* The specification does not state how timestamps should be related
                 * between input and output. It's obvious that the timestamps should
                 * increase at the appropriate rate based on the number of samples seen,
                 * but where they should start is not stated. Google Chrome starts with
                 * the timestamp of the first input frame, and ignores all other input
                 * frame timestamps. We follow that convention as well. */
                if (frames.length && this._pts === null)
                    this._pts = (frames[0].pts || 0);
                const fframes = yield this._libav.ff_filter_multi(this._buffersrc_ctx, this._buffersink_ctx, this._frame, frames, fin);
                for (const frame of fframes) {
                    frame.pts = this._pts;
                    frame.ptshi = 0;
                    this._pts += frame.nb_samples;
                }
                return fframes;
            });
        }
        // Internal: Get output metadata
        _getOutputMetadata(frame) {
            return __awaiter$6(this, void 0, void 0, function* () {
                const libav = this._libav;
                const c = this._c;
                const extradataPtr = yield libav.AVCodecContext_extradata(c);
                const extradata_size = yield libav.AVCodecContext_extradata_size(c);
                let extradata = null;
                if (extradataPtr && extradata_size)
                    extradata = yield libav.copyout_u8(extradataPtr, extradata_size);
                this._outputMetadata.decoderConfig.sampleRate = frame.sample_rate;
                this._outputMetadata.decoderConfig.numberOfChannels = frame.channels;
                if (extradata)
                    this._outputMetadata.decoderConfig.description = extradata;
                this._outputMetadataFilled = true;
            });
        }
        _outputEncodedAudioChunks(packets) {
            const libav = this._libav;
            const sampleRate = this._filter_out_ctx.sample_rate;
            for (const packet of packets) {
                // 1. data
                const data = packet.data;
                // 2. type
                const type = (packet.flags & 1) ? "key" : "delta";
                // 3. timestamp
                let timestamp = libav.i64tof64(packet.pts, packet.ptshi);
                timestamp = Math.floor(timestamp / sampleRate * 1000000);
                // 4. duration
                let duration;
                if (typeof packet.duration !== "undefined") {
                    duration = libav.i64tof64(packet.duration, packet.durationhi || 0);
                    duration = Math.floor(duration / sampleRate * 1000000);
                }
                const chunk = new EncodedAudioChunk$1({
                    data, type, timestamp, duration
                });
                if (this._outputMetadataFilled)
                    this._output(chunk, this._outputMetadata || void 0);
                else
                    this._output(chunk);
            }
        }
        flush() {
            /* 1. If [[state]] is not "configured", return a promise rejected with
             *    InvalidStateError DOMException. */
            if (this.state !== "configured")
                throw new DOMException("Invalid state", "InvalidStateError");
            // 2. Let promise be a new Promise.
            // 3. Append promise to [[pending flush promises]].
            // 4. Queue a control message to flush the codec with promise.
            // 5. Process the control message queue.
            // 6. Return promise.
            const ret = this._p.then(() => __awaiter$6(this, void 0, void 0, function* () {
                if (!this._c)
                    return;
                /* 1. Signal [[codec implementation]] to emit all internal pending
                 *    outputs. */
                // Make sure any last data is flushed
                const libav = this._libav;
                const c = this._c;
                const frame = this._frame;
                const pkt = this._pkt;
                const buffersrc_ctx = this._buffersrc_ctx;
                this._buffersink_ctx;
                let encodedOutputs = null;
                try {
                    let fframes = null;
                    if (buffersrc_ctx)
                        fframes = yield this._filter([], true);
                    encodedOutputs =
                        yield libav.ff_encode_multi(c, frame, pkt, fframes || [], true);
                    if (!this._outputMetadataFilled && fframes && fframes.length)
                        yield this._getOutputMetadata(fframes[0]);
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeAudioEncoder(ex);
                    });
                }
                /* 2. Let encoded outputs be a list of encoded audio data outputs
                 *    emitted by [[codec implementation]]. */
                // 3. Queue a task to perform these steps:
                {
                    /* 1. If encoded outputs is not empty, run the Output
                     *    EncodedAudioChunks algorithm with encoded outputs. */
                    if (encodedOutputs)
                        this._outputEncodedAudioChunks(encodedOutputs);
                    // 2. Remove promise from [[pending flush promises]].
                    // 3. Resolve promise.
                    // (shared queue)
                }
            }));
            this._p = ret;
            return ret;
        }
        reset() {
            this._resetAudioEncoder(new DOMException("Reset", "AbortError"));
        }
        close() {
            this._closeAudioEncoder(new DOMException("Close", "AbortError"));
        }
        static isConfigSupported(config) {
            return __awaiter$6(this, void 0, void 0, function* () {
                const enc = encoder(config.codec, config);
                let supported = false;
                if (enc) {
                    const libav = yield get();
                    try {
                        const [, c, frame, pkt] = yield libav.ff_init_encoder(enc.codec, enc);
                        yield libav.ff_free_encoder(c, frame, pkt);
                        supported = true;
                    }
                    catch (ex) { }
                    yield free(libav);
                }
                return {
                    supported,
                    config: cloneConfig(config, ["codec", "sampleRate", "numberOfChannels", "bitrate"])
                };
            });
        }
    };

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    const EncodedVideoChunk$1 = EncodedAudioChunk$1;

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$5 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    // A canvas element used to convert CanvasImageSources to buffers
    let offscreenCanvas = null;
    let VideoFrame$1 = class VideoFrame {
        constructor(data, init) {
            /* NOTE: These should all be readonly, but the constructor style above
             * doesn't work with that */
            this.format = "I420";
            this.codedWidth = 0;
            this.codedHeight = 0;
            this.codedRect = null;
            this.visibleRect = null;
            this.displayWidth = 0;
            this.displayHeight = 0;
            this.timestamp = 0; // microseconds
            this._layout = null;
            this._data = null;
            /**
             * (Internal) Does this use non-square pixels?
             */
            this._nonSquarePixels = false;
            /**
             * (Internal) If non-square pixels, the SAR (sample/pixel aspect ratio)
             */
            this._sar_num = 1;
            this._sar_den = 1;
            if (data instanceof ArrayBuffer ||
                data.buffer instanceof ArrayBuffer) {
                this._constructBuffer(data, init);
            }
            else if (data instanceof VideoFrame ||
                (globalThis.VideoFrame && data instanceof globalThis.VideoFrame)) {
                const array = new Uint8Array(data.allocationSize());
                data.copyTo(array);
                this._constructBuffer(array, {
                    transfer: [array.buffer],
                    // 1. Let format be otherFrame.format.
                    /* 2. FIXME: If init.alpha is discard, assign
                     * otherFrame.format's equivalent opaque format format. */
                    format: data.format,
                    /* 3. Let validInit be the result of running the Validate
                     * VideoFrameInit algorithm with format and otherFrame’s
                     * [[coded width]] and [[coded height]]. */
                    // 4. If validInit is false, throw a TypeError.
                    /* 7. Assign the following attributes from otherFrame to frame:
                     * codedWidth, codedHeight, colorSpace. */
                    codedHeight: data.codedHeight,
                    codedWidth: data.codedWidth,
                    colorSpace: data.colorSpace,
                    /* 8. Let defaultVisibleRect be the result of performing the
                     * getter steps for visibleRect on otherFrame. */
                    /* 9. Let defaultDisplayWidth, and defaultDisplayHeight be
                     * otherFrame’s [[display width]], and [[display height]]
                     * respectively. */
                    /* 10. Run the Initialize Visible Rect and Display Size
                     * algorithm with init, frame, defaultVisibleRect,
                     * defaultDisplayWidth, and defaultDisplayHeight. */
                    visibleRect: (init === null || init === void 0 ? void 0 : init.visibleRect) || data.visibleRect,
                    displayHeight: (init === null || init === void 0 ? void 0 : init.displayHeight) || data.displayHeight,
                    displayWidth: (init === null || init === void 0 ? void 0 : init.displayWidth) || data.displayWidth,
                    /* 11. If duration exists in init, assign it to frame’s
                     * [[duration]]. Otherwise, assign otherFrame.duration to
                     * frame’s [[duration]]. */
                    duration: (init === null || init === void 0 ? void 0 : init.duration) || data.duration,
                    /* 12. If timestamp exists in init, assign it to frame’s
                     * [[timestamp]]. Otherwise, assign otherFrame’s timestamp to
                     * frame’s [[timestamp]]. */
                    timestamp: (init === null || init === void 0 ? void 0 : init.timestamp) || data.timestamp,
                    /* Assign the result of calling Copy VideoFrame metadata with
                     * init’s metadata to frame.[[metadata]]. */
                    metadata: JSON.parse(JSON.stringify(init === null || init === void 0 ? void 0 : init.metadata))
                });
            }
            else if (data instanceof HTMLVideoElement) {
                /* Check the usability of the image argument. If this throws an
                 * exception or returns bad, then throw an InvalidStateError
                 * DOMException. */
                if (data.readyState === HTMLVideoElement.prototype.HAVE_NOTHING
                    || data.readyState === HTMLVideoElement.prototype.HAVE_METADATA) {
                    throw new DOMException("Video is not ready for reading frames", "InvalidStateError");
                }
                // If image’s networkState attribute is NETWORK_EMPTY, then throw an InvalidStateError DOMException.
                if (data.networkState === data.NETWORK_EMPTY) {
                    throw new DOMException("Video network state is empty", "InvalidStateError");
                }
                this._constructCanvas(data, Object.assign(Object.assign({}, init), { timestamp: (init === null || init === void 0 ? void 0 : init.timestamp) || data.currentTime * 1e6 }));
            }
            else {
                this._constructCanvas(data, init);
            }
        }
        _constructCanvas(image, init) {
            /* The spec essentially re-specifies “draw it”, and has specific
             * instructions for each sort of thing it might be. So, we don't
             * document all the steps here, we just... draw it. */
            // Get the width and height
            let width = 0, height = 0;
            if (image.naturalWidth) {
                width = image.naturalWidth;
                height = image.naturalHeight;
            }
            else if (image.videoWidth) {
                width = image.videoWidth;
                height = image.videoHeight;
            }
            else if (image.width) {
                width = image.width;
                height = image.height;
            }
            if (!width || !height)
                throw new DOMException("Could not determine dimensions", "InvalidStateError");
            if (offscreenCanvas === null) {
                if (typeof OffscreenCanvas !== "undefined") {
                    offscreenCanvas = new OffscreenCanvas(width, height);
                }
                else {
                    offscreenCanvas = document.createElement("canvas");
                    offscreenCanvas.style.display = "none";
                    document.body.appendChild(offscreenCanvas);
                }
            }
            offscreenCanvas.width = width;
            offscreenCanvas.height = height;
            const options = { desynchronized: true, willReadFrequently: true };
            const ctx = offscreenCanvas.getContext("2d", options);
            ctx.clearRect(0, 0, width, height);
            ctx.drawImage(image, 0, 0);
            this._constructBuffer(ctx.getImageData(0, 0, width, height).data, {
                format: "RGBA",
                codedWidth: width,
                codedHeight: height,
                timestamp: (init === null || init === void 0 ? void 0 : init.timestamp) || 0,
                duration: (init === null || init === void 0 ? void 0 : init.duration) || 0,
                layout: [{ offset: 0, stride: width * 4 }],
                displayWidth: (init === null || init === void 0 ? void 0 : init.displayWidth) || width,
                displayHeight: (init === null || init === void 0 ? void 0 : init.displayHeight) || height
            });
        }
        _constructBuffer(data, init) {
            // 1. If init is not a valid VideoFrameBufferInit, throw a TypeError.
            VideoFrame._checkValidVideoFrameBufferInit(init);
            /* 2. Let defaultRect be «[ "x:" → 0, "y" → 0, "width" →
             *    init.codedWidth, "height" → init.codedWidth ]». */
            const defaultRect = new DOMRect(0, 0, init.codedWidth, init.codedHeight);
            // 3. Let overrideRect be undefined.
            let overrideRect = void 0;
            // 4. If init.visibleRect exists, assign its value to overrideRect.
            if (init.visibleRect)
                overrideRect = DOMRect.fromRect(init.visibleRect);
            /* 5. Let parsedRect be the result of running the Parse Visible Rect
             *    algorithm with defaultRect, overrideRect, init.codedWidth,
             *    init.codedHeight, and init.format. */
            // 6. If parsedRect is an exception, return parsedRect.
            this.codedWidth = init.codedWidth; // (for _parseVisibleRect)
            this.codedHeight = init.codedHeight;
            const parsedRect = this._parseVisibleRect(defaultRect, overrideRect || null);
            // 7. Let optLayout be undefined.
            let optLayout = void 0;
            // 8. If init.layout exists, assign its value to optLayout.
            if (init.layout) {
                if (init.layout instanceof Array)
                    optLayout = init.layout;
                else
                    optLayout = Array.from(init.layout);
            }
            /* 9. Let combinedLayout be the result of running the Compute Layout
             *    and Allocation Size algorithm with parsedRect, init.format, and
             *    optLayout. */
            // 10. If combinedLayout is an exception, throw combinedLayout.
            this.format = init.format; // (needed for _computeLayoutAndAllocationSize)
            const combinedLayout = this._computeLayoutAndAllocationSize(parsedRect, optLayout || null);
            /* 11. If data.byteLength is less than combinedLayout’s allocationSize,
             *     throw a TypeError. */
            if (data.byteLength < combinedLayout.allocationSize)
                throw new TypeError("data is too small for layout");
            /* 12. If init.transfer contains more than one reference to the same
             *     ArrayBuffer, then throw a DataCloneError DOMException. */
            // 13. For each transferable in init.transfer:
            // 1. If [[Detached]] internal slot is true, then throw a DataCloneError DOMException.
            // (not checked in polyfill)
            /* 14. If init.transfer contains an ArrayBuffer referenced by data the
             *     User Agent MAY choose to: */
            let transfer = false;
            if (init.transfer) {
                /* 1. Let resource be a new media resource referencing pixel data
                 *    in data. */
                let inBuffer;
                if (data.buffer)
                    inBuffer = data.buffer;
                else
                    inBuffer = data;
                let t;
                if (init.transfer instanceof Array)
                    t = init.transfer;
                else
                    t = Array.from(init.transfer);
                for (const b of t) {
                    if (b === inBuffer) {
                        transfer = true;
                        break;
                    }
                }
            }
            // 15. Otherwise:
            /* 1. Let resource be a new media resource containing a copy of
             *    data. Use visibleRect and layout to determine where in data
             *    the pixels for each plane reside. */
            /*    The User Agent MAY choose to allocate resource with a larger
             *    coded size and plane strides to improve memory alignment.
             *    Increases will be reflected by codedWidth and codedHeight.
             *    Additionally, the User Agent MAY use visibleRect to copy only
             *    the visible rectangle. It MAY also reposition the visible
             *    rectangle within resource. The final position will be
             *    reflected by visibleRect. */
            /* NOTE: The spec seems to be missing the step where you actually use
             * the resource to define the [[resource reference]]. */
            const format = init.format;
            if (init.layout) {
                // FIXME: Make sure it's the right size
                if (init.layout instanceof Array)
                    this._layout = init.layout;
                else
                    this._layout = Array.from(init.layout);
            }
            else {
                const numPlanes_ = numPlanes(format);
                const layout = [];
                let offset = 0;
                for (let i = 0; i < numPlanes_; i++) {
                    const sampleWidth = horizontalSubSamplingFactor(format, i);
                    const sampleHeight = verticalSubSamplingFactor(format, i);
                    const stride = ~~(this.codedWidth / sampleWidth);
                    layout.push({ offset, stride });
                    offset += stride * (~~(this.codedHeight / sampleHeight));
                }
                this._layout = layout;
            }
            this._data = new Uint8Array(data.buffer || data, data.byteOffset || 0);
            if (!transfer) {
                const numPlanes_ = numPlanes(format);
                // Only copy the relevant part
                let layout = this._layout;
                let lo = 1 / 0;
                let hi = 0;
                for (let i = 0; i < numPlanes_; i++) {
                    const plane = layout[i];
                    let offset = plane.offset;
                    if (offset < lo)
                        lo = offset;
                    const sampleHeight = verticalSubSamplingFactor(format, i);
                    offset += plane.stride * (~~(this.codedHeight / sampleHeight));
                    if (offset > hi)
                        hi = offset;
                }
                // Fix the layout to compensate
                if (lo !== 0) {
                    layout = this._layout = layout.map(x => ({
                        offset: x.offset - lo,
                        stride: x.stride
                    }));
                }
                this._data = this._data.slice(lo, hi);
            }
            // 16. For each transferable in init.transfer:
            // 1. Perform DetachArrayBuffer on transferable
            // (not doable in polyfill)
            // 17. Let resourceCodedWidth be the coded width of resource.
            const resourceCodedWidth = init.codedWidth;
            // 18. Let resourceCodedHeight be the coded height of resource.
            const resourceCodedHeight = init.codedHeight;
            /* 19. Let resourceVisibleLeft be the left offset for the visible
             *     rectangle of resource. */
            parsedRect.left;
            /* 20. Let resourceVisibleTop be the top offset for the visible
             *     rectangle of resource. */
            parsedRect.top;
            // 21. Let frame be a new VideoFrame object initialized as follows:
            {
                /* 1. Assign resourceCodedWidth, resourceCodedHeight,
                 *    resourceVisibleLeft, and resourceVisibleTop to
                 *    [[coded width]], [[coded height]], [[visible left]], and
                 *    [[visible top]] respectively. */
                // (codedWidth/codedHeight done earlier)
                this.codedRect = new DOMRect(0, 0, resourceCodedWidth, resourceCodedHeight);
                this.visibleRect = parsedRect;
                // 2. If init.visibleRect exists:
                if (init.visibleRect) {
                    // 1. Let truncatedVisibleWidth be the value of visibleRect.width after truncating.
                    // 2. Assign truncatedVisibleWidth to [[visible width]].
                    // 3. Let truncatedVisibleHeight be the value of visibleRect.height after truncating.
                    // 4. Assign truncatedVisibleHeight to [[visible height]].
                    this.visibleRect = DOMRect.fromRect(init.visibleRect);
                    // 3. Otherwise:
                }
                else {
                    // 1. Assign [[coded width]] to [[visible width]].
                    // 2. Assign [[coded height]] to [[visible height]].
                    this.visibleRect = new DOMRect(0, 0, resourceCodedWidth, resourceCodedHeight);
                }
                /* 4. If init.displayWidth exists, assign it to [[display width]].
                 *    Otherwise, assign [[visible width]] to [[display width]]. */
                if (typeof init.displayWidth === "number")
                    this.displayWidth = init.displayWidth;
                else
                    this.displayWidth = this.visibleRect.width;
                /* 5. If init.displayHeight exists, assign it to [[display height]].
                 *    Otherwise, assign [[visible height]] to [[display height]]. */
                if (typeof init.displayHeight === "number")
                    this.displayHeight = init.displayHeight;
                else
                    this.displayHeight = this.visibleRect.height;
                // Account for non-square pixels
                if (this.displayWidth !== this.visibleRect.width ||
                    this.displayHeight !== this.visibleRect.height) {
                    // Dubious (but correct) SAR calculation
                    this._nonSquarePixels = true;
                    this._sar_num = this.displayWidth * this.visibleRect.width;
                    this._sar_den = this.displayHeight * this.visibleRect.height;
                }
                else {
                    this._nonSquarePixels = false;
                    this._sar_num = this._sar_den = 1;
                }
                /* 6. Assign init’s timestamp and duration to [[timestamp]] and
                 *    [[duration]] respectively. */
                this.timestamp = init.timestamp;
                this.duration = init.duration;
                // 7. Let colorSpace be undefined.
                // 8. If init.colorSpace exists, assign its value to colorSpace.
                // (color spaces not supported)
                // 9. Assign init’s format to [[format]].
                // (done earlier)
                /* 10. Assign the result of running the Pick Color Space algorithm,
                 *     with colorSpace and [[format]], to [[color space]]. */
                // (color spaces not supported)
                /* 11. Assign the result of calling Copy VideoFrame metadata with
                 *     init’s metadata to frame.[[metadata]]. */
                // (no actual metadata is yet described by the spec)
            }
            // 22. Return frame.
        }
        /**
         * Convert a polyfill VideoFrame to a native VideoFrame.
         * @param opts  Conversion options
         */
        toNative(opts = {}) {
            const ret = new globalThis.VideoFrame(this._data, {
                layout: this._layout,
                format: this.format,
                codedWidth: this.codedWidth,
                codedHeight: this.codedHeight,
                visibleRect: this.visibleRect,
                displayWidth: this.displayWidth,
                displayHeight: this.displayHeight,
                duration: this.duration,
                timestamp: this.timestamp,
                transfer: opts.transfer ? [this._data.buffer] : []
            });
            if (opts.transfer)
                this.close();
            return ret;
        }
        /**
         * Convert a native VideoFrame to a polyfill VideoFrame. WARNING: Inefficient,
         * as the data cannot be transferred out.
         * @param from  VideoFrame to copy in
         */
        static fromNative(from /* native VideoFrame */) {
            const vf = from;
            const data = new Uint8Array(vf.allocationSize());
            vf.copyTo(data);
            return new VideoFrame(data, {
                format: vf.format,
                codedWidth: vf.codedWidth,
                codedHeight: vf.codedHeight,
                visibleRect: vf.visibleRect,
                displayWidth: vf.displayWidth,
                displayHeight: vf.displayHeight,
                duration: vf.duration,
                timestamp: vf.timestamp
            });
        }
        // Internal
        _libavGetData() { return this._data; }
        _libavGetLayout() { return this._layout; }
        static _checkValidVideoFrameBufferInit(init) {
            // 1. If codedWidth = 0 or codedHeight = 0,return false.
            if (!init.codedWidth || !init.codedHeight)
                throw new TypeError("Invalid coded dimensions");
            if (init.visibleRect) {
                /* 2. If any attribute of visibleRect is negative or not finite, return
                 *    false. */
                const vr = DOMRect.fromRect(init.visibleRect);
                if (vr.x < 0 || !Number.isFinite(vr.x) ||
                    vr.y < 0 || !Number.isFinite(vr.y) ||
                    vr.width < 0 || !Number.isFinite(vr.width) ||
                    vr.height < 0 || !Number.isFinite(vr.height)) {
                    throw new TypeError("Invalid visible rectangle");
                }
                // 3. If visibleRect.y + visibleRect.height > codedHeight, return false.
                if (vr.y + vr.height > init.codedHeight)
                    throw new TypeError("Visible rectangle outside of coded height");
                // 4. If visibleRect.x + visibleRect.width > codedWidth, return false.
                if (vr.x + vr.width > init.codedWidth)
                    throw new TypeError("Visible rectangle outside of coded width");
                // 5. If only one of displayWidth or displayHeight exists, return false.
                // 6. If displayWidth = 0 or displayHeight = 0, return false.
                if ((init.displayWidth && !init.displayHeight) ||
                    (!init.displayWidth && !init.displayHeight) ||
                    (init.displayWidth === 0 || init.displayHeight === 0))
                    throw new TypeError("Invalid display dimensions");
            }
            // 7. Return true.
        }
        metadata() {
            // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
            if (this._data === null)
                throw new DOMException("Detached", "InvalidStateError");
            /* 2. Return the result of calling Copy VideoFrame metadata with
             *    [[metadata]]. */
            // No actual metadata is yet defined in the spec
            return null;
        }
        allocationSize(options = {}) {
            // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
            if (this._data === null)
                throw new DOMException("Detached", "InvalidStateError");
            // 2. If [[format]] is null, throw a NotSupportedError DOMException.
            if (this.format === null)
                throw new DOMException("Not supported", "NotSupportedError");
            /* 3. Let combinedLayout be the result of running the Parse
             * VideoFrameCopyToOptions algorithm with options. */
            // 4. If combinedLayout is an exception, throw combinedLayout.
            const combinedLayout = this._parseVideoFrameCopyToOptions(options);
            // 5. Return combinedLayout’s allocationSize.
            return combinedLayout.allocationSize;
        }
        _parseVideoFrameCopyToOptions(options) {
            /* 1. Let defaultRect be the result of performing the getter steps for
             * visibleRect. */
            const defaultRect = this.visibleRect;
            // 2. Let overrideRect be undefined.
            // 3. If options.rect exists, assign its value to overrideRect.
            let overrideRect = options.rect ?
                new DOMRect(options.rect.x, options.rect.y, options.rect.width, options.rect.height)
                : null;
            /* 4. Let parsedRect be the result of running the Parse Visible Rect
             * algorithm with defaultRect, overrideRect, [[coded width]], [[coded
             * height]], and [[format]]. */
            // 5. If parsedRect is an exception, return parsedRect.
            const parsedRect = this._parseVisibleRect(defaultRect, overrideRect);
            // 6. Let optLayout be undefined.
            // 7. If options.layout exists, assign its value to optLayout.
            let optLayout = null;
            if (options.layout) {
                if (options.layout instanceof Array)
                    optLayout = options.layout;
                else
                    optLayout = Array.from(options.layout);
            }
            /* 8. Let combinedLayout be the result of running the Compute Layout
             * and Allocation Size algorithm with parsedRect, [[format]], and
             * optLayout. */
            const combinedLayout = this._computeLayoutAndAllocationSize(parsedRect, optLayout);
            // 9. Return combinedLayout.
            return combinedLayout;
        }
        _parseVisibleRect(defaultRect, overrideRect) {
            // 1. Let sourceRect be defaultRect
            let sourceRect = defaultRect;
            // 2. If overrideRect is not undefined:
            if (overrideRect) {
                /* 1. If either of overrideRect.width or height is 0, return a
                 * TypeError. */
                if (overrideRect.width === 0 || overrideRect.height === 0)
                    throw new TypeError("Invalid rectangle");
                /* 2. If the sum of overrideRect.x and overrideRect.width is
                 * greater than [[coded width]], return a TypeError. */
                if (overrideRect.x + overrideRect.width > this.codedWidth)
                    throw new TypeError("Invalid rectangle");
                /* 3. If the sum of overrideRect.y and overrideRect.height is
                 * greater than [[coded height]], return a TypeError. */
                if (overrideRect.y + overrideRect.height > this.codedHeight)
                    throw new TypeError("Invalid rectangle");
                // 4. Assign overrideRect to sourceRect.
                sourceRect = overrideRect;
            }
            /* 3. Let validAlignment be the result of running the Verify Rect Offset
             *    Alignment algorithm with format and sourceRect. */
            const validAlignment = this._verifyRectOffsetAlignment(sourceRect);
            // 4. If validAlignment is false, throw a TypeError.
            if (!validAlignment)
                throw new TypeError("Invalid alignment");
            // 5. Return sourceRect.
            return sourceRect;
        }
        _computeLayoutAndAllocationSize(parsedRect, layout) {
            // 1. Let numPlanes be the number of planes as defined by format.
            let numPlanes_ = numPlanes(this.format);
            /* 2. If layout is not undefined and its length does not equal
             * numPlanes, throw a TypeError. */
            if (layout && layout.length !== numPlanes_)
                throw new TypeError("Invalid layout");
            // 3. Let minAllocationSize be 0.
            let minAllocationSize = 0;
            // 4. Let computedLayouts be a new list.
            let computedLayouts = [];
            // 5. Let endOffsets be a new list.
            let endOffsets = [];
            // 6. Let planeIndex be 0.
            let planeIndex = 0;
            // 7. While planeIndex < numPlanes:
            while (planeIndex < numPlanes_) {
                /* 1. Let plane be the Plane identified by planeIndex as defined by
                 * format. */
                // 2. Let sampleBytes be the number of bytes per sample for plane.
                const sampleBytes_ = sampleBytes(this.format, planeIndex);
                /* 3. Let sampleWidth be the horizontal sub-sampling factor of each
                 * subsample for plane. */
                const sampleWidth = horizontalSubSamplingFactor(this.format, planeIndex);
                /* 4. Let sampleHeight be the vertical sub-sampling factor of each
                 * subsample for plane. */
                const sampleHeight = verticalSubSamplingFactor(this.format, planeIndex);
                // 5. Let computedLayout be a new computed plane layout.
                const computedLayout = {
                    destinationOffset: 0,
                    destinationStride: 0,
                    /* 6. Set computedLayout’s sourceTop to the result of the division
                     *    of truncated parsedRect.y by sampleHeight, rounded up to the
                     *    nearest integer. */
                    sourceTop: Math.ceil(~~parsedRect.y / sampleHeight),
                    /* 7. Set computedLayout’s sourceHeight to the result of the
                     *    division of truncated parsedRect.height by sampleHeight,
                     *    rounded up to the nearest integer. */
                    sourceHeight: Math.ceil(~~parsedRect.height / sampleHeight),
                    /* 8. Set computedLayout’s sourceLeftBytes to the result of the
                     *    integer division of truncated parsedRect.x by sampleWidth,
                     *    multiplied by sampleBytes. */
                    sourceLeftBytes: ~~(parsedRect.x / sampleWidth * sampleBytes_),
                    /* 9. Set computedLayout’s sourceWidthBytes to the result of the
                     *    integer division of truncated parsedRect.width by
                     *    sampleHeight, multiplied by sampleBytes. */
                    sourceWidthBytes: ~~(parsedRect.width / sampleWidth * sampleBytes_)
                };
                // 10. If layout is not undefined:
                if (layout) {
                    /* 1. Let planeLayout be the PlaneLayout in layout at position
                     * planeIndex. */
                    const planeLayout = layout[planeIndex];
                    /* 2. If planeLayout.stride is less than computedLayout’s
                     * sourceWidthBytes, return a TypeError. */
                    if (planeLayout.stride < computedLayout.sourceWidthBytes)
                        throw new TypeError("Invalid stride");
                    /* 3. Assign planeLayout.offset to computedLayout’s
                     * destinationOffset. */
                    computedLayout.destinationOffset = planeLayout.offset;
                    /* 4. Assign planeLayout.stride to computedLayout’s
                     * destinationStride. */
                    computedLayout.destinationStride = planeLayout.stride;
                    // 11. Otherwise:
                }
                else {
                    /* 1. Assign minAllocationSize to computedLayout’s
                     * destinationOffset. */
                    computedLayout.destinationOffset = minAllocationSize;
                    /* 2. Assign computedLayout’s sourceWidthBytes to
                     * computedLayout’s destinationStride. */
                    computedLayout.destinationStride = computedLayout.sourceWidthBytes;
                }
                /* 12. Let planeSize be the product of multiplying computedLayout’s
                 * destinationStride and sourceHeight. */
                const planeSize = computedLayout.destinationStride * computedLayout.sourceHeight;
                /* 13. Let planeEnd be the sum of planeSize and computedLayout’s
                 * destinationOffset. */
                const planeEnd = planeSize + computedLayout.destinationOffset;
                /* 14. If planeSize or planeEnd is greater than maximum range of
                 * unsigned long, return a TypeError. */
                if (planeSize >= 0x100000000 ||
                    planeEnd >= 0x100000000)
                    throw new TypeError("Plane too large");
                // 15. Append planeEnd to endOffsets.
                endOffsets.push(planeEnd);
                /* 16. Assign the maximum of minAllocationSize and planeEnd to
                 * minAllocationSize. */
                if (planeEnd > minAllocationSize)
                    minAllocationSize = planeEnd;
                // 17. Let earlierPlaneIndex be 0.
                let earlierPlaneIndex = 0;
                // 18. While earlierPlaneIndex is less than planeIndex.
                while (earlierPlaneIndex < planeIndex) {
                    // 1. Let earlierLayout be computedLayouts[earlierPlaneIndex].
                    const earlierLayout = computedLayouts[earlierPlaneIndex];
                    /* 2. If endOffsets[planeIndex] is less than or equal to
                     * earlierLayout’s destinationOffset or if
                     * endOffsets[earlierPlaneIndex] is less than or equal to
                     * computedLayout’s destinationOffset, continue. */
                    if (planeEnd <= earlierLayout.destinationOffset ||
                        endOffsets[earlierPlaneIndex] <= computedLayout.destinationOffset) ;
                    else
                        throw new TypeError("Invalid plane layout");
                    // 4. Increment earlierPlaneIndex by 1.
                    earlierPlaneIndex++;
                }
                // 19. Append computedLayout to computedLayouts.
                computedLayouts.push(computedLayout);
                // 20. Increment planeIndex by 1.
                planeIndex++;
            }
            /* 8. Let combinedLayout be a new combined buffer layout, initialized
             * as follows: */
            const combinedLayout = {
                // 1. Assign computedLayouts to computedLayouts.
                computedLayouts,
                // 2. Assign minAllocationSize to allocationSize.
                allocationSize: minAllocationSize
            };
            // 9. Return combinedLayout.
            return combinedLayout;
        }
        _verifyRectOffsetAlignment(rect) {
            // 1. If format is null, return true.
            if (!this.format)
                return true;
            // 2. Let planeIndex be 0.
            let planeIndex = 0;
            // 3. Let numPlanes be the number of planes as defined by format.
            const numPlanes_ = numPlanes(this.format);
            // 4. While planeIndex is less than numPlanes:
            while (planeIndex < numPlanes_) {
                /* 1. Let plane be the Plane identified by planeIndex as defined by
                 * format. */
                /* 2. Let sampleWidth be the horizontal sub-sampling factor of each
                 * subsample for plane. */
                const sampleWidth = horizontalSubSamplingFactor(this.format, planeIndex);
                /* 3. Let sampleHeight be the vertical sub-sampling factor of each
                 * subsample for plane. */
                const sampleHeight = verticalSubSamplingFactor(this.format, planeIndex);
                // 4. If rect.x is not a multiple of sampleWidth, return false.
                const xw = rect.x / sampleWidth;
                if (xw !== ~~xw)
                    return false;
                // 5. If rect.y is not a multiple of sampleHeight, return false.
                const yh = rect.y / sampleHeight;
                if (yh !== ~~yh)
                    return false;
                // 6. Increment planeIndex by 1.
                planeIndex++;
            }
            // 5. Return true.
            return true;
        }
        copyTo(destination, options = {}) {
            return __awaiter$5(this, void 0, void 0, function* () {
                const destBuf = new Uint8Array(destination.buffer || destination, destination.byteOffset || 0);
                // 1. If [[Detached]] is true, throw an InvalidStateError DOMException.
                if (this._data === null)
                    throw new DOMException("Detached", "InvalidStateError");
                // 2. If [[format]] is null, throw a NotSupportedError DOMException.
                if (!this.format)
                    throw new DOMException("No format", "NotSupportedError");
                /* 3. Let combinedLayout be the result of running the Parse
                 * VideoFrameCopyToOptions algorithm with options. */
                /* 4. If combinedLayout is an exception, return a promise rejected with
                 * combinedLayout. */
                const combinedLayout = this._parseVideoFrameCopyToOptions(options);
                /* 5. If destination.byteLength is less than combinedLayout’s
                 * allocationSize, return a promise rejected with a TypeError. */
                if (destination.byteLength < combinedLayout.allocationSize)
                    throw new TypeError("Insufficient space");
                // 6. Let p be a new Promise.
                /* 7. Let copyStepsQueue be the result of starting a new parallel
                 * queue. */
                // 8. Let planeLayouts be a new list.
                let planeLayouts = [];
                // 9. Enqueue the following steps to copyStepsQueue:
                {
                    /* 1. Let resource be the media resource referenced by [[resource
                     * reference]]. */
                    /* 2. Let numPlanes be the number of planes as defined by
                     *    [[format]]. */
                    numPlanes(this.format);
                    // 3. Let planeIndex be 0.
                    let planeIndex = 0;
                    // 4. While planeIndex is less than combinedLayout’s numPlanes:
                    while (planeIndex < combinedLayout.computedLayouts.length) {
                        /* 1. Let sourceStride be the stride of the plane in resource as
                         * identified by planeIndex. */
                        const sourceStride = this._layout[planeIndex].stride;
                        /* 2. Let computedLayout be the computed plane layout in
                         * combinedLayout’s computedLayouts at the position of planeIndex */
                        const computedLayout = combinedLayout.computedLayouts[planeIndex];
                        /* 3. Let sourceOffset be the product of multiplying
                         * computedLayout’s sourceTop by sourceStride */
                        let sourceOffset = computedLayout.sourceTop * sourceStride;
                        // 4. Add computedLayout’s sourceLeftBytes to sourceOffset.
                        sourceOffset += computedLayout.sourceLeftBytes;
                        // 5. Let destinationOffset be computedLayout’s destinationOffset.
                        let destinationOffset = computedLayout.destinationOffset;
                        // 6. Let rowBytes be computedLayout’s sourceWidthBytes.
                        const rowBytes = computedLayout.sourceWidthBytes;
                        /* 7. Let layout be a new PlaneLayout, with offset set to
                         *    destinationOffset and stride set to rowBytes. */
                        const layout = {
                            offset: computedLayout.destinationOffset,
                            stride: computedLayout.destinationStride
                        };
                        // 8. Let row be 0.
                        let row = 0;
                        // 9. While row is less than computedLayout’s sourceHeight:
                        while (row < computedLayout.sourceHeight) {
                            /* 1. Copy rowBytes bytes from resource starting at
                             * sourceOffset to destination starting at destinationOffset. */
                            destBuf.set(this._data.subarray(sourceOffset, sourceOffset + rowBytes), destinationOffset);
                            // 2. Increment sourceOffset by sourceStride.
                            sourceOffset += sourceStride;
                            /* 3. Increment destinationOffset by computedLayout’s
                             * destinationStride. */
                            destinationOffset += computedLayout.destinationStride;
                            // 4. Increment row by 1.
                            row++;
                        }
                        // 10. Increment planeIndex by 1.
                        planeIndex++;
                        // 11. Append layout to planeLayouts.
                        planeLayouts.push(layout);
                    }
                    // 5. Queue a task to resolve p with planeLayouts.
                }
                // 10. Return p.
                return planeLayouts;
            });
        }
        clone() {
            return new VideoFrame(this._data, {
                format: this.format,
                codedWidth: this.codedWidth,
                codedHeight: this.codedHeight,
                timestamp: this.timestamp,
                duration: this.duration,
                layout: this._layout,
                transfer: [this._data.buffer]
            });
        }
        close() {
            this._data = null;
        }
    };
    /**
     * Convert a WebCodecs pixel format to a libav pixel format.
     * @param libav  LibAV instance for constants
     * @param wcFormat  WebCodecs format
     */
    function wcFormatToLibAVFormat(libav, wcFormat) {
        let format = libav.AV_PIX_FMT_RGBA;
        switch (wcFormat) {
            case "I420":
                format = libav.AV_PIX_FMT_YUV420P;
                break;
            case "I420P10":
                format = 0x3E; /* AV_PIX_FMT_YUV420P10 */
                break;
            case "I420P12":
                format = 0x7B; /* AV_PIX_FMT_YUV420P12 */
                break;
            case "I420A":
                format = libav.AV_PIX_FMT_YUVA420P;
                break;
            case "I420AP10":
                format = 0x57; /* AV_PIX_FMT_YUVA420P10 */
                break;
            case "I420AP12":
                throw new TypeError("YUV420P12 is not supported by libav");
            case "I422":
                format = libav.AV_PIX_FMT_YUV422P;
                break;
            case "I422P10":
                format = 0x40; /* AV_PIX_FMT_YUV422P10 */
                break;
            case "I422P12":
                format = 0x7F; /* AV_PIX_FMT_YUV422P12 */
                break;
            case "I422A":
                format = 0x4E; /* AV_PIX_FMT_YUVA422P */
                break;
            case "I422AP10":
                format = 0x59; /* AV_PIX_FMT_YUVA422P10 */
                break;
            case "I422AP10":
                format = 0xBA; /* AV_PIX_FMT_YUVA422P12 */
                break;
            case "I444":
                format = libav.AV_PIX_FMT_YUV444P;
                break;
            case "I444P10":
                format = 0x44; /* AV_PIX_FMT_YUV444P10 */
                break;
            case "I444P12":
                format = 0x83; /* AV_PIX_FMT_YUV444P12 */
                break;
            case "I444A":
                format = 0x4F; /* AV_PIX_FMT_YUVA444P */
                break;
            case "I444AP10":
                format = 0x5B; /* AV_PIX_FMT_YUVA444P10 */
                break;
            case "I444AP12":
                format = 0xBC; /* AV_PIX_FMT_YUVA444P10 */
                break;
            case "NV12":
                format = libav.AV_PIX_FMT_NV12;
                break;
            case "RGBA":
                format = libav.AV_PIX_FMT_RGBA;
                break;
            case "RGBX":
                format = 0x77; /* AV_PIX_FMT_RGB0 */
                break;
            case "BGRA":
                format = libav.AV_PIX_FMT_BGRA;
                break;
            case "BGRX":
                format = 0x79; /* AV_PIX_FMT_BGR0 */
                break;
            default:
                throw new TypeError("Invalid VideoPixelFormat");
        }
        return format;
    }
    /**
     * Number of planes in the given format.
     * @param format  The format
     */
    function numPlanes(format) {
        switch (format) {
            case "I420":
            case "I420P10":
            case "I420P12":
            case "I422":
            case "I422P10":
            case "I422P12":
            case "I444":
            case "I444P10":
            case "I444P12":
                return 3;
            case "I420A":
            case "I420AP10":
            case "I420AP12":
            case "I422A":
            case "I422AP10":
            case "I422AP12":
            case "I444A":
            case "I444AP10":
            case "I444AP12":
                return 4;
            case "NV12":
                return 2;
            case "RGBA":
            case "RGBX":
            case "BGRA":
            case "BGRX":
                return 1;
            default:
                throw new DOMException("Unsupported video pixel format", "NotSupportedError");
        }
    }
    /**
     * Number of bytes per sample in the given format and plane.
     * @param format  The format
     * @param planeIndex  The plane index
     */
    function sampleBytes(format, planeIndex) {
        switch (format) {
            case "I420":
            case "I420A":
            case "I422":
            case "I422A":
            case "I444":
            case "I444A":
                return 1;
            case "I420P10":
            case "I420AP10":
            case "I422P10":
            case "I422AP10":
            case "I444P10":
            case "I444AP10":
            case "I420P12":
            case "I420AP12":
            case "I422P12":
            case "I422AP12":
            case "I444P12":
            case "I444AP12":
                return 2;
            case "NV12":
                if (planeIndex === 1)
                    return 2;
                else
                    return 1;
            case "RGBA":
            case "RGBX":
            case "BGRA":
            case "BGRX":
                return 4;
            default:
                throw new DOMException("Unsupported video pixel format", "NotSupportedError");
        }
    }
    /**
     * Horizontal sub-sampling factor for the given format and plane.
     * @param format  The format
     * @param planeIndex  The plane index
     */
    function horizontalSubSamplingFactor(format, planeIndex) {
        // First plane (often luma) is always full
        if (planeIndex === 0)
            return 1;
        // Plane 3 (alpha if present) is always full
        if (planeIndex === 3)
            return 1;
        switch (format) {
            case "I420":
            case "I420P10":
            case "I420P12":
            case "I420A":
            case "I420AP10":
            case "I420AP12":
            case "I422":
            case "I422P10":
            case "I422P12":
            case "I422A":
            case "I422AP10":
            case "I422AP12":
                return 2;
            case "I444":
            case "I444P10":
            case "I444P12":
            case "I444A":
            case "I444AP10":
            case "I444AP12":
                return 1;
            case "NV12":
                return 2;
            case "RGBA":
            case "RGBX":
            case "BGRA":
            case "BGRX":
                return 1;
            default:
                throw new DOMException("Unsupported video pixel format", "NotSupportedError");
        }
    }
    /**
     * Vertical sub-sampling factor for the given format and plane.
     * @param format  The format
     * @param planeIndex  The plane index
     */
    function verticalSubSamplingFactor(format, planeIndex) {
        // First plane (often luma) is always full
        if (planeIndex === 0)
            return 1;
        // Plane 3 (alpha if present) is always full
        if (planeIndex === 3)
            return 1;
        switch (format) {
            case "I420":
            case "I420P10":
            case "I420P12":
            case "I420A":
            case "I420AP10":
            case "I420AP12":
                return 2;
            case "I422":
            case "I422P10":
            case "I422P12":
            case "I422A":
            case "I422AP10":
            case "I422AP12":
            case "I444":
            case "I444P10":
            case "I444P12":
            case "I444A":
            case "I444AP10":
            case "I444AP12":
                return 1;
            case "NV12":
                return 2;
            case "RGBA":
            case "RGBX":
            case "BGRA":
            case "BGRX":
                return 1;
            default:
                throw new DOMException("Unsupported video pixel format", "NotSupportedError");
        }
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$4 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    let VideoDecoder$1 = class VideoDecoder extends DequeueEventTarget {
        constructor(init) {
            super();
            // 1. Let d be a new VideoDecoder object.
            // 2. Assign a new queue to [[control message queue]].
            this._p = Promise.all([]);
            // 3. Assign false to [[message queue blocked]].
            // (unneeded in polyfill)
            // 4. Assign null to [[codec implementation]].
            this._libav = null;
            this._codec = this._c = this._pkt = this._frame = 0;
            /* 5. Assign the result of starting a new parallel queue to
             *    [[codec work queue]]. */
            // (shared queue)
            // 6. Assign false to [[codec saturated]].
            // (saturation not needed)
            // 7. Assign init.output to [[output callback]].
            this._output = init.output;
            // 8. Assign init.error to [[error callback]].
            this._error = init.error;
            // 9. Assign null to [[active decoder config]].
            // (part of codec)
            // 10. Assign true to [[key chunk required]].
            // (part of codec)
            // 11. Assign "unconfigured" to [[state]]
            this.state = "unconfigured";
            // 12. Assign 0 to [[decodeQueueSize]].
            this.decodeQueueSize = 0;
            // 13. Assign a new list to [[pending flush promises]].
            // (shared queue)
            // 14. Assign false to [[dequeue event scheduled]].
            // (not needed in polyfill)
            // 15. Return d.
        }
        configure(config) {
            // 1. If config is not a valid VideoDecoderConfig, throw a TypeError.
            // NOTE: We don't support sophisticated codec string parsing (yet)
            // 2. If [[state]] is “closed”, throw an InvalidStateError DOMException.
            if (this.state === "closed")
                throw new DOMException("Decoder is closed", "InvalidStateError");
            // Free any internal state
            if (this._libav)
                this._p = this._p.then(() => this._free());
            // 3. Set [[state]] to "configured".
            this.state = "configured";
            // 4. Set [[key chunk required]] to true.
            // (part of the codec)
            // 5. Queue a control message to configure the decoder with config.
            this._p = this._p.then(() => __awaiter$4(this, void 0, void 0, function* () {
                /* 1. Let supported be the result of running the Check
                 * Configuration Support algorithm with config. */
                const supported = decoder(config.codec, config);
                /* 2. If supported is false, queue a task to run the Close
                 *    VideoDecoder algorithm with NotSupportedError and abort these
                 *    steps. */
                if (!supported) {
                    this._closeVideoDecoder(new DOMException("Unsupported codec", "NotSupportedError"));
                    return;
                }
                /* 3. If needed, assign [[codec implementation]] with an
                 *    implementation supporting config. */
                // 4. Configure [[codec implementation]] with config.
                const libav = this._libav = yield get();
                // Initialize
                [this._codec, this._c, this._pkt, this._frame] =
                    yield libav.ff_init_decoder(supported.codec);
                yield libav.AVCodecContext_time_base_s(this._c, 1, 1000);
                // 5. queue a task to run the following steps:
                // 1. Assign false to [[message queue blocked]].
                // 2. Queue a task to Process the control message queue.
            })).catch(this._error);
        }
        // Our own algorithm, close libav
        _free() {
            return __awaiter$4(this, void 0, void 0, function* () {
                if (this._c) {
                    yield this._libav.ff_free_decoder(this._c, this._pkt, this._frame);
                    this._codec = this._c = this._pkt = this._frame = 0;
                }
                if (this._libav) {
                    free(this._libav);
                    this._libav = null;
                }
            });
        }
        _closeVideoDecoder(exception) {
            // 1. Run the Reset VideoDecoder algorithm with exception.
            this._resetVideoDecoder(exception);
            // 2. Set [[state]] to "closed".
            this.state = "closed";
            /* 3. Clear [[codec implementation]] and release associated system
             * resources. */
            this._p = this._p.then(() => this._free());
            /* 4. If exception is not an AbortError DOMException, invoke the
             *    [[error callback]] with exception. */
            if (exception.name !== "AbortError")
                this._p = this._p.then(() => { this._error(exception); });
        }
        _resetVideoDecoder(exception) {
            // 1. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Decoder closed", "InvalidStateError");
            // 2. Set [[state]] to "unconfigured".
            this.state = "unconfigured";
            // ... really, we're just going to free it now
            this._p = this._p.then(() => this._free());
        }
        decode(chunk) {
            const self = this;
            // 1. If [[state]] is not "configured", throw an InvalidStateError.
            if (this.state !== "configured")
                throw new DOMException("Unconfigured", "InvalidStateError");
            // 2. If [[key chunk required]] is true:
            //    1. If chunk.[[type]] is not key, throw a DataError.
            /*    2. Implementers SHOULD inspect the chunk’s [[internal data]] to
             *    verify that it is truly a key chunk. If a mismatch is detected,
             *    throw a DataError. */
            //    3. Otherwise, assign false to [[key chunk required]].
            // 3. Increment [[decodeQueueSize]].
            this.decodeQueueSize++;
            // 4. Queue a control message to decode the chunk.
            this._p = this._p.then(function () {
                return __awaiter$4(this, void 0, void 0, function* () {
                    const libav = self._libav;
                    const c = self._c;
                    const pkt = self._pkt;
                    const frame = self._frame;
                    let decodedOutputs = null;
                    /* 3. Decrement [[decodeQueueSize]] and run the Schedule Dequeue
                     *    Event algorithm. */
                    self.decodeQueueSize--;
                    self.dispatchEvent(new CustomEvent("dequeue"));
                    // 1. Attempt to use [[codec implementation]] to decode the chunk.
                    try {
                        // Convert to a libav packet
                        const ptsFull = Math.floor(chunk.timestamp / 1000);
                        const [pts, ptshi] = libav.f64toi64(ptsFull);
                        const packet = {
                            data: chunk._libavGetData(),
                            pts,
                            ptshi,
                            dts: pts,
                            dtshi: ptshi
                        };
                        if (chunk.duration) {
                            packet.duration = Math.floor(chunk.duration / 1000);
                            packet.durationhi = 0;
                        }
                        decodedOutputs = yield libav.ff_decode_multi(c, pkt, frame, [packet]);
                        /* 2. If decoding results in an error, queue a task on the control
                         * thread event loop to run the Close VideoDecoder algorithm with
                         * EncodingError. */
                    }
                    catch (ex) {
                        self._p = self._p.then(() => {
                            self._closeVideoDecoder(ex);
                        });
                    }
                    /* 3. If [[codec saturated]] equals true and
                     *    [[codec implementation]] is no longer saturated, queue a task
                     *    to perform the following steps: */
                    // 1. Assign false to [[codec saturated]].
                    // 2. Process the control message queue.
                    // (unneeded)
                    /* 4. Let decoded outputs be a list of decoded video data outputs
                     *    emitted by [[codec implementation]] in presentation order. */
                    /* 5. If decoded outputs is not empty, queue a task to run the
                     *    Output VideoFrame algorithm with decoded outputs. */
                    if (decodedOutputs)
                        self._outputVideoFrames(decodedOutputs);
                });
            }).catch(this._error);
        }
        _outputVideoFrames(frames) {
            const libav = this._libav;
            for (const frame of frames) {
                // 1. format
                let format;
                switch (frame.format) {
                    case libav.AV_PIX_FMT_YUV420P:
                        format = "I420";
                        break;
                    case 0x3E: /* AV_PIX_FMT_YUV420P10 */
                        format = "I420P10";
                        break;
                    case 0x7B: /* AV_PIX_FMT_YUV420P12 */
                        format = "I420P12";
                        break;
                    case libav.AV_PIX_FMT_YUVA420P:
                        format = "I420A";
                        break;
                    case 0x57: /* AV_PIX_FMT_YUVA420P10 */
                        format = "I420AP10";
                        break;
                    case libav.AV_PIX_FMT_YUV422P:
                        format = "I422";
                        break;
                    case 0x40: /* AV_PIX_FMT_YUV422P10 */
                        format = "I422P10";
                        break;
                    case 0x7F: /* AV_PIX_FMT_YUV422P12 */
                        format = "I422P12";
                        break;
                    case 0x4E: /* AV_PIX_FMT_YUVA422P */
                        format = "I422A";
                        break;
                    case 0x59: /* AV_PIX_FMT_YUVA422P10 */
                        format = "I422AP10";
                        break;
                    case 0xBA: /* AV_PIX_FMT_YUVA422P12 */
                        format = "I422AP12";
                        break;
                    case libav.AV_PIX_FMT_YUV444P:
                        format = "I444";
                        break;
                    case 0x44: /* AV_PIX_FMT_YUV444P10 */
                        format = "I444P10";
                        break;
                    case 0x83: /* AV_PIX_FMT_YUV444P12 */
                        format = "I444P12";
                        break;
                    case 0x4F: /* AV_PIX_FMT_YUVA444P */
                        format = "I444A";
                        break;
                    case 0x5B: /* AV_PIX_FMT_YUVA444P10 */
                        format = "I444AP10";
                        break;
                    case 0xBC: /* AV_PIX_FMT_YUVA444P12 */
                        format = "I444AP12";
                        break;
                    case libav.AV_PIX_FMT_NV12:
                        format = "NV12";
                        break;
                    case libav.AV_PIX_FMT_RGBA:
                        format = "RGBA";
                        break;
                    case 0x77: /* AV_PIX_FMT_RGB0 */
                        format = "RGBX";
                        break;
                    case libav.AV_PIX_FMT_BGRA:
                        format = "BGRA";
                        break;
                    case 0x79: /* AV_PIX_FMT_BGR0 */
                        format = "BGRX";
                        break;
                    default:
                        throw new DOMException("Unsupported libav format!", "EncodingError");
                }
                // 2. width and height
                const codedWidth = frame.width;
                const codedHeight = frame.height;
                // 3. cropping
                let visibleRect;
                if (frame.crop) {
                    visibleRect = new DOMRect(frame.crop.left, frame.crop.top, codedWidth - frame.crop.left - frame.crop.right, codedHeight - frame.crop.top - frame.crop.bottom);
                }
                else {
                    visibleRect = new DOMRect(0, 0, codedWidth, codedHeight);
                }
                // Check for non-square pixels
                let displayWidth = codedWidth;
                let displayHeight = codedHeight;
                if (frame.sample_aspect_ratio && frame.sample_aspect_ratio[0]) {
                    const sar = frame.sample_aspect_ratio;
                    if (sar[0] > sar[1])
                        displayWidth = ~~(codedWidth * sar[0] / sar[1]);
                    else
                        displayHeight = ~~(codedHeight * sar[1] / sar[0]);
                }
                // 3. timestamp
                const timestamp = libav.i64tof64(frame.pts, frame.ptshi) * 1000;
                const data = new VideoFrame$1(frame.data, {
                    layout: frame.layout,
                    format, codedWidth, codedHeight, visibleRect, displayWidth, displayHeight,
                    timestamp
                });
                this._output(data);
            }
        }
        flush() {
            /* 1. If [[state]] is not "configured", return a promise rejected with
             *    InvalidStateError DOMException. */
            if (this.state !== "configured")
                throw new DOMException("Invalid state", "InvalidStateError");
            // 2. Set [[key chunk required]] to true.
            // (handled by codec)
            // 3. Let promise be a new Promise.
            // 4. Append promise to [[pending flush promises]].
            // 5. Queue a control message to flush the codec with promise.
            // 6. Process the control message queue.
            const ret = this._p.then(() => __awaiter$4(this, void 0, void 0, function* () {
                /* 1. Signal [[codec implementation]] to emit all internal pending
                 *    outputs. */
                if (!this._c)
                    return;
                // Make sure any last data is flushed
                const libav = this._libav;
                const c = this._c;
                const pkt = this._pkt;
                const frame = this._frame;
                let decodedOutputs = null;
                try {
                    decodedOutputs = yield libav.ff_decode_multi(c, pkt, frame, [], true);
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeVideoDecoder(ex);
                    });
                }
                /* 2. Let decoded outputs be a list of decoded video data outputs
                 *    emitted by [[codec implementation]]. */
                // 3. Queue a task to perform these steps:
                {
                    /* 1. If decoded outputs is not empty, run the Output VideoFrame
                     *    algorithm with decoded outputs. */
                    if (decodedOutputs)
                        this._outputVideoFrames(decodedOutputs);
                    // 2. Remove promise from [[pending flush promises]].
                    // 3. Resolve promise.
                }
            }));
            this._p = ret;
            // 7. Return promise.
            return ret;
        }
        reset() {
            this._resetVideoDecoder(new DOMException("Reset", "AbortError"));
        }
        close() {
            this._closeVideoDecoder(new DOMException("Close", "AbortError"));
        }
        static isConfigSupported(config) {
            return __awaiter$4(this, void 0, void 0, function* () {
                const dec = decoder(config.codec, config);
                let supported = false;
                if (dec) {
                    const libav = yield get();
                    try {
                        const [, c, pkt, frame] = yield libav.ff_init_decoder(dec.codec);
                        yield libav.ff_free_decoder(c, pkt, frame);
                        supported = true;
                    }
                    catch (ex) { }
                    yield free(libav);
                }
                return {
                    supported,
                    config: cloneConfig(config, ["codec", "codedWidth", "codedHeight"])
                };
            });
        }
    };

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$3 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    let VideoEncoder$1 = class VideoEncoder extends DequeueEventTarget {
        constructor(init) {
            super();
            this._extradataSet = false;
            this._extradata = null;
            // If our output uses non-square pixels, that information
            this._nonSquarePixels = false;
            this._sar_num = 1;
            this._sar_den = 1;
            // 1. Let e be a new VideoEncoder object.
            // 2. Assign a new queue to [[control message queue]].
            this._p = Promise.all([]);
            // 3. Assign false to [[message queue blocked]].
            // (unneeded in polyfill)
            // 4. Assign null to [[codec implementation]].
            this._libav = null;
            this._codec = this._c = this._frame = this._pkt = 0;
            /* 5. Assign the result of starting a new parallel queue to
             *    [[codec work queue]]. */
            // (shared queue)
            // 6. Assign false to [[codec saturated]].
            // (saturation unneeded)
            // 7. Assign init.output to [[output callback]].
            this._output = init.output;
            // 8. Assign init.error to [[error callback]].
            this._error = init.error;
            // 9. Assign null to [[active encoder config]].
            // (part of codec)
            // 10. Assign null to [[active output config]].
            this._metadata = null;
            // 11. Assign "unconfigured" to [[state]]
            this.state = "unconfigured";
            // 12. Assign 0 to [[encodeQueueSize]].
            this.encodeQueueSize = 0;
            // 13. Assign a new list to [[pending flush promises]].
            // (shared queue)
            // 14. Assign false to [[dequeue event scheduled]].
            // (shared queue)
            // 15. Return e.
        }
        configure(config) {
            // 1. If config is not a valid VideoEncoderConfig, throw a TypeError.
            // NOTE: We don't support sophisticated codec string parsing (yet)
            // 2. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Encoder is closed", "InvalidStateError");
            // Free any internal state
            if (this._libav)
                this._p = this._p.then(() => this._free());
            // 3. Set [[state]] to "configured".
            this.state = "configured";
            // 4. Queue a control message to configure the encoder using config.
            this._p = this._p.then(() => __awaiter$3(this, void 0, void 0, function* () {
                /* 1. Let supported be the result of running the Check
                 * Configuration Support algorithm with config. */
                const supported = encoder(config.codec, config);
                /* 2. If supported is false, queue a task to run the Close
                 *    VideoEncoder algorithm with NotSupportedError and abort these
                 *    steps. */
                if (!supported) {
                    this._closeVideoEncoder(new DOMException("Unsupported codec", "NotSupportedError"));
                    return;
                }
                /* 3. If needed, assign [[codec implementation]] with an
                 *    implementation supporting config. */
                // 4. Configure [[codec implementation]] with config.
                const libav = this._libav = yield get();
                this._metadata = {
                    decoderConfig: {
                        codec: supported.codec
                    }
                };
                // And initialize
                [this._codec, this._c, this._frame, this._pkt] =
                    yield libav.ff_init_encoder(supported.codec, supported);
                this._extradataSet = false;
                this._extradata = null;
                yield libav.AVCodecContext_time_base_s(this._c, 1, 1000);
                const width = config.width;
                const height = config.height;
                this._sws = 0;
                this._swsFrame = 0;
                this._swsOut = {
                    width, height,
                    format: supported.ctx.pix_fmt
                };
                // Check for non-square pixels
                const dWidth = config.displayWidth || width;
                const dHeight = config.displayHeight || height;
                if (dWidth !== width || dHeight !== height) {
                    this._nonSquarePixels = true;
                    this._sar_num = dWidth * height;
                    this._sar_den = dHeight * width;
                }
                else {
                    this._nonSquarePixels = false;
                }
                // 5. queue a task to run the following steps:
                // 1. Assign false to [[message queue blocked]].
                // 2. Queue a task to Process the control message queue.
            })).catch(this._error);
        }
        // Our own algorithm, close libav
        _free() {
            return __awaiter$3(this, void 0, void 0, function* () {
                if (this._sws) {
                    yield this._libav.av_frame_free_js(this._swsFrame);
                    yield this._libav.sws_freeContext(this._sws);
                    this._sws = this._swsFrame = 0;
                    this._swsIn = this._swsOut = void 0;
                }
                if (this._c) {
                    yield this._libav.ff_free_encoder(this._c, this._frame, this._pkt);
                    this._codec = this._c = this._frame = this._pkt = 0;
                }
                if (this._libav) {
                    free(this._libav);
                    this._libav = null;
                }
            });
        }
        _closeVideoEncoder(exception) {
            // 1. Run the Reset VideoEncoder algorithm with exception.
            this._resetVideoEncoder(exception);
            // 2. Set [[state]] to "closed".
            this.state = "closed";
            /* 3. Clear [[codec implementation]] and release associated system
             * resources. */
            this._p = this._p.then(() => this._free());
            /* 4. If exception is not an AbortError DOMException, invoke the
             *    [[error callback]] with exception. */
            if (exception.name !== "AbortError")
                this._p = this._p.then(() => { this._error(exception); });
        }
        _resetVideoEncoder(exception) {
            // 1. If [[state]] is "closed", throw an InvalidStateError.
            if (this.state === "closed")
                throw new DOMException("Encoder closed", "InvalidStateError");
            // 2. Set [[state]] to "unconfigured".
            this.state = "unconfigured";
            // ... really, we're just going to free it now
            this._p = this._p.then(() => this._free());
        }
        encode(frame, options = {}) {
            /* 1. If the value of frame’s [[Detached]] internal slot is true, throw
             * a TypeError. */
            if (frame._libavGetData() === null)
                throw new TypeError("Detached");
            // 2. If [[state]] is not "configured", throw an InvalidStateError.
            if (this.state !== "configured")
                throw new DOMException("Unconfigured", "InvalidStateError");
            /* 3. Let frameClone hold the result of running the Clone VideoFrame
             * algorithm with frame. */
            const frameClone = frame.clone();
            // 4. Increment [[encodeQueueSize]].
            this.encodeQueueSize++;
            // 5. Queue a control message to encode frameClone.
            this._p = this._p.then(() => __awaiter$3(this, void 0, void 0, function* () {
                const libav = this._libav;
                const c = this._c;
                const pkt = this._pkt;
                const framePtr = this._frame;
                const swsOut = this._swsOut;
                let encodedOutputs = null;
                /* 3. Decrement [[encodeQueueSize]] and run the Schedule Dequeue
                 *    Event algorithm. */
                this.encodeQueueSize--;
                this.dispatchEvent(new CustomEvent("dequeue"));
                /* 1. Attempt to use [[codec implementation]] to encode frameClone
                 * according to options. */
                try {
                    // Convert the format
                    const format = wcFormatToLibAVFormat(libav, frameClone.format);
                    // Convert the data
                    const rawU8 = frameClone._libavGetData();
                    const layout = frameClone._libavGetLayout();
                    // Convert the timestamp
                    const ptsFull = Math.floor(frameClone.timestamp / 1000);
                    const [pts, ptshi] = libav.f64toi64(ptsFull);
                    // Make the frame
                    const frame = {
                        data: rawU8, layout,
                        format, pts, ptshi,
                        width: frameClone.codedWidth,
                        height: frameClone.codedHeight,
                        crop: {
                            left: frameClone.visibleRect.left,
                            right: frameClone.visibleRect.right,
                            top: frameClone.visibleRect.top,
                            bottom: frameClone.visibleRect.bottom
                        },
                        key_frame: options.keyFrame ? 1 : 0,
                        pict_type: options.keyFrame ? 1 : 0
                    };
                    // Possibly scale
                    if (frame.width !== swsOut.width ||
                        frame.height !== swsOut.height ||
                        frame.format !== swsOut.format) {
                        if (frameClone._nonSquarePixels) {
                            frame.sample_aspect_ratio = [
                                frameClone._sar_num,
                                frameClone._sar_den
                            ];
                        }
                        // Need a scaler
                        let sws = this._sws, swsIn = this._swsIn, swsFrame = this._swsFrame;
                        if (!sws ||
                            frame.width !== swsIn.width ||
                            frame.height !== swsIn.height ||
                            frame.format !== swsIn.format) {
                            // Need to allocate the scaler
                            if (sws)
                                yield libav.sws_freeContext(sws);
                            swsIn = {
                                width: frame.width,
                                height: frame.height,
                                format: frame.format
                            };
                            sws = yield libav.sws_getContext(swsIn.width, swsIn.height, swsIn.format, swsOut.width, swsOut.height, swsOut.format, 2, 0, 0, 0);
                            this._sws = sws;
                            this._swsIn = swsIn;
                            // Maybe need a frame
                            if (!swsFrame)
                                this._swsFrame = swsFrame = yield libav.av_frame_alloc();
                        }
                        // Scale and encode the frame
                        const [, swsRes, , , , , , encRes] = yield Promise.all([
                            libav.ff_copyin_frame(framePtr, frame),
                            libav.sws_scale_frame(sws, swsFrame, framePtr),
                            this._nonSquarePixels ?
                                libav.AVFrame_sample_aspect_ratio_s(swsFrame, this._sar_num, this._sar_den) :
                                null,
                            libav.AVFrame_pts_s(swsFrame, pts),
                            libav.AVFrame_ptshi_s(swsFrame, ptshi),
                            libav.AVFrame_key_frame_s(swsFrame, options.keyFrame ? 1 : 0),
                            libav.AVFrame_pict_type_s(swsFrame, options.keyFrame ? 1 : 0),
                            libav.avcodec_send_frame(c, swsFrame)
                        ]);
                        if (swsRes < 0 || encRes < 0)
                            throw new Error("Encoding failed!");
                        encodedOutputs = [];
                        while (true) {
                            const recv = yield libav.avcodec_receive_packet(c, pkt);
                            if (recv === -libav.EAGAIN)
                                break;
                            else if (recv < 0)
                                throw new Error("Encoding failed!");
                            encodedOutputs.push(yield libav.ff_copyout_packet(pkt));
                        }
                    }
                    else {
                        if (this._nonSquarePixels) {
                            frame.sample_aspect_ratio = [
                                this._sar_num,
                                this._sar_den
                            ];
                        }
                        // Encode directly
                        encodedOutputs =
                            yield libav.ff_encode_multi(c, framePtr, pkt, [frame]);
                    }
                    if (encodedOutputs.length && !this._extradataSet)
                        yield this._getExtradata();
                    /* 2. If encoding results in an error, queue a task to run the
                     *    Close VideoEncoder algorithm with EncodingError and return. */
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeVideoEncoder(ex);
                    });
                    return;
                }
                /* 3. If [[codec saturated]] equals true and
                 *    [[codec implementation]] is no longer saturated, queue a task
                 *    to perform the following steps: */
                // 1. Assign false to [[codec saturated]].
                // 2. Process the control message queue.
                // (unneeded in polyfill)
                /* 4. Let encoded outputs be a list of encoded video data outputs
                 *    emitted by [[codec implementation]]. */
                /* 5. If encoded outputs is not empty, queue a task to run the
                 *    Output EncodedVideoChunks algorithm with encoded outputs. */
                if (encodedOutputs)
                    this._outputEncodedVideoChunks(encodedOutputs);
            })).catch(this._error);
        }
        // Internal: Get extradata
        _getExtradata() {
            return __awaiter$3(this, void 0, void 0, function* () {
                const libav = this._libav;
                const c = this._c;
                const extradata = yield libav.AVCodecContext_extradata(c);
                const extradata_size = yield libav.AVCodecContext_extradata_size(c);
                if (extradata && extradata_size) {
                    this._metadata.decoderConfig.description = this._extradata =
                        yield libav.copyout_u8(extradata, extradata_size);
                }
                this._extradataSet = true;
            });
        }
        _outputEncodedVideoChunks(packets) {
            const libav = this._libav;
            for (const packet of packets) {
                // 1. type
                const type = (packet.flags & 1) ? "key" : "delta";
                // 2. timestamp
                const timestamp = libav.i64tof64(packet.pts, packet.ptshi) * 1000;
                const chunk = new EncodedVideoChunk$1({
                    type: type, timestamp,
                    data: packet.data
                });
                if (this._extradataSet)
                    this._output(chunk, this._metadata || void 0);
                else
                    this._output(chunk);
            }
        }
        flush() {
            /* 1. If [[state]] is not "configured", return a promise rejected with
             *    InvalidStateError DOMException. */
            if (this.state !== "configured")
                throw new DOMException("Invalid state", "InvalidStateError");
            // 2. Let promise be a new Promise.
            // 3. Append promise to [[pending flush promises]].
            // 4. Queue a control message to flush the codec with promise.
            // 5. Process the control message queue.
            const ret = this._p.then(() => __awaiter$3(this, void 0, void 0, function* () {
                /* 1. Signal [[codec implementation]] to emit all internal pending
                 *    outputs. */
                if (!this._c)
                    return;
                // Make sure any last data is flushed
                const libav = this._libav;
                const c = this._c;
                const frame = this._frame;
                const pkt = this._pkt;
                let encodedOutputs = null;
                try {
                    encodedOutputs =
                        yield libav.ff_encode_multi(c, frame, pkt, [], true);
                    if (!this._extradataSet)
                        yield this._getExtradata();
                }
                catch (ex) {
                    this._p = this._p.then(() => {
                        this._closeVideoEncoder(ex);
                    });
                }
                /* 2. Let encoded outputs be a list of encoded video data outputs
                 *    emitted by [[codec implementation]]. */
                // 3. Queue a task to perform these steps:
                {
                    /* 1. If encoded outputs is not empty, run the Output
                     *    EncodedVideoChunks algorithm with encoded outputs. */
                    if (encodedOutputs)
                        this._outputEncodedVideoChunks(encodedOutputs);
                    // 2. Remove promise from [[pending flush promises]].
                    // 3. Resolve promise.
                }
            }));
            this._p = ret;
            // 6. Return promise.
            return ret;
        }
        reset() {
            this._resetVideoEncoder(new DOMException("Reset", "AbortError"));
        }
        close() {
            this._closeVideoEncoder(new DOMException("Close", "AbortError"));
        }
        static isConfigSupported(config) {
            return __awaiter$3(this, void 0, void 0, function* () {
                const enc = encoder(config.codec, config);
                let supported = false;
                if (enc) {
                    const libav = yield get();
                    try {
                        const [, c, frame, pkt] = yield libav.ff_init_encoder(enc.codec, enc);
                        yield libav.ff_free_encoder(c, frame, pkt);
                        supported = true;
                    }
                    catch (ex) { }
                    yield free(libav);
                }
                return {
                    supported,
                    config: cloneConfig(config, ["codec", "width", "height", "bitrate", "framerate", "latencyMode"])
                };
            });
        }
    };

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$2 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    // A non-threaded libav.js instance for scaling.
    let scalerSync = null;
    // A synchronous libav.js instance for scaling.
    let scalerAsync = null;
    // The original drawImage
    let origDrawImage = null;
    // The original drawImage Offscreen
    let origDrawImageOffscreen = null;
    // The original createImageBitmap
    let origCreateImageBitmap = null;
    /**
     * Load rendering capability.
     * @param libavOptions  Options to use while loading libav
     * @param polyfill  Set to polyfill CanvasRenderingContext2D.drawImage
     */
    function load$1(libavOptions, polyfill) {
        return __awaiter$2(this, void 0, void 0, function* () {
            // Get our scalers
            if ("importScripts" in globalThis) {
                // Make sure the worker code doesn't run
                LibAVWrapper.nolibavworker = true;
            }
            scalerSync = (yield LibAVWrapper.LibAV(Object.assign(Object.assign({}, libavOptions), { noworker: true, yesthreads: false })));
            scalerAsync = yield LibAVWrapper.LibAV(libavOptions);
            // Polyfill drawImage
            if ('CanvasRenderingContext2D' in globalThis) {
                origDrawImage = CanvasRenderingContext2D.prototype.drawImage;
                if (polyfill)
                    CanvasRenderingContext2D.prototype.drawImage = drawImagePolyfill;
            }
            if ('OffscreenCanvasRenderingContext2D' in globalThis) {
                origDrawImageOffscreen = OffscreenCanvasRenderingContext2D.prototype.drawImage;
                if (polyfill)
                    OffscreenCanvasRenderingContext2D.prototype.drawImage = drawImagePolyfillOffscreen;
            }
            // Polyfill createImageBitmap
            origCreateImageBitmap = globalThis.createImageBitmap;
            if (polyfill)
                globalThis.createImageBitmap = createImageBitmap$1;
        });
    }
    /**
     * Draw this video frame on this canvas, synchronously.
     * @param ctx  CanvasRenderingContext2D to draw on
     * @param image  VideoFrame (or anything else) to draw
     * @param sx  Source X position OR destination X position
     * @param sy  Source Y position OR destination Y position
     * @param sWidth  Source width OR destination width
     * @param sHeight  Source height OR destination height
     * @param dx  Destination X position
     * @param dy  Destination Y position
     * @param dWidth  Destination width
     * @param dHeight  Destination height
     */
    function canvasDrawImage$1(ctx, image, ax, ay, sWidth, sHeight, dx, dy, dWidth, dHeight) {
        if (!(image._data)) {
            // Just use the original
            return origDrawImage.apply(ctx, Array.prototype.slice.call(arguments, 1));
        }
        // Normalize the arguments
        if (typeof sWidth === "undefined") {
            // dx, dy
            dx = ax;
            dy = ay;
        }
        else if (typeof dx === "undefined") {
            // dx, dy, dWidth, dHeight
            dx = ax;
            dy = ay;
            dWidth = sWidth;
            dHeight = sHeight;
            sWidth = void 0;
            sHeight = void 0;
        }
        else ;
        if (typeof dWidth === "undefined") {
            dWidth = image.displayWidth;
            dHeight = image.displayHeight;
        }
        // Convert the format to libav.js
        const format = wcFormatToLibAVFormat(scalerSync, image.format);
        // Convert the frame synchronously
        const sctx = scalerSync.sws_getContext_sync(image.visibleRect.width, image.visibleRect.height, format, dWidth, dHeight, scalerSync.AV_PIX_FMT_RGBA, 2, 0, 0, 0);
        const inFrame = scalerSync.av_frame_alloc_sync();
        const outFrame = scalerSync.av_frame_alloc_sync();
        let rawU8;
        let layout;
        if (image._libavGetData) {
            rawU8 = image._libavGetData();
            layout = image._libavGetLayout();
        }
        else {
            // Just have to hope this is a polyfill VideoFrame copied weirdly!
            rawU8 = image._data;
            layout = image._layout;
        }
        // Copy it in
        scalerSync.ff_copyin_frame_sync(inFrame, {
            data: rawU8,
            layout,
            format,
            width: image.codedWidth,
            height: image.codedHeight,
            crop: {
                left: image.visibleRect.left,
                right: image.visibleRect.right,
                top: image.visibleRect.top,
                bottom: image.visibleRect.bottom
            }
        });
        // Rescale
        scalerSync.sws_scale_frame_sync(sctx, outFrame, inFrame);
        // Get the data back out again
        const frameData = scalerSync.ff_copyout_frame_video_imagedata_sync(outFrame);
        // Finally, draw it
        ctx.putImageData(frameData, dx, dy);
        // And clean up
        scalerSync.av_frame_free_js_sync(outFrame);
        scalerSync.av_frame_free_js_sync(inFrame);
        scalerSync.sws_freeContext_sync(sctx);
    }
    /**
     * Polyfill version of canvasDrawImage.
     */
    function drawImagePolyfill(image, sx, sy, sWidth, sHeight, dx, dy, dWidth, dHeight) {
        if (image instanceof VideoFrame$1) {
            return canvasDrawImage$1(this, image, sx, sy, sWidth, sHeight, dx, dy, dWidth, dHeight);
        }
        return origDrawImage.apply(this, arguments);
    }
    /**
     * Polyfill version of offscreenCanvasDrawImage.
     */
    function drawImagePolyfillOffscreen(image, sx, sy, sWidth, sHeight, dx, dy, dWidth, dHeight) {
        if (image instanceof VideoFrame$1) {
            return canvasDrawImage$1(this, image, sx, sy, sWidth, sHeight, dx, dy, dWidth, dHeight);
        }
        return origDrawImageOffscreen.apply(this, arguments);
    }
    /**
     * Create an ImageBitmap from this drawable, asynchronously. NOTE:
     * Sub-rectangles are not implemented for VideoFrames, so only options is
     * available, and there, only scaling is available.
     * @param image  VideoFrame (or anything else) to draw
     * @param options  Other options
     */
    function createImageBitmap$1(image, opts = {}) {
        if (!(image._data)) {
            // Just use the original
            return origCreateImageBitmap.apply(globalThis, arguments);
        }
        // Convert the format to libav.js
        const format = wcFormatToLibAVFormat(scalerAsync, image.format);
        // Normalize arguments
        const dWidth = (typeof opts.resizeWidth === "number")
            ? opts.resizeWidth : image.displayWidth;
        const dHeight = (typeof opts.resizeHeight === "number")
            ? opts.resizeHeight : image.displayHeight;
        // Convert the frame
        return (() => __awaiter$2(this, void 0, void 0, function* () {
            const [sctx, inFrame, outFrame] = yield Promise.all([
                scalerAsync.sws_getContext(image.visibleRect.width, image.visibleRect.height, format, dWidth, dHeight, scalerAsync.AV_PIX_FMT_RGBA, 2, 0, 0, 0),
                scalerAsync.av_frame_alloc(),
                scalerAsync.av_frame_alloc()
            ]);
            // Convert the data
            let rawU8;
            let layout = void 0;
            if (image._libavGetData) {
                rawU8 = image._libavGetData();
                layout = image._libavGetLayout();
            }
            else if (image._data) {
                // Assume a VideoFrame weirdly serialized
                rawU8 = image._data;
                layout = image._layout;
            }
            else {
                rawU8 = new Uint8Array(image.allocationSize());
                yield image.copyTo(rawU8);
            }
            // Copy it in
            yield scalerAsync.ff_copyin_frame(inFrame, {
                data: rawU8,
                layout,
                format,
                width: image.codedWidth,
                height: image.codedHeight,
                crop: {
                    left: image.visibleRect.left,
                    right: image.visibleRect.right,
                    top: image.visibleRect.top,
                    bottom: image.visibleRect.bottom
                }
            }),
                // Rescale
                yield scalerAsync.sws_scale_frame(sctx, outFrame, inFrame);
            // Get the data back out again
            const frameData = yield scalerAsync.ff_copyout_frame_video_imagedata(outFrame);
            // And clean up
            yield Promise.all([
                scalerAsync.av_frame_free_js(outFrame),
                scalerAsync.av_frame_free_js(inFrame),
                scalerAsync.sws_freeContext(sctx)
            ]);
            // Make the ImageBitmap
            return yield origCreateImageBitmap(frameData);
        }))();
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter$1 = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    /**
     * Error thrown to indicate a configuration is unsupported.
     */
    let UnsupportedException$1 = class UnsupportedException extends Error {
        constructor() {
            super("The requested configuration is not supported");
        }
    };
    /**
     * Get an AudioDecoder environment that supports this configuration. Throws an
     * UnsupportedException if no environment supports the configuration.
     * @param config  Audio decoder configuration
     */
    function getAudioDecoder$1(config) {
        return __awaiter$1(this, void 0, void 0, function* () {
            try {
                if (typeof globalThis.AudioDecoder !== "undefined" &&
                    (yield globalThis.AudioDecoder.isConfigSupported(config)).supported) {
                    return {
                        AudioDecoder: globalThis.AudioDecoder,
                        EncodedAudioChunk: globalThis.EncodedAudioChunk,
                        AudioData: globalThis.AudioData
                    };
                }
            }
            catch (ex) { }
            if ((yield AudioDecoder$1.isConfigSupported(config)).supported) {
                return {
                    AudioDecoder: AudioDecoder$1,
                    EncodedAudioChunk: EncodedAudioChunk$1,
                    AudioData: AudioData$1
                };
            }
            throw new UnsupportedException$1();
        });
    }
    /**
     * Get an VideoDecoder environment that supports this configuration. Throws an
     * UnsupportedException if no environment supports the configuration.
     * @param config  Video decoder configuration
     */
    function getVideoDecoder$1(config) {
        return __awaiter$1(this, void 0, void 0, function* () {
            try {
                if (typeof globalThis.VideoDecoder !== "undefined" &&
                    (yield globalThis.VideoDecoder.isConfigSupported(config)).supported) {
                    return {
                        VideoDecoder: globalThis.VideoDecoder,
                        EncodedVideoChunk: globalThis.EncodedVideoChunk,
                        VideoFrame: globalThis.VideoFrame
                    };
                }
            }
            catch (ex) { }
            if ((yield VideoDecoder$1.isConfigSupported(config)).supported) {
                return {
                    VideoDecoder: VideoDecoder$1,
                    EncodedVideoChunk: EncodedVideoChunk$1,
                    VideoFrame: VideoFrame$1
                };
            }
            throw new UnsupportedException$1();
        });
    }
    /**
     * Get an AudioEncoder environment that supports this configuration. Throws an
     * UnsupportedException if no environment supports the configuration.
     * @param config  Audio encoder configuration
     */
    function getAudioEncoder$1(config) {
        return __awaiter$1(this, void 0, void 0, function* () {
            try {
                if (typeof globalThis.AudioEncoder !== "undefined" &&
                    (yield globalThis.AudioEncoder.isConfigSupported(config)).supported) {
                    return {
                        AudioEncoder: globalThis.AudioEncoder,
                        EncodedAudioChunk: globalThis.EncodedAudioChunk,
                        AudioData: globalThis.AudioData
                    };
                }
            }
            catch (ex) { }
            if ((yield AudioEncoder$1.isConfigSupported(config)).supported) {
                return {
                    AudioEncoder: AudioEncoder$1,
                    EncodedAudioChunk: EncodedAudioChunk$1,
                    AudioData: AudioData$1
                };
            }
            throw new UnsupportedException$1();
        });
    }
    /**
     * Get an VideoEncoder environment that supports this configuration. Throws an
     * UnsupportedException if no environment supports the configuration.
     * @param config  Video encoder configuration
     */
    function getVideoEncoder$1(config) {
        return __awaiter$1(this, void 0, void 0, function* () {
            try {
                if (typeof globalThis.VideoEncoder !== "undefined" &&
                    (yield globalThis.VideoEncoder.isConfigSupported(config)).supported) {
                    return {
                        VideoEncoder: globalThis.VideoEncoder,
                        EncodedVideoChunk: globalThis.EncodedVideoChunk,
                        VideoFrame: globalThis.VideoFrame
                    };
                }
            }
            catch (ex) { }
            if ((yield VideoEncoder$1.isConfigSupported(config)).supported) {
                return {
                    VideoEncoder: VideoEncoder$1,
                    EncodedVideoChunk: EncodedVideoChunk$1,
                    VideoFrame: VideoFrame$1
                };
            }
            throw new UnsupportedException$1();
        });
    }

    /*
     * This file is part of the libav.js WebCodecs Polyfill implementation. The
     * interface implemented is derived from the W3C standard. No attribution is
     * required when using this library.
     *
     * Copyright (c) 2021-2024 Yahweasel
     *
     * Permission to use, copy, modify, and/or distribute this software for any
     * purpose with or without fee is hereby granted.
     *
     * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
     * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
     * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
     * SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
     * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
     * OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
     * CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
     */
    var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
        function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
        return new (P || (P = Promise))(function (resolve, reject) {
            function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
            function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
            function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
            step((generator = generator.apply(thisArg, _arguments || [])).next());
        });
    };
    /**
     * Load LibAV-WebCodecs-Polyfill.
     */
    function load(options = {}) {
        return __awaiter(this, void 0, void 0, function* () {
            // Set up libavOptions
            let libavOptions = {};
            if (options.libavOptions)
                Object.assign(libavOptions, options.libavOptions);
            // Maybe load libav
            if (!options.LibAV && typeof globalThis.LibAV === "undefined") {
                yield new Promise((res, rej) => {
                    // Can't load workers from another origin
                    libavOptions.noworker = true;
                    // Load libav
                    const libavBase = "https://cdn.jsdelivr.net/npm/@libav.js/variant-webm-vp9@6.7.7/dist";
                    globalThis.LibAV = { base: libavBase };
                    const libavVar = "libav-6.7.7.1.1-webm-vp9.js";
                    if (typeof importScripts !== "undefined") {
                        importScripts(`${libavBase}/${libavVar}`);
                        res(void 0);
                    }
                    else {
                        const scr = document.createElement("script");
                        scr.src = `${libavBase}/${libavVar}`;
                        scr.onload = res;
                        scr.onerror = rej;
                        document.body.appendChild(scr);
                    }
                });
            }
            // And load the libav handler
            if (options.LibAV)
                setLibAV(options.LibAV);
            setLibAVOptions(libavOptions);
            yield load$2();
            if (options.polyfill) {
                for (const exp of [
                    ["EncodedAudioChunk", EncodedAudioChunk$1],
                    ["AudioData", AudioData$1],
                    ["AudioDecoder", AudioDecoder$1],
                    ["AudioEncoder", AudioEncoder$1],
                    ["EncodedVideoChunk", EncodedVideoChunk$1],
                    ["VideoFrame", VideoFrame$1],
                    ["VideoDecoder", VideoDecoder$1],
                    ["VideoEncoder", VideoEncoder$1]
                ]) {
                    if (!globalThis[exp[0]])
                        globalThis[exp[0]] = exp[1];
                }
            }
            yield load$1(libavOptions, !!options.polyfill);
        });
    }
    const EncodedAudioChunk = EncodedAudioChunk$1;
    const AudioData = AudioData$1;
    const AudioDecoder = AudioDecoder$1;
    const AudioEncoder = AudioEncoder$1;
    const EncodedVideoChunk = EncodedVideoChunk$1;
    const VideoFrame = VideoFrame$1;
    const VideoDecoder = VideoDecoder$1;
    const VideoEncoder = VideoEncoder$1;
    // Rendering
    const canvasDrawImage = canvasDrawImage$1;
    const createImageBitmap = createImageBitmap$1;
    const UnsupportedException = UnsupportedException$1;
    const getAudioDecoder = getAudioDecoder$1;
    const getVideoDecoder = getVideoDecoder$1;
    const getAudioEncoder = getAudioEncoder$1;
    const getVideoEncoder = getVideoEncoder$1;

    exports.AudioData = AudioData;
    exports.AudioDecoder = AudioDecoder;
    exports.AudioEncoder = AudioEncoder;
    exports.EncodedAudioChunk = EncodedAudioChunk;
    exports.EncodedVideoChunk = EncodedVideoChunk;
    exports.UnsupportedException = UnsupportedException;
    exports.VideoDecoder = VideoDecoder;
    exports.VideoEncoder = VideoEncoder;
    exports.VideoFrame = VideoFrame;
    exports.canvasDrawImage = canvasDrawImage;
    exports.createImageBitmap = createImageBitmap;
    exports.getAudioDecoder = getAudioDecoder;
    exports.getAudioEncoder = getAudioEncoder;
    exports.getVideoDecoder = getVideoDecoder;
    exports.getVideoEncoder = getVideoEncoder;
    exports.load = load;

}));
