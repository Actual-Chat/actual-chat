export class Vibration {
    public static vibrate(durationMs: number = 100): void {
        const canVibrate = ('vibrate' in navigator);
        if (!canVibrate)
            return;
        window.navigator.vibrate(durationMs);
    }
}
