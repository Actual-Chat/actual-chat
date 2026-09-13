# MAUI media asset cache

Status: first foundation implemented on `codex/maui-content-cache`; tracked by
[#4512](https://github.com/Actual-Chat/actual-chat/issues/4512).

## This branch

Extract a MAUI-independent content-handler pipeline into `ActualChat.ContentCaching`.
A null result leaves handling to the native platform; a non-null result owns a response
stream. Logging, HTTP fetching, and filesystem caching compose through the same contract.

MAUI calls only the logging handler in this phase, leaving media loading unchanged.
Android observes intercepted requests; Windows observes image/media requests; Apple observes
the existing `content://` handler. Remote HTTPS on Apple will need URL projection before
it can reach this pipeline. Native response adaptation and key/path wiring come later.

The first filesystem implementation stores complete GETs for immutable media with known
lengths up to 1 MiB, configurable. Headers/ranges, larger files, unknown lengths, and responses
with private/no-store/vary/cookie state bypass persistence and retain their downstream streams.
The full representation URL identifies each cache entry by default. A configurable URL
normalizer can remove CDN-specific signing parameters from cache identity while retaining
content variants; downloading still uses the original signed URL. Header/cookie-based
identity is not implemented in this phase.

Use one directory level of 256 buckets for the expected maximum of 100K files: the first
SHA-256 byte as two lowercase hex characters, then the full Base64Url hash as the filename.
Hash the normalized URL as UTF-8. Encrypted staging uses `.p`; completed files have no extension.

Coordinate fills by absolute file path within the process, including across handler instances.
After waiting, recheck the cache and return a separate response. Waiter cancellation is isolated;
a failed/canceled owner or failed persistence allows a retry. Large/ranged bypasses are unchanged.

Encrypt metadata and payload together using AES-256-GCM with a fresh nonce per write,
a cache-specific HKDF key, and authenticated cache identity. Publish by atomic rename only
after a complete download. Corrupt entries become misses; unavailable storage does not
prevent serving downloaded bytes. Record access time and physical file size for later eviction.
No Kvasar index is needed for this version.

## Next decisions

**URL projection:** keep canonical remote URLs distinct from WebView-facing routes. Apply
projection after constructing image-proxy/resize URLs, and use app-issued opaque references
as `LocalContentRegistry` does. Include avatars, transformed previews, thumbnails, and
audio/video/file attachments. Native download/share consumers retain canonical URLs or open
the cache directly. Arbitrary external content requires a versioned URL before caching.

**Progressive storage:** replace the whole-entry envelope with independently authenticated
chunks, including asset identity, position, and generation. Never persist plaintext staging
files. Readers consume completed chunks while later chunks download; seeking fetches missing
ranges directly. Coalesce overlapping downloads, isolate reader cancellation, and retain
valid completed chunks after interruption. Transport/RPC message size must not dictate disk
chunk size or playback startup latency.

**Platform delivery:** Android can return a progressive stream. Apple can incrementally call
[`WKURLSchemeTask.didReceive`](https://developer.apple.com/documentation/webkit/wkurlschemetask/didreceive%28_%3A%29-8t5f8);
stopping a request must cancel its reader and prevent later native callbacks. Windows needs a
loopback response adapter: the [WebView2 response-content contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourceresponse.content)
requires complete response data when the deferral ends. Validate loopback origin/CORS and
capability routing before enabling it. Preserve existing local-upload preview handling.

**Persistence and access:** use app-owned persistent storage excluded from backup. Reuse
`MauiPreferences.DbEncryptionKey` as the root key, retaining its current Preferences-backed
protection model. Keep session/access invalidation distinct from LRU retention. The eventual
background eviction worker uses last access and total physical size; it is outside this work.

**RPC source:** add an offset/length-capable RPC downloader that identifies the same original
or transformed representation as HTTP. Keep CDN reachability and fallback selection above
the fetchers. Do not combine bytes until range responses and representation identity agree.

## Reuse

**Existing abstractions:** use `FilePath`, `AsyncLockSet<FilePath>`, and the existing SHA-256 /
Base64Url helpers from Core/Fusion; BCL `HttpRequestMessage`/`HttpResponseMessage` and
`HttpClient.ResponseHeadersRead` for the streaming HTTP boundary; BCL `AesGcm` and `HKDF`
for encryption with the same cipher family as the database cache. Existing native WebView
hooks supply the observation points. Fusion's `FileSystemCache` is a text key/value store;
it does not provide the encrypted response/stream contract. No existing abstraction fits
the complete pipeline.

Later reuse `UrlMapper`/`UrlMapperExt`, `LocalContentRegistry`, and
`MauiPreferences.DbEncryptionKey`. A separate `KvasarKvas` catalog and Kvasar page cipher
are options once sparse downloads and retention justify them, rather than dependencies of
the first file format.

**Placement:** the contract, logging decorator, HTTP fetcher, and file cache are reusable
outside MAUI. Both Core and a dedicated shared project are suitable; prefer
`ActualChat.ContentCaching` referencing Core to keep the subsystem independently testable
and its dependencies explicit. Keep native adapters and lifecycle/key wiring in App.Maui.
Put future media URL projection beside the shared mapper and enable it only for MAUI.
WebAssembly and new TypeScript infrastructure are outside scope.

## Verification boundary

This branch tests encrypted restart hits, identity isolation, corrupted/torn entries,
coordinated publication, cancellation, stream ownership, hash buckets, stale partials, and
streaming bypass behavior.
Enabling media routing requires separate device tests for offline images, throttled video
startup, seeking into an uncached tail, and cancellation on each native platform.
