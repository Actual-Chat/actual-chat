import { normalizeRotationQuarter, type RotationQuarter } from 'orientation';

export type { RotationQuarter } from 'orientation';

// Feed device pose (deviceQuarter * 90), not screen.orientation.angle —
// the device-pose value is correct even when the OS rotation lock is on.
// Assumes a 90° sensor offset (true for typical iOS and Android phones).
export function cameraRotationDeg(
    deviceAngle: number,
    isFrontCamera: boolean,
): number {
    const normalized = (((deviceAngle % 360) + 360) % 360);
    return isFrontCamera
        ? (90 + normalized) % 360
        : (90 - normalized + 360) % 360;
}

// Backwards-compat alias for the wire helper.
export function quantize(degrees: number): RotationQuarter {
    return normalizeRotationQuarter(degrees / 90);
}
