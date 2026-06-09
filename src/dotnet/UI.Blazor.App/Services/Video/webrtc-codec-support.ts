// WebRTC send-side codec capabilities — the analog of codec-support.ts for the
// WebRTC backend. Uses RTCRtpSender.getCapabilities('video') (no encoder
// instantiation) and maps an RTCRtpCodec to the WebCodecs-style codec string
// the receiver decodes with.

import { getLogs } from 'logging';

const { infoLog, warnLog } = getLogs('VideoRecorder');

export type WebRtcCodecCategory = 'h264' | 'hevc' | 'vp8' | 'vp9' | 'av1';

export interface WebRtcCodecChoice {
    category: WebRtcCodecCategory;
    // The RTCRtpCodec to push to the top of setCodecPreferences.
    rtcCodec: RTCRtpCodecCapability;
    // WebCodecs-style codec string for the wire DTO / receiver decoder config.
    wireCodec: string;
}

function getSendCodecs(): RTCRtpCodecCapability[] {
    // Guard via a runtime lookup — older engines may lack getCapabilities,
    // which the lib types don't model.
    const getCaps = (RTCRtpSender as unknown as {
        getCapabilities?: (kind: string) => { codecs: RTCRtpCodecCapability[] } | null;
    }).getCapabilities;
    const caps = typeof getCaps === 'function' ? getCaps('video') : null;
    return caps?.codecs ?? [];
}

function categoryOf(mimeType: string): WebRtcCodecCategory | null {
    const m = mimeType.toLowerCase();
    if (m === 'video/h264') return 'h264';
    if (m === 'video/h265' || m === 'video/hevc') return 'hevc';
    if (m === 'video/vp8') return 'vp8';
    if (m === 'video/vp9') return 'vp9';
    if (m === 'video/av1') return 'av1';
    return null;
}

function profileLevelId(sdpFmtpLine: string | undefined): string | null {
    const m = /profile-level-id=([0-9a-fA-F]{6})/.exec(sdpFmtpLine ?? '');
    return m ? m[1].toLowerCase() : null;
}

// Maps an RTCRtpCodec to the WebCodecs codec string the receiver decodes with.
// H.264 → avc1.<profile-level-id> (Annex-B, in-band SPS/PPS — no description).
// Others map to their canonical short strings; HEVC/AV1/VP9 keep a baseline
// profile string (the receiver refines from in-band/keyframe data).
export function rtcCodecToWireCodecString(codec: RTCRtpCodecCapability): string {
    const cat = categoryOf(codec.mimeType);
    switch (cat) {
    case 'h264': {
        const pid = profileLevelId(codec.sdpFmtpLine) ?? '42e01f';
        return `avc1.${pid}`;
    }
    case 'hevc':
        return 'hev1.1.6.L93.B0';
    case 'vp8':
        return 'vp8';
    case 'vp9':
        return 'vp09.00.10.08';
    case 'av1':
        return 'av01.0.04M.08';
    default:
        return 'avc1.42e01f';
    }
}

// Picks the WebRTC send codec. Defaults to H.264 — broadest support across
// Chromium + iOS Safari, Annex-B in-band parameters (no description needed),
// matching the receiver's H.264 path. `preferred` (e.g. from audience codecs)
// is honored when available.
export function pickWebRtcCodec(preferred?: WebRtcCodecCategory[]): WebRtcCodecChoice | null {
    const codecs = getSendCodecs();
    if (codecs.length === 0) {
        warnLog?.log('pickWebRtcCodec: RTCRtpSender.getCapabilities returned no codecs');
        return null;
    }
    // Fallback order is H.264/HEVC ONLY — never vp8/vp9/av1. The project's
    // encoder-codec policy (codec-support REPRESENTATIVE_CODECS) disables AV1/VP9;
    // selecting AV1 here would (a) violate that policy and (b) land on a software
    // AV1 encoder on devices without AV1 HW (reintroducing the readback we built
    // this backend to avoid). Callers pass `preferred` already constrained to the
    // active categories.
    const order: WebRtcCodecCategory[] = [
        ...(preferred ?? []),
        'h264', 'hevc',
    ];
    for (const cat of order) {
        const rtcCodec = codecs.find(c => categoryOf(c.mimeType) === cat);
        if (rtcCodec) {
            const choice: WebRtcCodecChoice = {
                category: cat,
                rtcCodec,
                wireCodec: rtcCodecToWireCodecString(rtcCodec),
            };
            infoLog?.log(`pickWebRtcCodec: ${cat} → ${choice.wireCodec} (${rtcCodec.mimeType} ${rtcCodec.sdpFmtpLine ?? ''})`);
            return choice;
        }
    }
    return null;
}

// All RTCRtpCodecs for one category — used to bias setCodecPreferences so the
// negotiated codec is the one we picked (and RTX/FEC follow).
export function codecsForCategory(category: WebRtcCodecCategory): RTCRtpCodecCapability[] {
    return getSendCodecs().filter(c => {
        const cat = categoryOf(c.mimeType);
        return cat === category
            || c.mimeType.toLowerCase() === 'video/rtx'
            || c.mimeType.toLowerCase() === 'video/red'
            || c.mimeType.toLowerCase() === 'video/ulpfec';
    });
}
