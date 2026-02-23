export interface IUploadStreamSource {
    getBlob(): Blob;
}

export interface IFileUpload {
    cancel(): void;
}
