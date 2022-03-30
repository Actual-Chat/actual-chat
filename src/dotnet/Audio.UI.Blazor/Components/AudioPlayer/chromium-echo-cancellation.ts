import { isAudioContext } from 'audio-context-pool';

const LogScope: string = 'ChromiumEchoCancellation'

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

const onChromiumAecError = (e: any) => {
    console.error(`${LogScope}: RTCPeerConnection loopback initialization error:`, e);
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
 * @returns {Promise<() => void>} cleanup function.
 */
export async function enableChromiumAec(stream: MediaStream): Promise<() => void> {
    const audioElement = new Audio();
    audioElement.muted = true;
    audioElement.setAttribute('playsinline', 'playsinline');
    audioElement.autoplay = false;
    audioElement.muted = false;
    const outboundPeerConnection = new RTCPeerConnection();
    /** loopback connection */
    const inboundPeerConnection = new RTCPeerConnection();

    outboundPeerConnection.onicecandidate = (e) => e.candidate
        && inboundPeerConnection.addIceCandidate(e.candidate).catch(onChromiumAecError);
    // prevents memory leaks with RTCPeerConnection's
    const cleanup = (): void => {
        audioElement.muted = true;
        audioElement.pause();
        audioElement.currentTime = 0;
        audioElement.srcObject = null;
        audioElement.remove();
        closeSilently(inboundPeerConnection);
        closeSilently(outboundPeerConnection);
    };
    const outboundOnIceConnectionStateChange = () => {
        if (outboundPeerConnection.iceConnectionState === 'disconnected') {
            delayedReconnect(cleanup, stream);
        }
        if (outboundPeerConnection.iceConnectionState === 'connected') {
            if (delayedReconnectTimeout) {
                // The RTCPeerConnection reconnected by itself, cancel recreating the local connection.
                clearTimeout(delayedReconnectTimeout);
            }
        }
    };
    outboundPeerConnection.addEventListener('iceconnectionstatechange', outboundOnIceConnectionStateChange);

    inboundPeerConnection.onicecandidate = (e) => e.candidate
        && outboundPeerConnection.addIceCandidate(e.candidate).catch(onChromiumAecError);

    const inboundOnIceConnectionStateChange = () => {
        if (inboundPeerConnection.iceConnectionState === 'disconnected') {
            delayedReconnect(cleanup, stream);
        }
        if (inboundPeerConnection.iceConnectionState === 'connected') {
            if (delayedReconnectTimeout) {
                // The RTCPeerConnection reconnected by itself, cancel recreating the local connection.
                clearTimeout(delayedReconnectTimeout);
            }
        }
    };
    inboundPeerConnection.addEventListener('iceconnectionstatechange', inboundOnIceConnectionStateChange);

    try {
        inboundPeerConnection.ontrack = (e) => {
            audioElement.srcObject = e.streams[0];
            audioElement.muted = false;
            // TODO: work with rights & rejects of playing
            void audioElement.play();
        };

        // setup the loopback
        const tracks: RTCRtpSender[] = [];
        stream.getTracks().forEach((track) => {
            tracks.push(outboundPeerConnection.addTrack(track, stream));
        });

        const offer = await outboundPeerConnection.createOffer(offerOptions);
        await outboundPeerConnection.setLocalDescription(offer);

        await inboundPeerConnection.setRemoteDescription(offer);
        const answer = await inboundPeerConnection.createAnswer();
        // we can rewrite answer's SDP here to apply better bitrate, see 'sdp-transform' package
        await inboundPeerConnection.setLocalDescription(answer);
        await outboundPeerConnection.setRemoteDescription(answer);

        const onStop = (): void => {
            console.warn(`${LogScope}: onStop`);
            inboundPeerConnection.removeEventListener('iceconnectionstatechange', inboundOnIceConnectionStateChange);
            outboundPeerConnection.removeEventListener('iceconnectionstatechange', outboundOnIceConnectionStateChange);
            tracks.forEach(track => outboundPeerConnection.removeTrack(track));
            cleanup();
        };

        return onStop;
    }
    catch (e) {
        onChromiumAecError(e);
    }
}

function closeSilently(connection: RTCPeerConnection) {
    try {
        connection.onicecandidate = null;
        connection.onconnectionstatechange = null;
        connection.ondatachannel = null;
        connection.onicecandidateerror = null;
        connection.oniceconnectionstatechange = null;
        connection.onicegatheringstatechange = null;
        connection.onnegotiationneeded = null;
        connection.onsignalingstatechange = null;
        connection.ontrack = null;
        connection.close();
    }
    catch (error) {
        console.error(`${LogScope}: can't close peer connection, error:`, error, ', connection:', connection);
    }
}

function delayedReconnect(cleanup: () => void, stream: MediaStream): void {
    if (delayedReconnectTimeout)
        clearTimeout(delayedReconnectTimeout);

    delayedReconnectTimeout = self.setTimeout(() => {
        delayedReconnectTimeout = null;
        console.warn(`${LogScope}: recreate RTCPeerConnection loopback `
            + `because the local connection was disconnected for 10s`);
        cleanup();
        void enableChromiumAec(stream);
    }, 10000);
}

let isAecWorkaroundNeededCached: boolean | null = null;

/** Chromium browsers don't apply echoCancellation to a Web Audio pipeline */
export function isAecWorkaroundNeeded(): boolean {
    const force = self['forceEchoCancellation'] as boolean;
    if (force !== null && force !== undefined)
        return force;
    if (isAecWorkaroundNeededCached !== null)
        return isAecWorkaroundNeededCached;
    const navigatorUAData: { mobile: boolean; } = self.navigator['userAgentData'] as { mobile: boolean; };
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
