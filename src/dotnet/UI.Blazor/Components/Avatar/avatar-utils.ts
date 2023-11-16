export const hashCode = (name: string): number => {
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
        const character = name.charCodeAt(i);
        hash = ((hash << 5) - hash) + character;
        hash = hash & hash; // Convert to 32bit integer
    }
    return Math.abs(hash);
};

export const mod = (num: number, max: number): number => {
    return num % max;
};

export const getDigit = (number: number, index: number): number => {
    return Math.floor((number / Math.pow(10, index)) % 10);
};

export const getBoolDigit = (number: number, index: number): boolean => {
    return (!((getDigit(number, index)) % 2));
};

export const getAngle = (x: number, y: number): number => {
    return Math.atan2(y, x) * 180 / Math.PI;
};

export const getUnit = (number: number, range: number, index: number): number => {
    let value = number % range;

    if (index && ((getDigit(number, index) % 2) === 0)) {
        return -value;
    } else {
        return value;
    }
};

export const getRandomColor = (number: number, colors: string[], range: number): string => {
    return colors[(number) % range];
};

export const getContrast = (hexColor: string): string => {

    // If a leading # is provided, remove it
    if (hexColor.slice(0, 1) === '#') {
        hexColor = hexColor.slice(1);
    }

    // Convert to RGB value
    const r = parseInt(hexColor.substring(0, 2), 16);
    const g = parseInt(hexColor.substring(2, 4), 16);
    const b = parseInt(hexColor.substring(4, 6), 16);

    // Get YIQ ratio
    const yiq = ((r * 299) + (g * 587) + (b * 114)) / 1000;

    // Check contrast
    return (yiq >= 128) ? '#000000' : '#FFFFFF';
};
