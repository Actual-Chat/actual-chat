export interface VadMessage {
    type: 'create' | 'init' | 'reset';
}

export interface CreateVadMessage extends VadMessage {
    type: 'create';
    artifactVersions: Map<string,string>;
}
