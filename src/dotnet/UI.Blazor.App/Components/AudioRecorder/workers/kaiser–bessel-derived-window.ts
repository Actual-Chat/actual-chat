const BESSEL_I0_ITER = 50;

export function KaiserBesselDerivedWindow(windowSize: number, alpha: number): Float32Array {
    const window = new Float32Array(windowSize);
    const alpha2 = (alpha * Math.PI / windowSize) * (alpha * Math.PI / windowSize);

    let sum = 0.0;
    for (let i = 0; i < windowSize; i++) {
        const tmp = i * (windowSize - i) * alpha2;
        let bessel = 1.0;
        for (let j = BESSEL_I0_ITER; j > 0; j--)
            bessel = bessel * tmp / (j * j) + 1;
        sum += bessel;
        window[i] = sum;
    }
    sum++;
    for (let i = 0; i < windowSize; i++)
        window[i] = Math.sqrt(window[i] / sum);

    return window;
}
