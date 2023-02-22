
export function getVersionedArtifactPath(artifactPath: string): string {
    // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
    const artifactVersionsMap = globalThis.App?.artifactVersions as Map<string, string>;
    if (!artifactVersionsMap)
        return artifactPath;

    const version = artifactVersionsMap.get(artifactPath);
    if (!version)
        return artifactPath;

    return artifactPath + '?v=' + version;
}
