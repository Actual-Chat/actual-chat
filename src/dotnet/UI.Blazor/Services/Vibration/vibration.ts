export class Vibration {
    public static vibrate(periodMs: number = 100): void {
        const canVibrate = ('vibrate' in navigator);
        if (!canVibrate)
            return;
        window.navigator.vibrate(periodMs);
    }
}
