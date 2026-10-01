import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('device-info', () => ({ DeviceInfo: { isMobile: true, isIos: false } }));
vi.mock('orientation', () => ({ DeviceOrientation: { quarter: 0 } }));

import { MediaCapture } from '../../../src/dotnet/UI.Blazor.App/Services/Video/services/media-capture';

type GetUserMedia = (constraints: { video: MediaTrackConstraints }) => Promise<MediaStream>;

const KnownDeviceId = 'known-camera';
const StaleDeviceId = 'stale-camera';

function overconstrained(constraint: string): Error {
    return Object.assign(new Error('Overconstrained'), { name: 'OverconstrainedError', constraint });
}

function streamOf(deviceId: string): MediaStream {
    const track = { getSettings: () => ({ deviceId, width: 720, height: 1280 }), stop: vi.fn() };
    return { getVideoTracks: () => [track] } as unknown as MediaStream;
}

function requestedDeviceId(constraints: MediaTrackConstraints): string | undefined {
    return (constraints.deviceId as ConstrainDOMStringParameters | undefined)?.exact as string | undefined;
}

describe('MediaCapture.captureCameraStream', () => {
    let getUserMedia: ReturnType<typeof vi.fn<GetUserMedia>>;

    beforeEach(() => {
        getUserMedia = vi.fn<GetUserMedia>();
        vi.stubGlobal('navigator', { mediaDevices: { getUserMedia } });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('falls back to the default camera when the requested device id is unknown', async () => {
        // arrange
        getUserMedia.mockImplementation(({ video }) => requestedDeviceId(video)
            ? Promise.reject(overconstrained('deviceId'))
            : Promise.resolve(streamOf(KnownDeviceId)));

        // act
        const track = await MediaCapture.captureCameraStream({ deviceId: StaleDeviceId });

        // assert
        expect(track.getSettings().deviceId).toBe(KnownDeviceId);
        expect(getUserMedia).toHaveBeenCalledTimes(2);
    });

    it('falls back when the browser does not name the failed constraint', async () => {
        // arrange
        getUserMedia.mockImplementation(({ video }) => requestedDeviceId(video)
            ? Promise.reject(overconstrained(''))
            : Promise.resolve(streamOf(KnownDeviceId)));

        // act
        const track = await MediaCapture.captureCameraStream({ deviceId: StaleDeviceId });

        // assert
        expect(track.getSettings().deviceId).toBe(KnownDeviceId);
    });

    it('keeps the requested camera when only the size constraints fail', async () => {
        // arrange
        getUserMedia.mockImplementation(({ video }) => video.width && 'min' in (video.width as object)
            ? Promise.reject(overconstrained('width'))
            : Promise.resolve(streamOf(requestedDeviceId(video) ?? 'default-camera')));

        // act
        const track = await MediaCapture.captureCameraStream({ deviceId: KnownDeviceId });

        // assert
        expect(track.getSettings().deviceId).toBe(KnownDeviceId);
    });

    it('rethrows errors that are not about the device id', async () => {
        // arrange
        const error = Object.assign(new Error('Denied'), { name: 'NotAllowedError' });
        getUserMedia.mockRejectedValue(error);

        // act
        const whenCaptured = MediaCapture.captureCameraStream({ deviceId: StaleDeviceId });

        // assert
        await expect(whenCaptured).rejects.toBe(error);
    });
});
