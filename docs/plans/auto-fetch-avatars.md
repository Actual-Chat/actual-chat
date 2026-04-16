# Implementation Plan: Auto-Fetch User Profile Picture Flow

## Overview
A new `ProfilePictureFlow` runs per user, triggered on every sign-in. It fetches candidate profile pictures from Gravatar and Google, evaluates them against quality criteria, and sets the best one as the user's avatar — only if they still have the default avatar.

---

## 1. Trigger Point

**Where:** `AccountsBackend.OnSignIn` in `src/dotnet/Users.Service/AccountsBackend.cs` — at the end of both the "new account" and "existing account update" branches.

**How:** Schedule a flow resume via `FlowHub.NewResumeEvent<ProfilePictureFlow>(userId).Schedule(ct)`.

**Why triggering on every sign-in:** Users may add a new email identity between sign-ins (e.g., verify an additional email after their Google account). On each sign-in, the flow checks if there's new work to do.

---

## 2. New Flow: `ProfilePictureFlow`

**Location:** `src/dotnet/Users.Service/Flows/ProfilePictureFlow.cs`

**Attribute config:**
```csharp
[Flow(ResumeTimeout = 60, DataVersion = 1)]
```
- `ResumeTimeout = 60` → each resume runs up to 1 minute (enough for one HTTP fetch + image processing).

**Base class:** Inherits from `Flow<Unit>` (not `PeriodicFlow` — we don't want recurring auto-runs; we're event-driven).

**Persisted state:**
```csharp
[DataMember(Order = 0), MemoryPackOrder(0)]
public AvatarCandidate? Best { get; set; }

[DataMember(Order = 1), MemoryPackOrder(1)]
public ApiArray<string> ProcessedSources { get; set; }
// entries like "Gravatar|user@x.com" or "Google|user@x.com"

[DataMember(Order = 2), MemoryPackOrder(2)]
public bool IsFinished { get; set; }
// Sticky flag — set once we terminate; prevents new sign-ins from re-activating.
```

**New value type: `AvatarCandidate`**
**Location:** `src/dotnet/Users.Service/Flows/AvatarCandidate.cs`

```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record AvatarCandidate(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Source,      // "Gravatar" | "Google"
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Email,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] string ContentType, // "image/jpeg" etc.
    [property: DataMember(Order = 3), MemoryPackOrder(3)] int Width,
    [property: DataMember(Order = 4), MemoryPackOrder(4)] int Height,
    [property: DataMember(Order = 5), MemoryPackOrder(5)] int SizeBytes,
    [property: DataMember(Order = 6), MemoryPackOrder(6)] byte[]? InlineData, // set if <= 64 KB
    [property: DataMember(Order = 7), MemoryPackOrder(7)] string? BlobId      // set if > 64 KB (temp blob storage)
)
{
    public int MinDimension => Math.Min(Width, Height);
    public int MaxDimension => Math.Max(Width, Height);
}
```

Inline-vs-blob decision: if `SizeBytes <= 65_536` → inline; otherwise store to blob storage under a temp path like `tmp-avatar-candidates/{userId}/{randomId}.{ext}` and keep `BlobId` in the candidate.

---

## 3. Flow Algorithm (one `Resume()` call)

```
if IsFinished → SetResult(Unit); return   // belt & suspenders

account = AccountsBackend.Get(userId)
if account is null → SetResult(Unit); return

if !IsDefaultAvatar(account.Avatar):
    Console.Log("Avatar is non-default; terminating")
    CleanupBlob(Best)       // delete any pending temp blob
    IsFinished = true
    SetResult(Unit); return

emails = account.Identities.GetEmails().Distinct()
nextJob = FindNextUnprocessed(emails, ProcessedSources)
    // ordered by: Google first (if we have a picture URL claim), then Gravatar,
    // iterating over every email.

if nextJob is null:
    // Nothing left to try.
    if Best is not null and PassesAllCriteria(Best):
        await ApplyAsAvatar(Best)     // via MediaProcessor + MediaSaver, same as upload
        Console.Log("Applied candidate from {Source} ({Width}x{Height})")
    else:
        Console.Log("No candidate passed quality criteria; keeping default avatar")
    CleanupBlob(Best)
    IsFinished = true
    SetResult(Unit); return

try:
    candidate = await Fetch(nextJob)   // Gravatar or Google
catch RateLimited:
    Console.LogWarning("Rate-limited by {Source}; backing off 1h")
    Runtime.StageResumeIn(TimeSpan.FromHours(1))
    return    // do NOT mark as processed; retry same job
catch other Exception:
    Console.LogWarning("Fetch failed: {ex}")
    ProcessedSources = ProcessedSources.Add(nextJob.Key)
    Runtime.StageResume()   // try next immediately
    return

ProcessedSources = ProcessedSources.Add(nextJob.Key)

if candidate is null or !PassesAllCriteria(candidate):
    Console.Log("Rejected {Source}/{Email}: reason")
    Runtime.StageResume()
    return

if Best is null or candidate.MinDimension > Best.MinDimension:
    Console.Log("New best from {Source}: {W}x{H}")
    CleanupBlob(Best)
    Best = candidate
else:
    CleanupBlob(candidate)

Runtime.StageResume()   // try next source/email
```

**Final application of avatar:** When applying `Best`, materialize bytes (from `InlineData` or blob storage), then run through `IMediaProcessor.ProcessUpload(..., MediaKind.UserAvatarPicture, ...)` and `IMediaSaver.Save(...)` — **the same path as `AvatarPicturesController.UploadPicture`**. Then call `AvatarsBackend_Change` to update the avatar's `MediaId`. A final safety check inside `ApplyAsAvatar` re-fetches the account and bails out if the avatar is no longer default (avoids race with a concurrent user upload).

---

## 4. "Default Avatar" Detection

**Helper:** `static bool IsDefaultAvatar(AvatarFull avatar) => avatar.MediaId.IsNone && avatar.PictureUrl.IsNullOrEmpty();`

Rationale: the seeded default avatar has only `AvatarKey` set (see `AccountsBackend.cs:504`). Any custom upload sets `MediaId`; any external picture sets `PictureUrl`.

---

## 5. Source Fetchers

**Common interface:**
```csharp
internal interface IProfilePictureFetcher
{
    string Source { get; }
    bool CanFetch(AccountFull account, string email);
    Task<AvatarCandidate?> Fetch(AccountFull account, string email, CancellationToken ct);
}
```

Two implementations:

### 5a. `GravatarFetcher`
- URL: `https://gravatar.com/avatar/{sha256(email.Trim().ToLower())}?s=512&d=404`
  (Gravatar accepts SHA-256 today; avoids MD5.)
- `d=404` → returns 404 if no custom image (filters out Gravatar's default geometric placeholders).
- Treat HTTP 429 as `RateLimitedException`.

### 5b. `GoogleFetcher`
- Reads picture URL from `account.Claims["urn:google:picture"]` (see §6).
- Downloads that URL.
- Can also upgrade the URL: Google picture URLs typically support `=s512` size hint appended (e.g., `.../photo.jpg=s512-c`).
- If claim is missing, `CanFetch` returns false.
- Treat HTTP 429 as `RateLimitedException`.

Both fetchers:
- Use `IHttpClientFactory` with a named client (timeout 10s).
- Read response stream into memory, cap at e.g. 5 MB.
- Decode via ImageSharp to learn `Width`, `Height`, `ContentType`.
- Decide inline (≤ 64 KB) vs blob storage and construct `AvatarCandidate`.

---

## 6. Google Profile Picture Claim — Auth Adjustments

The `profile` scope is already included by default in `AddGoogle()`, so the `userinfo` endpoint response contains a `picture` field — we just need to capture it.

**Change 1 — Web/Native Google auth** (`src/dotnet/Users.Service/Module/UsersServiceModule.cs`):

In `authentication.AddGoogle(options => { ... })`, add:
```csharp
options.ClaimActions.MapJsonKey("urn:google:picture", "picture", "url");
```

This makes `ClaimMapper` (`NativeAuthController.SignInGoogle` at line 118-120 iterates `options.ClaimActions`) capture the picture URL as a claim.

**Change 2 — Native Android** (`src/dotnet/App.Maui/Platforms/Android/NativeGoogleAuth.cs`):

Verify that the native sign-in still sends an authorization code that the server exchanges for a token with access to the userinfo endpoint (it does, based on `NativeAuthController.SignInGoogle`). No code-side change needed in Android — the same server-side claim action applies.

**Change 3 — Claim is stored in `account.Claims`**:

The existing flow already copies claims into `AccountFull.Claims` during sign-in. Verify that `urn:google:picture` is preserved (spot-check `ClaimMapper` behavior). If not, extend it to include this claim name.

**Note:** This also means the picture URL is available right at first sign-in for Google users — no extra token storage or OAuth scope changes needed.

---

## 7. Quality Criteria (pass/fail)

All checks done on the decoded image:

1. **Min dimension ≥ 80 px** (`Math.Min(W, H) >= 80`)
2. **Aspect ratio close to square:** `MaxDim / MinDim <= 1.5` — allows 80×120 but rejects 80×200. (You said "2:3 or better", which is 1.5.)
3. **File size ≥ 5 KB** (`SizeBytes >= 5_120`)
4. **Entropy check:** compute Shannon entropy on a downsampled grayscale (e.g., ImageSharp resize to 32×32). Threshold TBD — start with `entropy >= 4.0 bits`. Rejects uniform-color or near-uniform default avatars.

**Selection:** among all passing candidates, the winner is the one with largest `MinDimension` (tie-break: larger `SizeBytes`).

Helper lives in `src/dotnet/Users.Service/Flows/ProfilePictureQuality.cs`.

---

## 8. Temp Blob Storage

**Where:** Reuse `IBlobStorages` with scope `BlobScope.UploadTempRecord` (already exists for uploads).

**Path convention:** `profile-picture-candidates/{userId}/{randomId}.{ext}`

**Cleanup:** any time we discard a candidate (superseded, rejected, or after applying), call `blobStorage.Delete(path)`. On flow termination, ensure `Best`'s blob is also cleaned up after `ApplyAsAvatar`.

---

## 9. Applying the Avatar

Inside `ApplyAsAvatar(AvatarCandidate best)`:

1. Re-fetch account; abort if avatar no longer default.
2. Load bytes: `best.InlineData` or read from blob via `IBlobStorages.Get(scope).Read(best.BlobId!)`.
3. Construct an `UploadedFile` (the same type `AvatarPicturesController` uses).
4. `mediaId = MediaId.New(userId.Value)`
5. `processed = await MediaProcessor.ProcessUpload(uploadedFile, MediaKind.UserAvatarPicture, null, ct)`
6. `mediaRef = await MediaSaver.Save(mediaId, processed, isUpdate: false, MediaKind.UserAvatarPicture, ct)`
7. Load current `AvatarFull` via `AvatarsBackend.Get(avatar.Id)`.
8. Build updated `AvatarFull` with `MediaId = mediaRef.MediaId`.
9. Call `AvatarsBackend_Change(avatar.Id, currentVersion, Change.Update(updated))`.
10. Clean up temp blob.

This matches exactly what a user upload does — so thumbnail generation and other processing run identically.

---

## 10. Logging

All logs go through `Console.Log*`:
- `Console.LogSection` at major decision points.
- Info-level when a new best is chosen (`"New best from Google: 512x512 (82KB)"`).
- Warning-level on fetch failures and rate limits.
- Info-level when final avatar is applied (`"Applied avatar from Gravatar: 400x400"`).
- Info-level when terminating without a change (`"No candidate passed; keeping default avatar"`).

Flow console is persisted (max 8 KB) and visible for debugging.

---

## 11. Files to Add

| File | Purpose |
|------|---------|
| `src/dotnet/Users.Service/Flows/ProfilePictureFlow.cs` | Flow class + algorithm |
| `src/dotnet/Users.Service/Flows/AvatarCandidate.cs` | Record for candidate state |
| `src/dotnet/Users.Service/Flows/ProfilePictureQuality.cs` | Pass/fail criteria + entropy |
| `src/dotnet/Users.Service/Flows/IProfilePictureFetcher.cs` | Common fetcher interface |
| `src/dotnet/Users.Service/Flows/GravatarFetcher.cs` | Gravatar implementation |
| `src/dotnet/Users.Service/Flows/GoogleProfilePictureFetcher.cs` | Google implementation |
| `src/dotnet/Users.Service/Flows/ProfilePictureFlowHelpers.cs` | `IsDefaultAvatar`, blob helpers, `ApplyAsAvatar` |

## 12. Files to Modify

| File | Change |
|------|--------|
| `src/dotnet/Users.Service/Module/UsersServiceModule.cs` | Register flow with `AddFlows().Add<ProfilePictureFlow>()`; add Google `picture` claim action; register fetchers + named `HttpClient` |
| `src/dotnet/Users.Service/AccountsBackend.cs` | In `OnSignIn` (both new + existing paths), schedule `FlowHub.NewResumeEvent<ProfilePictureFlow>(userId.Value).Schedule(...)` |
| `src/dotnet/Users.Service/ClaimMapper.cs` | (If needed) preserve `urn:google:picture` in `AccountFull.Claims` |

---

## 13. Edge Cases Handled

- **No email identities:** Flow immediately finishes (nothing to process).
- **User changes avatar mid-flow:** Next resume sees non-default avatar and terminates cleanly, cleaning up temp blob.
- **Race on final apply:** Re-check inside `ApplyAsAvatar`.
- **Rate limiting:** Detected via HTTP 429, triggers 1-hour back-off without marking source as processed.
- **Partial `IsFinished` on old flow instance when user adds new email:** Currently `IsFinished = true` makes the flow never resume again. This is a deliberate trade-off — once done, done. If the user wants a retry later, they can upload manually. (Open question: do you want the flow to un-finish when a *new unprocessed* email appears? Say so and I'll make `IsFinished` re-evaluate on resume.)

---

## 14. Open Questions / Risks

1. **Flow restart on new email after finishing:** as noted above, should we re-open the flow if a new email appears post-termination? Current design says no (simpler), but easy to flip.
2. **Entropy threshold tuning:** `4.0 bits` is a starting guess. May need calibration against real Gravatar defaults / Google default avatars.
3. **Google picture URL stability:** Google may return cached CDN URLs that expire. We fetch immediately on the same resume, so this should be fine.
4. **`urn:google:picture` claim persistence:** Need to verify `ClaimMapper` doesn't filter it out. If it does, tiny extension needed.
