import { DeviceInfo } from 'device-info';
import { Log } from 'logging';
import { PromiseSource } from 'promises';
import { Disposable } from 'disposable';

const { infoLog, errorLog } = Log.get('WebRtcAec');

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

export let isWebRtcAecRequired = DeviceInfo.isAndroid && DeviceInfo.isChromium;

/**
 * Routes audio through Chrome's audio mixer (via a loopback WebRTC connection),
 * thus enabling Acoustic Echo Cancellation (AEC) while preserving all the audio processing with WebAudio API.
 * DOESN'T WORK WITH `AudioContext.destination`!
 * playing with Audio tag is necessary as workaround of
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=933677}
 * {@link https://bugs.chromium.org/p/chromium/issues/detail?id=687574}
 */
export async function createWebRtcAecStream(stream: MediaStream): Promise<MediaStream & Disposable> {
    if (!isWebRtcAecRequired)
        return;

    infoLog?.log(`createWebRtcAecStream(), stream:`, stream);
    let outboundConnection = new RTCPeerConnection();
    let inboundConnection = new RTCPeerConnection();
    const tracks = stream.getTracks();

    const closeConnection = (connection?: RTCPeerConnection) => {
        if (connection == null)
            return;

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
            errorLog?.log(`closeConnection failed:`, error, ', connection:', connection);
        }
    }

    try {
        inboundConnection.onicecandidate = (e) => e.candidate && outboundConnection.addIceCandidate(e.candidate);
        const whenLoopbackStreamReady = new PromiseSource<MediaStream>()
        inboundConnection.ontrack = (e) => whenLoopbackStreamReady.resolve(e.streams[0]);

        outboundConnection.onicecandidate = (e) => e.candidate && inboundConnection.addIceCandidate(e.candidate);
        tracks.forEach((track) => outboundConnection.addTrack(track, stream));

        const offer = await outboundConnection.createOffer(offerOptions);
        await outboundConnection.setLocalDescription(offer);
        await inboundConnection.setRemoteDescription(offer);
        const answer = await inboundConnection.createAnswer();
        await inboundConnection.setLocalDescription(answer);
        await outboundConnection.setRemoteDescription(answer);
        const loopbackStream = await whenLoopbackStreamReady as MediaStream & Disposable;
        loopbackStream['dispose'] = () => {
            closeConnection(inboundConnection);
            inboundConnection = null;
            closeConnection(outboundConnection);
            outboundConnection = null;
        };
        return loopbackStream;
    }
    catch (e) {
        errorLog?.log(`createWebRtcAecStream() failed:`, e);
    }
}
