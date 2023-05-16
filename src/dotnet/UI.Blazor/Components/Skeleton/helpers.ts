export function randomIntFromInterval(min, max): number {
    return Math.floor(Math.random() * (max - min + 1) + min);
}

export enum MessageWidth {
    "w-1" = 1,
    "w-2" = 2,
    "w-3" = 3,
    "w-4" = 4,
    "w-5" = 5,
    "w-6" = 6,
    "w-7" = 7,
    "w-8" = 8,
    "w-9" = 9,
    "w-10" = 10,
}
