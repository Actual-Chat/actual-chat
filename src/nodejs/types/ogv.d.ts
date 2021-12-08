declare module "ogv" {
    class OGVPlayer extends HTMLAudioElement { }
    class OGVLoader {
        constructor(debug: boolean, debugFilter?: RegExp);
        static base: string;
    }
    class OGVCompat {
        hasWebAudio(): boolean;
        hasWebAssembly(): boolean;
        supported(component: string): boolean;
    }
}