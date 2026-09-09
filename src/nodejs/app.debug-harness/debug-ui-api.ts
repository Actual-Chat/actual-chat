// Types only: a page.evaluate body is serialized and cannot call back into this
// module, so every helper here would be unreachable from inside the page.

/** The `window.debugUI` surface this harness relies on, as seen from inside the page. */
export interface DebugUIApi {
    signIn(phoneOrEmail: string, opts?: SignInOptions): Promise<void>;
    signOut(): Promise<void>;
    getUserId(): Promise<string>;
    setRenderMode(mode: 'a' | 's' | 'w'): Promise<void>;
    fake: {
        send(text: string): Promise<void>;
        attach(files: DebugAttachment[]): Promise<void>;
    };
}

export interface SignInOptions {
    register?: boolean;
    skipOnboarding?: boolean;
    skipBubbles?: boolean;
}

export interface DebugAttachment {
    name: string;
    type: string;
    base64: string;
}

export interface DebugUIRoot {
    debugUI?: DebugUIApi;
}
