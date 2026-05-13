import { describe, it, expect } from 'vitest';
import {
    quantize,
    iosCameraRotationDeg,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/orientation/quantize';

describe('quantize', () => {
    it.each<[number, 0 | 1 | 2 | 3]>([
        [0, 0],
        [44, 0],
        [45, 1],
        [89, 1],
        [90, 1],
        [91, 1],
        [134, 1],
        [135, 2],
        [179, 2],
        [180, 2],
        [225, 3],
        [269, 3],
        [270, 3],
        [315, 0],
        [360, 0],
        [-1, 0],
        [-45, 0],
        [-90, 3],
    ])('quantize(%i) = %i', (deg, expected) => {
        expect(quantize(deg)).toBe(expected);
    });

    it('non-finite → 0', () => {
        expect(quantize(NaN)).toBe(0);
        expect(quantize(Infinity)).toBe(0);
    });
});

describe('iosCameraRotationDeg', () => {
    // From WebRTC RTCCameraVideoCapturer.m (see plan).
    it.each<[number, boolean, number]>([
        [0, true, 90],
        [90, true, 180],
        [180, true, 270],
        [270, true, 0],
        [0, false, 90],
        [90, false, 0],
        [180, false, 270],
        [270, false, 180],
    ])('angle=%i front=%s → %i', (angle, isFront, expected) => {
        expect(iosCameraRotationDeg(angle, isFront)).toBe(expected);
    });

    it('handles negative/360+ angles', () => {
        expect(iosCameraRotationDeg(-90, true)).toBe(iosCameraRotationDeg(270, true));
        expect(iosCameraRotationDeg(450, false)).toBe(iosCameraRotationDeg(90, false));
    });
});
