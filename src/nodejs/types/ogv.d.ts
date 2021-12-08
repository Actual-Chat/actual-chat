declare module "ogv" {
    interface OGVPlayerOptions {
        /** base URL for additional resources, such as codec libraries */
        base?: string;
        /** Running the codec in a worker thread */
        worker?: boolean;
        /** Experimental SIMD mode, if built. */
        simd?: boolean;
        /** Experimental pthreads multithreading mode, if built. */
        threading?: boolean;
        debug?: boolean;
        debugFilter?: RegExp;
        video?: HTMLVideoElement | boolean;
        /** Allows replacement StreamFile instance with a compatible implementation */
        stream?: any; //StreamFile;
        /** pre-created AudioContext */
        audioContext?: AudioContext;
        /** pre-created output node */
        audioDestination?: MediaStreamAudioDestinationNode;
        /** Allows setting a custom backend for audioFeeder */
        audioBackendFactory?: any;
    }

    class OGVPlayer extends HTMLAudioElement {
        constructor(options?: OGVPlayerOptions);
    }
    class OGVLoader {
        constructor();
        base: string;
        urlForClass(className: string): string;
        urlForScript(scriptName: string): string;
        loadClass(className: string, callback: Function, options?: any);
    }
    class OGVCompat {
        hasWebAudio(): boolean;
        hasWebAssembly(): boolean;
        supported(component: string): boolean;
    }
}