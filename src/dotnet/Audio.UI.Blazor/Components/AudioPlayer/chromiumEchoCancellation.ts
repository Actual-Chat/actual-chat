/**
 * @file Chromium doesn't apply echoCancellation to web audio pipeline.
 * The workaround is using a loopback webrtc connection.
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=687574}
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=933677}
 */
const offerOptions = {
    offerVideo: false,
    offerAudio: true,
    offerToReceiveAudio: false,
    offerToReceiveVideo: false
};

const onError = (e: any) => {
    console.error("enableChromiumAec: RTCPeerConnection loopback initialization error", e);
};

let delayedReconnectTimeout: number | null = null;

/**
 * Routes audio through Chrome's audio mixer (via a loopback WebRTC connection),
 * thus enabling Acoustic Echo Cancellation (AEC) while preserving all the audio processing with WebAudio API.
 * DOESN'T WORK WITH `AudioContext.destination`!
 * playing with Audio tag is necessary as workaround of
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=933677}
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=687574}
 * @param {MediaStream} stream Audio input
 * @returns {Promise<MediaStream>} Audio output with applied echoCancellation
 */
export async function enableChromiumAec(stream: MediaStream): Promise<MediaStream> {
    const loopbackStream = new MediaStream();
    const outboundPeerConnection = new RTCPeerConnection();
    /** loopback connection */
    const inboundPeerConnection = new RTCPeerConnection();

    outboundPeerConnection.onicecandidate = (e) => e.candidate
        && inboundPeerConnection.addIceCandidate(e.candidate).catch(onError);

    outboundPeerConnection.addEventListener("iceconnectionstatechange", () => {
        if (outboundPeerConnection.iceConnectionState === "disconnected") {
            delayedReconnect(stream);
        }
        if (outboundPeerConnection.iceConnectionState === "connected") {
            if (delayedReconnectTimeout) {
                // The RTCPeerConnection reconnected by itself, cancel recreating the local connection.
                clearTimeout(delayedReconnectTimeout);
            }
        }
    });

    inboundPeerConnection.onicecandidate = (e) => e.candidate
        && outboundPeerConnection.addIceCandidate(e.candidate).catch(onError);
    inboundPeerConnection.addEventListener("iceconnectionstatechange", () => {
        if (inboundPeerConnection.iceConnectionState === "disconnected") {
            delayedReconnect(stream);
        }
        if (inboundPeerConnection.iceConnectionState === "connected") {
            if (delayedReconnectTimeout) {
                // The RTCPeerConnection reconnected by itself, cancel recreating the local connection.
                clearTimeout(delayedReconnectTimeout);
            }
        }
    });
    try {

        inboundPeerConnection.ontrack = (e) =>
            e.streams[0].getTracks().forEach((track) => loopbackStream.addTrack(track));

        // setup the loopback
        stream.getTracks().forEach((track) => outboundPeerConnection.addTrack(track, stream));

        const offer = await outboundPeerConnection.createOffer(offerOptions);
        await outboundPeerConnection.setLocalDescription(offer);

        await inboundPeerConnection.setRemoteDescription(offer);
        const answer = await inboundPeerConnection.createAnswer();
        // we can rewrite answer's SDP here to apply better bitrate, see 'sdp-transform' package
        await inboundPeerConnection.setLocalDescription(answer);
        await outboundPeerConnection.setRemoteDescription(answer);

        return loopbackStream;
    }
    catch (e) {
        onError(e);
    }
}

function delayedReconnect(stream: MediaStream): void {
    if (delayedReconnectTimeout)
        clearTimeout(delayedReconnectTimeout);

    delayedReconnectTimeout = self.setTimeout(() => {
        delayedReconnectTimeout = null;
        console.warn("enableChromiumAec: recreate RTCPeerConnection loopback "
            + "because the local connection was disconnected for 10s");
        enableChromiumAec(stream);
    }, 10000);
}

let isAecWorkaroundNeededCached: boolean | null = null;

/** Chromium browsers don't apply echoCancellation to a Web Audio pipeline */
export function isAecWorkaroundNeeded(): boolean {
    const force = self["forceEchoCancellation"];
    if (force !== null && force !== undefined)
        return force;
    if (isAecWorkaroundNeededCached !== null)
        return isAecWorkaroundNeededCached;
    const navigatorUAData: { mobile: boolean; } = self.navigator["userAgentData"];
    // mobile phones have a good echoCancellation by default, we don't need anything to do
    if (navigatorUAData != null && navigatorUAData.mobile != null && navigatorUAData.mobile === true) {
        isAecWorkaroundNeededCached = false;
        return isAecWorkaroundNeededCached;
    }
    const isChromium = window.navigator.userAgent.indexOf('Chrome') !== -1;
    if (!isChromium) {
        isAecWorkaroundNeededCached = false;
        return isAecWorkaroundNeededCached;
    }
    // additional checks for mobile phones + chrome, that don't support userAgentData yet
    if (/Android|Mobile|Phone|webOS|iPhone|iPad|iPod|BlackBerry/i.test(navigator.userAgent)) {
        isAecWorkaroundNeededCached = false;
        return isAecWorkaroundNeededCached;
    }
    isAecWorkaroundNeededCached = true;
    return isAecWorkaroundNeededCached;
}