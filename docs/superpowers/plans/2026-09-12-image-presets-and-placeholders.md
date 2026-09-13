# Image presets, deferred upload and inline placeholders — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the preset ladder with pixel budgets, defer uploads until the draft is committed, ship a tiny jpegli placeholder inside every image's media row, and fix the defects device testing found.

**Architecture:** The existing client pipeline (jpegli WASM in a module worker, driven from Blazor through `ImageProcessingInterop`) keeps its shape. Presets become a pixel budget plus a long-side cap instead of a single max dimension; the worker gains a second output kind for the 64 px placeholder; the placeholder travels base64 in the media row's metadata bag and is painted as the first layer of `image-skeleton`. Upload timing moves from "when processing ends" to "when the draft commits".

**Tech Stack:** TypeScript (worker, `src/nodejs/src/image-processing/`), C# (Blazor UI, `src/dotnet/UI.Blazor.App/`), ImageSharp (server, identify only), jpegli WASM (already shipped), vitest + xUnit.

**Spec:** [docs/superpowers/specs/2026-09-12-image-presets-and-placeholders-design.md](../specs/2026-09-12-image-presets-and-placeholders-design.md)

## Global Constraints

- Preset budgets: `L² × ¾` pixels, long side `≤ 1.5 × L`, for L = 8192, 4096, 1920 — i.e. **50 331 648 px / 12288**, **12 582 912 px / 6144**, **2 764 800 px / 2880**. Default is the 4096 row.
- Menu labels: `Up to 50mpx / 12K`, `Up to 12mpx / 6K`, `Up to 3mpx / 3K`, plus `Original (with EXIF)`.
- Nothing is ever upscaled; an image inside both limits is re-encoded at its own size.
- Server bounds: **12288 px per side, 96 000 000 px total**. A source past them uploads as a **file attachment**, never a rejection.
- Placeholder: jpegli distance 6, 4:2:0, long side exactly 64 px, aspect clamped to 2:1, stored as `[format byte][signed short-side byte][payload]` base64 in `Media.Metadata`.
- Size estimates are computed, never encoded, and displayed rounded (0.1 MB below 1 MB, coarser above).
- GIF, animated WebP and APNG are never re-encoded, at any preset.
- `docs/CODING_STYLE.md` governs all C#/TS: no `Async` suffix, no new `///` on members, minimal comments, mixed brace style, control-flow statements on their own line.
- Validation: `npm run build:Verify`, `npm run test:unit`, `dotnet build ActualChat.CI.slnf`, plus the per-task test commands.
- Branch `feat/media-quality-selector`. Don't push; don't run `/prepare-merge`.

---

## File structure

| File | Responsibility |
|---|---|
| `src/nodejs/src/image-processing/image-geometry.ts` | `fitWithin` (long side) **and** new `fitWithinBudget` (pixels + long side) |
| `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageSizeEstimator.cs` | **new** — predicts encoded size without encoding |
| `src/nodejs/src/image-processing/placeholder-encoder.ts` | **new** — 64 px jpegli encode + container packing |
| `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImagePlaceholder.cs` | **new** — container → `data:` URL on the render path |
| `src/nodejs/src/image-processing/image-processor-worker.ts` | budget-aware resize, placeholder output, encoder rebuild on trap |
| `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageQualityPreset.cs` | the ladder, budgets, labels |
| `src/dotnet/UI.Blazor.App/Services/FileUploads/UploadSessions.cs` | removes the media a discarded session reserved |
| `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/FileAttachments.cs` | commit-based upload timing |
| `src/dotnet/UI.Blazor/Components/Skeleton/image-skeleton.lit.ts` | placeholder layer beneath thumbnail/image |
| `src/dotnet/Core.Server/Uploads/AttachmentImageUploadProcessor.cs` | oversize → binary file |
| `src/dotnet/Api/Constants.cs` | server bounds |

---

## Task 1: Pixel-budget geometry

**Files:**
- Modify: `src/nodejs/src/image-processing/image-geometry.ts`
- Test: `tests/ts/unit/image-geometry.test.ts`

**Interfaces:**
- Produces: `fitWithinBudget(width: number, height: number, maxPixels: number | null, maxLongSide: number | null): ImageSize` — scales down so that `width*height <= maxPixels` **and** `max(width,height) <= maxLongSide`, preserving aspect, never upscaling.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ts/unit/image-geometry.test.ts`:

```ts
describe('fitWithinBudget', () => {
    it('should leave an image inside both limits untouched', () => {
        // Arrange / Act
        const size = fitWithinBudget(4032, 3024, 12582912, 6144);
        // Assert
        expect(size).toEqual({ width: 4032, height: 3024 });
    });

    it('should scale down to the pixel budget', () => {
        const size = fitWithinBudget(16320, 12240, 50331648, 12288);
        expect(size.width * size.height).toBeLessThanOrEqual(50331648);
        expect(Math.abs(size.width / size.height - 16320 / 12240)).toBeLessThan(0.01);
    });

    it('should let a wide image spend its budget on width up to the long-side cap', () => {
        const size = fitWithinBudget(30000, 5000, 50331648, 12288);
        expect(size.width).toBe(12288);
        expect(size.height).toBe(2048);
    });

    it('should apply the long-side cap even when the pixel budget is not reached', () => {
        const size = fitWithinBudget(20000, 1000, 50331648, 12288);
        expect(size.width).toBe(12288);
    });

    it('should never upscale', () => {
        const size = fitWithinBudget(800, 600, 50331648, 12288);
        expect(size).toEqual({ width: 800, height: 600 });
    });

    it('should treat null limits as unbounded', () => {
        const size = fitWithinBudget(9000, 9000, null, null);
        expect(size).toEqual({ width: 9000, height: 9000 });
    });
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run tests/ts/unit/image-geometry.test.ts`
Expected: FAIL — `fitWithinBudget is not a function`.

- [ ] **Step 3: Implement**

Append to `src/nodejs/src/image-processing/image-geometry.ts`:

```ts
export function fitWithinBudget(
    width: number,
    height: number,
    maxPixels: number | null,
    maxLongSide: number | null,
): ImageSize {
    const pixelScale = maxPixels === null ? 1 : Math.sqrt(maxPixels / (width * height));
    const longSide = Math.max(width, height);
    const sideScale = maxLongSide === null ? 1 : maxLongSide / longSide;
    const scale = Math.min(1, pixelScale, sideScale);
    if (scale >= 1)
        return { width, height };

    return {
        width: Math.max(1, Math.floor(width * scale)),
        height: Math.max(1, Math.floor(height * scale)),
    };
}
```

`floor`, not `round`: rounding up can push the result back over the pixel budget the caller
just asked us to respect.

- [ ] **Step 4: Run the tests**

Run: `npx vitest run tests/ts/unit/image-geometry.test.ts`
Expected: PASS, including the existing `fitWithin` tests.

- [ ] **Step 5: Commit**

```bash
git add src/nodejs/src/image-processing/image-geometry.ts tests/ts/unit/image-geometry.test.ts
git commit -m "feat(image-processing): fit an image to a pixel budget and a long-side cap"
```

---

## Task 2: The preset ladder

**Files:**
- Modify: `src/nodejs/src/image-processing/image-processing-contracts.ts`, `src/nodejs/src/image-processing/image-processor-worker.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageQualityPreset.cs`, `ImageProcessRequest.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs`

**Interfaces:**
- Consumes: `fitWithinBudget` (Task 1).
- Produces: `ImageOutputSpec` gains `maxPixels: number | null` (TS) / `int? MaxPixels` (C#); `ImageQualityPreset` becomes `Mpx50 = 0, Mpx12, Mpx3, Original, OriginalWithExif` with `GetBudget()` returning `(int? MaxPixels, int? MaxLongSide)`.

- [ ] **Step 1: Write the failing C# test**

Replace the body of `tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs` with:

```csharp
public class ImageQualityPresetTest
{
    [Fact]
    public void DefaultPresetShouldBe12Mpx()
        => default(ImageQualityPreset).Should().Be(ImageQualityPreset.Mpx12);

    [Theory]
    [InlineData(ImageQualityPreset.Mpx50, 50_331_648, 12288)]
    [InlineData(ImageQualityPreset.Mpx12, 12_582_912, 6144)]
    [InlineData(ImageQualityPreset.Mpx3, 2_764_800, 2880)]
    public void ReEncodingPresetsShouldCarryTheirBudget(ImageQualityPreset preset, int maxPixels, int maxLongSide)
    {
        var budget = preset.GetBudget();
        budget.MaxPixels.Should().Be(maxPixels);
        budget.MaxLongSide.Should().Be(maxLongSide);
    }

    [Theory]
    [InlineData(ImageQualityPreset.Original)]
    [InlineData(ImageQualityPreset.OriginalWithExif)]
    public void OriginalPresetsShouldHaveNoBudget(ImageQualityPreset preset)
    {
        var budget = preset.GetBudget();
        budget.MaxPixels.Should().BeNull();
        budget.MaxLongSide.Should().BeNull();
    }

    [Fact]
    public void OriginalWithExifShouldKeepMetadata()
        => ImageQualityPreset.OriginalWithExif.ToRequest().Outputs[0].StripMetadata.Should().BeFalse();

    [Fact]
    public void OriginalShouldStripMetadataWithoutReEncoding()
    {
        var spec = ImageQualityPreset.Original.ToRequest().Outputs[0];
        spec.StripMetadata.Should().BeTrue();
        spec.Codec.Should().Be("passthrough");
    }

    [Fact]
    public void ReEncodingPresetShouldRequestMainAndPlaceholder()
    {
        var outputs = ImageQualityPreset.Mpx12.ToRequest().Outputs;
        outputs.Select(o => o.Kind).Should().Contain("main");
        outputs.Select(o => o.Kind).Should().Contain("placeholder");
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageQualityPresetTest"`
Expected: FAIL — `Mpx50` does not exist.

- [ ] **Step 3: Rewrite the preset type**

`src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageQualityPreset.cs` — full file:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public enum ImageQualityPreset
{
    Mpx12 = 0,
    Mpx50,
    Mpx3,
    Original,
    OriginalWithExif,
}

public readonly record struct ImageQualityBudget(int? MaxPixels, int? MaxLongSide);

public static class ImageQualityPresetExt
{
    public static ImageQualityBudget GetBudget(this ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Mpx50 => new(50_331_648, 12288),
            ImageQualityPreset.Mpx12 => new(12_582_912, 6144),
            ImageQualityPreset.Mpx3 => new(2_764_800, 2880),
            _ => new(null, null),
        };

    public static ImageProcessRequest ToRequest(this ImageQualityPreset preset)
    {
        var placeholder = ImageOutputSpec.Placeholder();
        return preset switch {
            ImageQualityPreset.Original => new([ImageOutputSpec.Original(stripMetadata: true), placeholder]),
            ImageQualityPreset.OriginalWithExif => new([ImageOutputSpec.Original(stripMetadata: false), placeholder]),
            _ => new([ImageOutputSpec.Main(preset.GetBudget()), placeholder]),
        };
    }
}
```

The menu order (best first) is `OriginalWithExif, Original, Mpx50, Mpx12, Mpx3`; the enum
order is deliberately different so `default` is the 12 mpx row without giving it value 0
by accident in a future reorder.

- [ ] **Step 4: Update the request records**

`src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageProcessRequest.cs` — replace `ImageOutputSpec`:

```csharp
public sealed record ImageOutputSpec(
    string Kind,
    int? MaxPixels,
    int? MaxLongSide,
    string Codec,
    bool StripMetadata,
    int? MaxPassthroughPixels = null)
{
    public static ImageOutputSpec Main(ImageQualityBudget budget)
        => new("main", budget.MaxPixels, budget.MaxLongSide, "auto", true);

    public static ImageOutputSpec Original(bool stripMetadata)
        => new("main", null, null, "passthrough", stripMetadata, Constants.Attachments.MaxImagePixelCount);

    public static ImageOutputSpec Placeholder()
        => new("placeholder", null, null, "placeholder", true);
}
```

`ImageSizeEstimate` is deleted here — Task 5 replaces it with a computed estimate.

- [ ] **Step 5: Mirror the contract in TypeScript**

In `src/nodejs/src/image-processing/image-processing-contracts.ts`:

```ts
export type ImageOutputKind = 'main' | 'placeholder';
export type ImageOutputCodec = 'auto' | 'passthrough' | 'placeholder';

export interface ImageOutputSpec {
    kind: ImageOutputKind;
    maxPixels: number | null;
    maxLongSide: number | null;
    codec: ImageOutputCodec;
    stripMetadata: boolean;
    /** A passthrough output above this pixel count is re-encoded within the budget instead. */
    maxPassthroughPixels: number | null;
}
```

and in `ImageOutput`, replace `isSource` usage untouched but add:

```ts
    /** Set only on the placeholder output: the packed container bytes, base64'd. */
    placeholder?: string;
```

- [ ] **Step 6: Teach the worker the budget**

In `image-processor-worker.ts`, replace every `fitWithin(bitmap.width, bitmap.height, spec.maxSize)` call with:

```ts
const target = fitWithinBudget(bitmap.width, bitmap.height, spec.maxPixels, spec.maxLongSide);
```

and the passthrough oversize check with:

```ts
const isOversized = spec.maxPassthroughPixels !== null
    && width * height > spec.maxPassthroughPixels;
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageQualityPresetTest"` and `npm run build:Verify`
Expected: tests PASS, build clean.

- [ ] **Step 8: Commit**

```bash
git add src/nodejs/src/image-processing src/dotnet/UI.Blazor.App/Services/ImageProcessing tests/Chat.UI.Blazor.UnitTests/ImageQualityPresetTest.cs
git commit -m "feat(image-processing): pixel-budget presets"
```

---

## Task 3: Server bounds and the file-attachment fallback

**Files:**
- Modify: `src/dotnet/Api/Constants.cs:187-189`, `src/dotnet/Core.Server/Uploads/AttachmentImageUploadProcessor.cs`
- Test: `tests/Core.Server.UnitTests/Uploads/AttachmentImageUploadProcessorTest.cs`

**Interfaces:**
- Produces: `Constants.Attachments.MaxImageSize = 12288`, `MaxImagePixelCount = 96_000_000`; `AttachmentImageUploadProcessor.Process` returns `upload.AsBinaryFile()` for a source past either bound instead of throwing.

- [ ] **Step 1: Write the failing test**

Add to `tests/Core.Server.UnitTests/Uploads/AttachmentImageUploadProcessorTest.cs`:

```csharp
[Fact]
public async Task ShouldStoreAnOversizedImageAsABinaryFile()
{
    // Arrange: an image whose header claims more pixels than the server stores
    var file = TestImages.CreateJpeg(13000, 9000);
    var upload = new UploadedFile("huge.jpg", "image/jpeg", file.Length, () => new MemoryStream(file));

    // Act
    var processed = await Processor.Process(upload, null, CancellationToken.None);

    // Assert: no exception, and the result is the untouched bytes without image metadata
    processed.Size.Should().BeNull();
    processed.File.Length.Should().Be(file.Length);
}
```

`TestImages.CreateJpeg(width, height)` exists already; if it does not take dimensions, add
the overload next to `CreateJpegWithExif` in the same file.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~AttachmentImageUploadProcessorTest"`
Expected: FAIL — the processor throws a constraint error.

- [ ] **Step 3: Raise the bounds**

`src/dotnet/Api/Constants.cs`:

```csharp
        // The client re-encodes within a pixel budget; these are the outer bounds a stored image may have
        public const int MaxImageSize = 12288;
        public const long MaxImagePixelCount = 96_000_000;
```

- [ ] **Step 4: Fall back instead of rejecting**

In `AttachmentImageUploadProcessor.Process`, replace the `RequireWithinLimits` call with a check that degrades:

```csharp
        var isWithinBounds = imageInfo.Width <= Constants.Attachments.MaxImageSize
            && imageInfo.Height <= Constants.Attachments.MaxImageSize
            && (long)imageInfo.Width * imageInfo.Height <= Constants.Attachments.MaxImagePixelCount;
        if (!isWithinBounds) {
            // Storing it as a file keeps the bytes the sender chose; rejecting after Send would not
            Log.LogInformation("Image {Width}x{Height} exceeds the stored-image bounds, keeping it as a file",
                imageInfo.Width, imageInfo.Height);
            return new ProcessedFile(upload.AsBinaryFile(), null);
        }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~Uploads"`
Expected: PASS (99+ tests).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Constants.cs src/dotnet/Core.Server/Uploads/AttachmentImageUploadProcessor.cs tests/Core.Server.UnitTests/Uploads
git commit -m "feat(uploads): store an oversized image as a file instead of rejecting it"
```

---

## Task 4: Encoder hardening

**Files:**
- Modify: `src/nodejs/src/image-processing/image-processor-worker.ts`
- Test: `tests/ts/unit/jpegli-encoder.test.ts`

**Interfaces:**
- Produces: `getEncoder()` discards its cached encoder when a call throws `WebAssembly.RuntimeError`; `canEncodeOnThisDevice(pixels: number): boolean` refuses a budget the device cannot afford.

**Why:** measured behaviour — jpegli encodes up to 240 MP; past ~245 MP it throws
`WebAssembly.RuntimeError` and **the instance is poisoned** (every later encode traps),
while past ~255 MP it returns null and stays healthy. The cache never rebuilds, so one trap
silently downgrades every later image to the canvas encoder.

- [ ] **Step 1: Write the failing test**

Add to `tests/ts/unit/jpegli-encoder.test.ts`:

```ts
it('should drop a poisoned encoder so the next call rebuilds it', async () => {
    // Arrange
    const loads: number[] = [];
    const encoder = {
        encode: () => { throw new WebAssembly.RuntimeError('memory access out of bounds'); },
    };
    const factory = () => { loads.push(1); return Promise.resolve(encoder as never); };

    // Act
    const first = await tryEncodeWithRebuild(factory, () => encoder.encode());
    const second = await tryEncodeWithRebuild(factory, () => encoder.encode());

    // Assert: each attempt loaded a fresh encoder rather than reusing the trapped one
    expect(first).toBeNull();
    expect(second).toBeNull();
    expect(loads.length).toBe(2);
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run tests/ts/unit/jpegli-encoder.test.ts`
Expected: FAIL — `tryEncodeWithRebuild is not exported`.

- [ ] **Step 3: Implement the rebuild rule**

In `image-processor-worker.ts`, export a helper and use it where the encoder is called:

```ts
export async function tryEncodeWithRebuild<T>(
    load: () => Promise<JpegliEncoder | null>,
    encode: (encoder: JpegliEncoder) => T,
): Promise<T | null> {
    const encoder = await load();
    if (!encoder)
        return null;

    try {
        return encode(encoder);
    }
    catch (e) {
        // A wasm trap poisons the instance: every later encode on it traps too, so it must go.
        // A plain Error is jpegli refusing the image, and leaves the instance usable.
        if (e instanceof WebAssembly.RuntimeError)
            whenEncoderLoaded = null;
        errorLog?.log('encode failed', e);
        return null;
    }
}
```

- [ ] **Step 4: Add the device budget guard**

Same file:

```ts
const MAX_ENCODE_PIXELS_MOBILE = 16_000_000;

export function canEncodeOnThisDevice(pixels: number): boolean
    // jpegli needs ~8.8 bytes of wasm heap per pixel, on top of the bitmap and the canvas copy;
    // a phone that runs out does not throw, the OS kills the app
    => !DeviceInfo.isMobile || pixels <= MAX_ENCODE_PIXELS_MOBILE;
```

Check the real flag name in `src/nodejs/src/device-info.ts` and use it; if no mobile flag
exists, derive one from `navigator.userAgentData?.mobile ?? /Android|iPhone|iPad/.test(navigator.userAgent)`.

In `reencode`, before decoding: when `!canEncodeOnThisDevice(targetPixels)`, return the
passthrough output instead, so the attachment still sends at its source size.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run tests/ts/unit/jpegli-encoder.test.ts` and `npm run build:Verify`
Expected: PASS, build clean.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/src/image-processing/image-processor-worker.ts tests/ts/unit/jpegli-encoder.test.ts
git commit -m "fix(image-processing): rebuild a poisoned encoder, skip encodes a phone cannot afford"
```

---

## Task 5: Size estimator

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageSizeEstimator.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/ImageSizeEstimatorTest.cs`

**Interfaces:**
- Consumes: `ImageQualityBudget` (Task 2).
- Produces: `ImageSizeEstimator.Estimate(long sourceBytes, Size2D sourceSize, string contentType, ImageQualityBudget budget): long` and `ImageSizeEstimator.Format(long bytes): string`.

**Why these constants:** fitted over 1200 real photos from four cameras, 41 fitted / 25 held out (`tmp/size-model/`). Holdout error is ~20% median overall and ~34% on phone photos — which is why `Format` rounds hard. Never encode to produce an estimate.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ImageSizeEstimatorTest
{
    private static readonly Size2D Phone12Mp = new(4032, 3024);

    [Fact]
    public void ShouldPredictADownscaleWithinTheModelsErrorBand()
    {
        // A 12 MP phone JPEG of ~3.5 MB downscaled to 2.8 MP measures around 600-900 KB
        var estimate = ImageSizeEstimator.Estimate(3_500_000, Phone12Mp, "image/jpeg", new(2_764_800, 2880));
        estimate.Should().BeInRange(300_000, 1_500_000);
    }

    [Fact]
    public void ShouldNeverExceedTheSourceWhenNothingIsResized()
    {
        var estimate = ImageSizeEstimator.Estimate(3_500_000, Phone12Mp, "image/jpeg", new(50_331_648, 12288));
        estimate.Should().BeLessThanOrEqualTo(3_500_000);
    }

    [Fact]
    public void ShouldCorrectForAMoreEfficientSourceFormat()
    {
        var jpeg = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/jpeg", new(2_764_800, 2880));
        var heic = ImageSizeEstimator.Estimate(2_000_000, Phone12Mp, "image/heic", new(2_764_800, 2880));
        // The same byte count in HEIC means a busier photo, so the JPEG we produce is bigger
        heic.Should().BeGreaterThan(jpeg);
    }

    [Fact]
    public void ShouldIgnoreSourceBytesForLosslessSources()
    {
        var small = ImageSizeEstimator.Estimate(500_000, Phone12Mp, "image/png", new(2_764_800, 2880));
        var large = ImageSizeEstimator.Estimate(50_000_000, Phone12Mp, "image/png", new(2_764_800, 2880));
        small.Should().Be(large);
    }

    [Theory]
    [InlineData(412_000, "~0.4 MB")]
    [InlineData(1_600_000, "~1.5 MB")]
    [InlineData(12_400_000, "~12 MB")]
    public void ShouldFormatCoarsely(long bytes, string expected)
        => ImageSizeEstimator.Format(bytes).Should().Be(expected);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageSizeEstimatorTest"`
Expected: FAIL — the type does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class ImageSizeEstimator
{
    private const double ResizeK = 2.427;
    private const double ResizeA = 0.816;
    private const double ResizeC = 0.393;
    private const double RecodeK = 0.1314;
    private const double RecodeC = -0.574;
    private const double PixelsOnlyK = 1.654;
    private const double PixelsOnlyA = 0.811;

    public static long Estimate(long sourceBytes, Size2D sourceSize, string contentType, ImageQualityBudget budget)
    {
        var sourcePixels = (double)sourceSize.Width * sourceSize.Height;
        if (sourcePixels <= 0)
            return sourceBytes;

        var target = FitWithinBudget(sourceSize, budget);
        var targetPixels = (double)target.Width * target.Height;
        if (!TryGetFormatMultiplier(contentType, out var multiplier))
            // A lossless source's byte size says more about its encoder than its content
            return (long)(PixelsOnlyK * Math.Pow(targetPixels, PixelsOnlyA));

        var bpp = sourceBytes / sourcePixels * multiplier;
        var estimate = targetPixels >= sourcePixels
            ? RecodeK * sourceBytes * Math.Pow(bpp, RecodeC)
            : ResizeK * Math.Pow(targetPixels, ResizeA) * Math.Pow(bpp, ResizeC);
        return (long)Math.Min(estimate, sourceBytes);
    }

    public static string Format(long bytes)
    {
        var mb = bytes / 1_000_000.0;
        var rounded = mb switch {
            < 1 => Math.Round(mb, 1),
            < 10 => Math.Round(mb * 2, MidpointRounding.AwayFromZero) / 2,
            _ => Math.Round(mb),
        };
        return $"~{rounded:0.#} MB";
    }

    // Private methods

    private static bool TryGetFormatMultiplier(string contentType, out double multiplier)
    {
        multiplier = contentType switch {
            "image/jpeg" or "image/jpg" => 1,
            "image/webp" => 3.52,
            "image/heic" or "image/heif" or "image/avif" => 5.27,
            _ => 0,
        };
        return multiplier > 0;
    }

    private static Size2D FitWithinBudget(Size2D size, ImageQualityBudget budget)
    {
        var pixelScale = budget.MaxPixels is { } maxPixels
            ? Math.Sqrt(maxPixels / ((double)size.Width * size.Height))
            : 1;
        var sideScale = budget.MaxLongSide is { } maxLongSide
            ? maxLongSide / (double)Math.Max(size.Width, size.Height)
            : 1;
        var scale = Math.Min(1, Math.Min(pixelScale, sideScale));
        return scale >= 1
            ? size
            : new Size2D(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImageSizeEstimatorTest"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImageSizeEstimator.cs tests/Chat.UI.Blazor.UnitTests/ImageSizeEstimatorTest.cs
git commit -m "feat(attachments): estimate upload size without encoding"
```

---

## Task 6: Placeholder encoding and its container

**Files:**
- Create: `src/nodejs/src/image-processing/placeholder-encoder.ts`
- Test: `tests/ts/unit/placeholder-encoder.test.ts`

**Interfaces:**
- Consumes: `JpegliEncoder` (existing), `fitWithinBudget` (Task 1).
- Produces: `encodePlaceholder(bitmap, encoder): Uint8Array`, `placeholderSize(width, height): ImageSize`, and the constants `PLACEHOLDER_LONG_SIDE = 64`, `PLACEHOLDER_DISTANCE = 6`, `PLACEHOLDER_FORMAT_STRIPPED = 1`, `PLACEHOLDER_FORMAT_FULL = 2`, `PLACEHOLDER_PREFIX: Uint8Array`.

**Container:** byte 0 is the format mark, byte 1 is the side that is not 64 as a signed value (positive means it is the width, negative the height), bytes 2.. are the payload. Format 1 drops the reconstructable JPEG prefix — SOI, DQT, the SOF0 skeleton and the SOS header, measured at 236 bytes and byte-identical across images at a fixed distance. Format 2 carries a whole JPEG, for any case where the encoder's prefix does not match the template.

- [ ] **Step 1: Write the failing tests**

```ts
describe('placeholder container', () => {
    it('should encode a landscape image with a negative short-side byte', async () => {
        // Arrange: a 128x96 source becomes a 64x48 placeholder
        const bitmap = await createTestBitmap(128, 96);
        // Act
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        // Assert
        expect(packed[0]).toBe(PLACEHOLDER_FORMAT_STRIPPED);
        expect(new Int8Array(packed.buffer, 1, 1)[0]).toBe(-48);
        expect(packed.length).toBeLessThan(1024);
    });

    it('should encode a portrait image with a positive short-side byte', async () => {
        const bitmap = await createTestBitmap(96, 128);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        expect(new Int8Array(packed.buffer, 1, 1)[0]).toBe(48);
    });

    it('should clamp an extreme aspect ratio to 2:1', async () => {
        const bitmap = await createTestBitmap(1000, 100);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        expect(Math.abs(new Int8Array(packed.buffer, 1, 1)[0])).toBe(PLACEHOLDER_LONG_SIDE / 2);
    });

    it('should stay well inside the byte budget on a photo', async () => {
        const bitmap = await createPhotoBitmap(1200, 900);
        const packed = encodePlaceholder(bitmap, await loadEncoder());
        // Measured median is ~413 bytes across 15 real photos
        expect(packed.length).toBeLessThan(900);
    });
});
```

`createTestBitmap` and `createPhotoBitmap` are local helpers building an `ImageBitmap` from an `OffscreenCanvas` — a flat fill for the first, a noisy gradient for the second, so the size assertion means something.

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run tests/ts/unit/placeholder-encoder.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```ts
import type { JpegliEncoder } from './jpegli-encoder';
import type { ImageSize } from './image-geometry';

export const PLACEHOLDER_LONG_SIDE = 64;
export const PLACEHOLDER_DISTANCE = 6;
export const PLACEHOLDER_FORMAT_STRIPPED = 1;
export const PLACEHOLDER_FORMAT_FULL = 2;

export function placeholderSize(width: number, height: number): ImageSize {
    const longSide = PLACEHOLDER_LONG_SIDE;
    // The 2:1 clamp keeps a panorama from degenerating into a line
    const shortSide = Math.max(
        Math.round(longSide / 2),
        Math.min(longSide, Math.round(longSide * Math.min(width, height) / Math.max(width, height))));
    return width >= height
        ? { width: longSide, height: shortSide }
        : { width: shortSide, height: longSide };
}

export function encodePlaceholder(bitmap: ImageBitmap, encoder: JpegliEncoder): Uint8Array {
    const size = placeholderSize(bitmap.width, bitmap.height);
    const canvas = new OffscreenCanvas(size.width, size.height);
    const context = canvas.getContext('2d')!;
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, size.width, size.height);
    const image = context.getImageData(0, 0, size.width, size.height);
    const jpeg = encoder.encode(image.data, size.width, size.height, { distance: PLACEHOLDER_DISTANCE });
    const prefixLength = matchesPrefix(jpeg) ? PLACEHOLDER_PREFIX.length : 0;
    const payload = jpeg.subarray(prefixLength);
    const packed = new Uint8Array(2 + payload.length);
    packed[0] = prefixLength > 0 ? PLACEHOLDER_FORMAT_STRIPPED : PLACEHOLDER_FORMAT_FULL;
    new Int8Array(packed.buffer, 1, 1)[0] = size.width < size.height ? size.width : -size.height;
    packed.set(payload, 2);
    return packed;
}
```

`matchesPrefix(jpeg)` compares the leading `PLACEHOLDER_PREFIX.length` bytes, skipping the four SOF0 bytes that hold height and width — those legitimately differ per image and the decoder rebuilds them from byte 1.

- [ ] **Step 4: Capture the prefix constant**

The prefix is a compile-time constant, not something the reader can derive — a client that has never encoded anything still has to decode. Add a test that prints it once:

```ts
it('prints the prefix template (run when PLACEHOLDER_DISTANCE changes)', async () => {
    const bitmap = await createTestBitmap(128, 96);
    const jpeg = (await loadEncoder()).encode(/* … 64x48 RGBA … */);
    console.log('PLACEHOLDER_PREFIX =', Array.from(jpeg.subarray(0, findScanStart(jpeg))));
    expect(true).toBe(true);
});
```

Paste the printed bytes into `placeholder-encoder.ts` as `export const PLACEHOLDER_PREFIX = new Uint8Array([...]);`, and write above it, in a comment: this constant and `PLACEHOLDER_DISTANCE` change together, and changing either needs a **new format mark**, never an edit — stored rows were packed against the old one.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run tests/ts/unit/placeholder-encoder.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/src/image-processing/placeholder-encoder.ts tests/ts/unit/placeholder-encoder.test.ts
git commit -m "feat(image-processing): encode a 64px placeholder into a compact container"
```

---

## Task 7: Placeholder decoding and rendering

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImagePlaceholder.cs`
- Modify: `src/dotnet/UI.Blazor/Components/Skeleton/image-skeleton.lit.ts`, `src/dotnet/UI.Blazor/Components/Skeleton/skeleton.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Attachment/VisualMediaAttachment.razor`, `src/dotnet/UI.Blazor.App/Components/ContentList/VisualMediaList.razor`
- Test: `tests/Chat.UI.Blazor.UnitTests/ImagePlaceholderTest.cs`

**Interfaces:**
- Consumes: the container from Task 6.
- Produces: `ImagePlaceholder.ToDataUrl(string? packedBase64): string` — the rebuilt `data:image/jpeg;base64,…` URL, or `""` for absent/unknown input; `image-skeleton` gains a `placeholderSrc` property.

**Why C#, not the TS decoder:** the render path must not await JS interop for every tile. The rebuild is a byte-array splice, so it costs nothing in C#.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ImagePlaceholderTest
{
    [Fact]
    public void ShouldReturnEmptyForMissingInput()
    {
        ImagePlaceholder.ToDataUrl(null).Should().BeEmpty();
        ImagePlaceholder.ToDataUrl("").Should().BeEmpty();
    }

    [Fact]
    public void ShouldReturnEmptyForAnUnknownFormatMark()
    {
        var packed = Convert.ToBase64String(new byte[] { 99, 32, 1, 2, 3 });
        ImagePlaceholder.ToDataUrl(packed).Should().BeEmpty();
    }

    [Fact]
    public void ShouldRebuildAStrippedPlaceholderIntoAJpegDataUrl()
    {
        var packed = Convert.ToBase64String(new byte[] { 1, unchecked((byte)-48), 0xFF, 0xC4, 0x00, 0x02 });
        var url = ImagePlaceholder.ToDataUrl(packed);
        url.Should().StartWith("data:image/jpeg;base64,");
        var bytes = Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]);
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8); // SOI, i.e. the prefix really was prepended
    }

    [Fact]
    public void ShouldPassAFullJpegThrough()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var packed = Convert.ToBase64String(new byte[] { 2, 64 }.Concat(jpeg).ToArray());
        var url = ImagePlaceholder.ToDataUrl(packed);
        Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]).Should().Equal(jpeg);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImagePlaceholderTest"`
Expected: FAIL — the type does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace ActualChat.UI.Blazor.App.Services;

public static class ImagePlaceholder
{
    private const byte FormatStripped = 1;
    private const byte FormatFull = 2;
    private const int LongSide = 64;
    private const string DataUrlPrefix = "data:image/jpeg;base64,";

    // Must stay byte-identical to PLACEHOLDER_PREFIX in placeholder-encoder.ts;
    // a change there needs a new format mark, not an edit here
    private static readonly byte[] Prefix = [/* pasted from the TS constant */];
    private static readonly int SofHeightOffset = FindSofDimensionOffset(Prefix);

    public static string ToDataUrl(string? packedBase64)
    {
        if (packedBase64.IsNullOrEmpty())
            return "";

        byte[] packed;
        try {
            packed = Convert.FromBase64String(packedBase64);
        }
        catch (FormatException) {
            return "";
        }
        if (packed.Length < 3)
            return "";

        var payload = packed.AsSpan(2);
        if (packed[0] == FormatFull)
            return DataUrlPrefix + Convert.ToBase64String(payload);
        if (packed[0] != FormatStripped)
            return "";

        var shortSide = unchecked((sbyte)packed[1]);
        var width = shortSide > 0 ? shortSide : LongSide;
        var height = shortSide > 0 ? LongSide : -shortSide;
        var jpeg = new byte[Prefix.Length + payload.Length];
        Prefix.CopyTo(jpeg, 0);
        payload.CopyTo(jpeg.AsSpan(Prefix.Length));
        WriteBigEndian(jpeg, SofHeightOffset, (ushort)height);
        WriteBigEndian(jpeg, SofHeightOffset + 2, (ushort)width);
        return DataUrlPrefix + Convert.ToBase64String(jpeg);
    }
}
```

`FindSofDimensionOffset` walks the prefix to the SOF0 marker (`0xFFC0`) and returns the offset of its height field — five bytes past the marker. `WriteBigEndian` writes a 16-bit value. Both are private helpers in the same file.

- [ ] **Step 4: Add the media accessor**

In `src/dotnet/Api/Media/Media.cs`, beside `Width`/`Height`:

```csharp
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Placeholder {
        get => this.GetMetadataValue("");
        init => this.SetMetadataValue(value);
    }
```

No migration and no serializer change: the bag is an opaque JSON column and old rows simply return `""`.

- [ ] **Step 5: Render it**

`image-skeleton.lit.ts` — add `@property() placeholderSrc: string;` and render it as the first `<img>` in **both** branches of `render()`:

```ts
                <img
                    part='image-placeholder'
                    class='image-placeholder'
                    draggable='false'
                    alt=''
                    .src='${this.placeholderSrc}'
                />
```

`skeleton.css`, next to the existing `data-image-state` rules:

```css
image-skeleton .image-placeholder {
    @apply absolute inset-0 w-full h-full;
    object-fit: cover;
    filter: blur(12px);
    transform: scale(1.1);
}
image-skeleton[data-image-state="original"] .image-placeholder {
    @apply hidden;
}
```

The blur hides jpegli's 8×8 block edges, which are visible on flat sky at this size; `scale(1.1)` keeps the blur from revealing the tile's edges.

In `VisualMediaAttachment.razor` and `VisualMediaList.razor`, pass it:

```razor
    var placeholderUrl = ImagePlaceholder.ToDataUrl(attachment.Media.Placeholder);
```

and add `placeholderSrc="@placeholderUrl"` to the `<image-skeleton>` element.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~ImagePlaceholderTest"` and `npm run build:Verify`
Expected: PASS, build clean.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ImageProcessing/ImagePlaceholder.cs src/dotnet/Api/Media/Media.cs src/dotnet/UI.Blazor/Components/Skeleton src/dotnet/UI.Blazor.App/Components tests/Chat.UI.Blazor.UnitTests/ImagePlaceholderTest.cs
git commit -m "feat(chat): paint a blurred placeholder while an image loads"
```

---

## Task 8: Generate and store the placeholder

**Files:**
- Modify: `src/nodejs/src/image-processing/image-processor-worker.ts`, `src/dotnet/UI.Blazor.App/Services/FileProviders/image-processing-interop.ts`
- Modify: `src/dotnet/UI.Blazor.App/Services/ImageProcessing/ProcessedImage.cs`, `ImageAttachmentProcessor.cs`
- Modify: `src/dotnet/UI.Blazor.App/Components/Attachment/Attachment.cs`
- Test: `tests/ts/unit/image-processor-worker.test.ts`

**Interfaces:**
- Consumes: `encodePlaceholder` (Task 6), the `placeholder` output kind (Task 2).
- Produces: `ImageProcessingResult.Placeholder` (string, base64, `""` when none); `Attachment.Placeholder`; the placeholder key in `GetMetadataForUploadSession`.

- [ ] **Step 1: Emit the placeholder from the worker**

In `image-processor-worker.ts`, when the request contains a `placeholder` output, encode it from the **same decoded bitmap** the main output uses — one decode, two outputs:

```ts
        if (spec.kind === 'placeholder') {
            const encoder = await getEncoder();
            const packed = encoder ? encodePlaceholder(bitmap, encoder) : null;
            outputs.push({
                kind: 'placeholder', blob: new Blob(), mimeType: '', width: 0, height: 0, isSource: false,
                placeholder: packed ? toBase64(packed) : '',
            });
            continue;
        }
```

For a passthrough main output there is no bitmap, so decode a small one for the placeholder alone: `createImageBitmap(source, { resizeWidth: 64, resizeQuality: 'high' })` — cheap, and it is what makes GIF and Original placeholders possible at all.

- [ ] **Step 2: Carry it to .NET**

`image-processing-interop.ts` — include `placeholder` in the returned info object. `ProcessedImage.cs` — add `string Placeholder` to `ProcessedImage` and `ImageProcessingResult`. `ImageAttachmentProcessor.cs` — copy it into the result it builds.

- [ ] **Step 3: Store it on the attachment and the media row**

`Attachment.cs` — add `public string Placeholder { get; init; } = "";` and set it in `GetMetadataForUploadSession`:

```csharp
        if (!Placeholder.IsNullOrEmpty())
            metadata = metadata.Set(nameof(Media.Media.Placeholder), Placeholder);
```

- [ ] **Step 4: Write the failing worker test**

```ts
it('should return a placeholder alongside the main output from one decode', async () => {
    // Arrange
    const source = await makeJpegBlob(800, 600);
    // Act
    const result = await processInWorker(source, {
        outputs: [mainSpec(12582912, 6144), placeholderSpec()],
    });
    // Assert
    const placeholder = result.outputs.find(o => o.kind === 'placeholder');
    expect(placeholder?.placeholder).toBeTruthy();
    expect(atob(placeholder!.placeholder!).length).toBeLessThan(1024);
});
```

- [ ] **Step 5: Run the tests**

Run: `npx vitest run tests/ts/unit/image-processor-worker.test.ts` and `npm run build:Verify`
Expected: PASS, build clean.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/src/image-processing src/dotnet/UI.Blazor.App tests/ts/unit
git commit -m "feat(attachments): ship a placeholder with every uploaded image"
```

---

## Task 9: Deferred upload

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/FileAttachments.cs`, `AttachmentList.cs`, `ChatMessageEditor.razor`
- Test: `tests/Chat.UI.Blazor.UnitTests/AttachmentListTest.cs`

**Interfaces:**
- Consumes: `ImageQualityPreset` (Task 2).
- Produces: `AttachmentList.IsCommitted` and `AttachmentList.Commit()`; `FileAttachments.CommitDraft(AttachmentList)` — idempotent, starts processing and upload for every attachment the list holds.

**The rule:** attaching uploads nothing. The draft commits on the first of typing, picking a preset, or Send. From then on that list encodes and uploads immediately, including attachments added later. Posting resets the list, so the next draft starts uncommitted.

- [ ] **Step 1: Write the failing test**

```csharp
public class AttachmentListTest
{
    [Fact]
    public void ShouldStartUncommitted()
        => new AttachmentList().IsCommitted.Should().BeFalse();

    [Fact]
    public void CommitShouldBeIdempotentAndRaiseOnce()
    {
        // Arrange
        var list = new AttachmentList();
        var commits = 0;
        list.Committed += () => commits++;

        // Act
        list.Commit();
        list.Commit();

        // Assert
        list.IsCommitted.Should().BeTrue();
        commits.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AttachmentListTest"`
Expected: FAIL — `IsCommitted` does not exist.

- [ ] **Step 3: Add the commit flag**

In `AttachmentList.cs`:

```csharp
    public bool IsCommitted { get; private set; }
    public event Action? Committed;

    public void Commit()
    {
        if (IsCommitted)
            return;

        IsCommitted = true;
        Committed?.Invoke();
    }
```

- [ ] **Step 4: Stop uploading on attach**

In `FileAttachments.AddAttachment`, replace the unconditional start with a commit check:

```csharp
        SetSourcePreview(attachment);
        list.Add(attachment);
        if (list.IsCommitted)
            _ = StartImageProcessing(list, attachment.Id, list.ImageQuality, null, isReprocess: false)
                .WithErrorLog(Log, "Failed to process attachment '{AttachmentId}'", attachment.Id)
                .SilentAwait();
```

A non-processable attachment (a file, a GIF) follows the same rule: `StartUpload` only runs once the list is committed.

- [ ] **Step 5: Start everything on commit**

In `FileAttachments`, subscribe to the list and process what is already there:

```csharp
    public Task CommitDraft(AttachmentList list)
    {
        list.Commit();
        var tasks = list.Items
            .Where(a => a.UploadSessionId.IsNullOrEmpty())
            .Select(a => a.IsProcessableImage
                ? StartImageProcessing(list, a.Id, list.ImageQuality, null, isReprocess: false)
                : StartUploadOnly(list, a.Id))
            .ToList();
        return Task.WhenAll(tasks);
    }
```

`StartUploadOnly` is the existing non-image path extracted into its own method, so both callers share it.

- [ ] **Step 6: Wire the three triggers**

In `ChatMessageEditor.razor`:
- the editor's text-changed handler calls `FileAttachments.CommitDraft(Attachments)` on the first non-empty input — guard with `if (!Attachments.IsCommitted)` so typing costs nothing afterwards;
- `OnImageQualityChanged` calls it before `SetImageQuality`;
- `Post` calls it before `WhenImagesProcessed`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests` and `dotnet build ActualChat.CI.slnf`
Expected: PASS, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatMessageEditor tests/Chat.UI.Blazor.UnitTests/AttachmentListTest.cs
git commit -m "feat(attachments): upload only once the draft is committed"
```

---

## Task 10: Convert HEIC and AVIF at attach

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/FileAttachments.cs`
- Modify: `src/nodejs/src/image-processing/image-format.ts`
- Test: `tests/ts/unit/image-format.test.ts`

**Interfaces:**
- Consumes: the `placeholder` output (Task 8).
- Produces: `needsPreviewConversion(format: ImageFormat): boolean` in TS; `FileAttachments` repoints an attachment's preview at the converted file.

**Why:** a HEIC picked on Android renders as a permanently stuck "loading" tile, because the preview URL points at the source and Chromium cannot decode HEIC. This is the fix, and it is the one thing that still happens at attach time.

- [ ] **Step 1: Write the failing test**

```ts
describe('needsPreviewConversion', () => {
    it.each(['heif', 'avif'] as const)('should convert %s', (format) => {
        expect(needsPreviewConversion(format)).toBe(true);
    });

    it.each(['jpeg', 'png', 'webp', 'gif', 'bmp'] as const)('should leave %s alone', (format) => {
        expect(needsPreviewConversion(format)).toBe(false);
    });
});
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npx vitest run tests/ts/unit/image-format.test.ts`
Expected: FAIL — not exported.

- [ ] **Step 3: Implement**

```ts
/** Formats a Chromium WebView cannot paint, so their preview must come from a converted copy.
 *  WebKit decodes both, but converting there too keeps one code path. */
export function needsPreviewConversion(format: ImageFormat): boolean
    => format === 'heif' || format === 'avif';
```

- [ ] **Step 4: Convert on attach and repoint the preview**

In `FileAttachments.AddAttachment`, for a processable image whose type is HEIC/HEIF/AVIF, run a preview-only process request (main output at the 3 mpx budget, plus the placeholder), then:

```csharp
        var preview = await FilePreviews.Get(previewProvider, "image/jpeg", Hub.StopToken);
        AttachmentsState.SetPreview(attachment.Id, AttachmentPreview.From(preview));
```

The converted file is a cleanup-owned temporary: register it with `AttachmentCleanupFactory.ForFile` so removing the attachment deletes it.

- [ ] **Step 5: Run the tests**

Run: `npx vitest run tests/ts/unit/image-format.test.ts`, `dotnet build ActualChat.CI.slnf`
Expected: PASS, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/nodejs/src/image-processing/image-format.ts tests/ts/unit/image-format.test.ts src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/FileAttachments.cs
git commit -m "fix(attachments): show a preview for HEIC and AVIF pictures"
```

---

## Task 11: Animated images keep their presets out of the way

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ImageQualitySelector.razor`, `ImageQualityMenu.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/Attachment/Attachment.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/AttachmentTest.cs`

**Interfaces:**
- Produces: `Attachment.IsReEncodable` — false for GIF, animated WebP and APNG, true for other supported images.

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData("image/gif", false)]
[InlineData("image/svg+xml", false)]
[InlineData("image/jpeg", true)]
[InlineData("image/png", true)]
[InlineData("image/heic", true)]
public void ShouldKnowWhatCanBeReEncoded(string fileType, bool expected)
{
    var attachment = new Attachment("x", fileType, 1000, new Size2D(10, 10));
    attachment.IsReEncodable.Should().Be(expected);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AttachmentTest"`
Expected: FAIL — `IsReEncodable` does not exist.

- [ ] **Step 3: Implement**

In `Attachment.cs`, beside `IsProcessableImage`:

```csharp
    // Animated formats lose their animation if re-encoded, so no preset applies to them
    public bool IsReEncodable => IsProcessableImage;
```

`IsProcessableImage` already excludes GIF and SVG. Animated WebP and APNG are detected by the worker (`isAnimatedImage`), which returns the source untouched for them — so the C# side only needs the static check, and the menu hides for anything where `IsReEncodable` is false.

- [ ] **Step 4: Hide the selector**

In `ImageQualitySelector.razor`, render nothing when the list holds no re-encodable image:

```razor
@if (!Attachments.Items.Any(a => a.IsReEncodable)) {
    return;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AttachmentTest"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components tests/Chat.UI.Blazor.UnitTests/AttachmentTest.cs
git commit -m "feat(attachments): hide quality presets for animated images"
```

---

## Task 12: Remove the media a discarded session reserved

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/FileUploads/UploadSessions.cs:205-219`, `UploadOperations.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/UploadSessionsTest.cs`

**Interfaces:**
- Produces: `UploadOperations.RemoveMedia(MediaId, CancellationToken)`; `DeleteSessionResources` calls it for the session's `ReservedMediaId`.

**Why:** a session reserves a media row before its first byte uploads, and discarding the session never removes it. The bytes are deleted correctly; the row and its progress row leak forever, and nothing on the server collects them.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task DiscardingASessionShouldRemoveItsReservedMedia()
{
    // Arrange
    var operations = new FakeUploadOperations();
    var sessions = NewUploadSessions(operations);
    var sessionId = await sessions.CreateSession(NewFileProvider(), MetadataBag.Empty, "");
    await sessions.GetOrReserveMedia(sessionId, CancellationToken.None);

    // Act
    sessions.ReleaseReference(sessionId);
    await operations.WhenIdle();

    // Assert
    operations.RemovedMediaIds.Should().ContainSingle();
}
```

If no fake exists for `UploadOperations`, add a minimal one in the test project rather than reaching for a mocking framework — the repo's other upload tests do the same.

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~UploadSessionsTest"`
Expected: FAIL — nothing removes the media.

- [ ] **Step 3: Implement**

`UploadOperations.cs`:

```csharp
    public async Task RemoveMedia(MediaId mediaId, CancellationToken cancellationToken)
        => await Commander.Call(new Media_RemoveMedia {
            Session = Session,
            MediaId = mediaId,
        }, cancellationToken).ConfigureAwait(false);
```

`UploadSessions.DeleteSessionResources` — after removing the upload:

```csharp
        if (reservedMediaId is { } mediaId)
            await _uploadOperations.RemoveMedia(mediaId, CancellationToken.None).ConfigureAwait(false);
```

Pass `session.Snapshot.ReservedMediaId` in from `DeleteSessionInternal`, alongside `session.UploadId`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~UploadSessionsTest"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/FileUploads tests/Chat.UI.Blazor.UnitTests/UploadSessionsTest.cs
git commit -m "fix(uploads): remove the media a discarded session reserved"
```

---

## Task 13: Menu wiring and localization

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ImageQualitySelector.razor`, `ImageQualityMenu.razor`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`, `src/dotnet/Localization/Resources/Strings.{bg,bs,cs,de,en,es,fr,hi,id,it,ja,ko,pl,pt,ru,tr,uk,vi,zh}.json`
- Test: `tests/Chat.UI.Blazor.UnitTests/AppLocalizationTest.cs` (existing, must keep passing)

**Interfaces:**
- Consumes: `ImageSizeEstimator` (Task 5), `ImageQualityPreset` (Task 2).
- Produces: menu option models carrying label, spec and estimated size.

- [ ] **Step 1: Add the keys**

`LocalizedStringsLocalizerExt.cs` — three new keys beside the existing quality ones:

```csharp
    public static string Editor_QualityUpTo50Mpx(this ILocalizedStrings strings) => strings["Editor_QualityUpTo50Mpx"];
    public static string Editor_QualityUpTo12Mpx(this ILocalizedStrings strings) => strings["Editor_QualityUpTo12Mpx"];
    public static string Editor_QualityUpTo3Mpx(this ILocalizedStrings strings) => strings["Editor_QualityUpTo3Mpx"];
```

English values: `Up to 50mpx / 12K`, `Up to 12mpx / 6K`, `Up to 3mpx / 3K`.

Translate all 19 hand-written catalogs. The numbers and the `K`/`mpx` units stay as they
are in every language — they are units, not words; only "Up to" is translated (e.g. `До 50mpx / 12K` for ru, `Bis zu 50mpx / 12K` for de). Do not machine-translate the units.

- [ ] **Step 2: Build the option models**

In `ImageQualitySelector.razor`, where the menu model is assembled:

```csharp
    private ImageQualityMenu.OptionModel ToOption(ImageQualityPreset preset, Attachment attachment)
    {
        var budget = preset.GetBudget();
        var size = budget.MaxPixels is null
            ? ImageSizeEstimator.Format(attachment.Length)
            : ImageSizeEstimator.Format(
                ImageSizeEstimator.Estimate(attachment.Length, attachment.Size, attachment.FileType, budget));
        return new(preset, GetLabel(preset), GetSpec(preset), size);
    }
```

For a multi-image draft the size shown is the sum over the re-encodable attachments.

- [ ] **Step 3: Say when Original becomes a file**

When an attachment exceeds the server bounds (`Constants.Attachments.MaxImageSize` / `MaxImagePixelCount`), the `Original (with EXIF)` option's spec line reads "sent as a file" — a new key `Editor_QualityOriginalAsFile`, translated in all 19 catalogs.

- [ ] **Step 4: Regenerate the derived catalogs**

Run: `scripts/derive-bcms.cmd` then `scripts/derive-max.cmd` (no `--check` first), commit whatever they regenerate, then re-run both with `--check`.
Expected: both `--check` runs report no differences.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests --filter "FullyQualifiedName~AppLocalizationTest"`, `dotnet build ActualChat.CI.slnf`
Expected: PASS, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Localization src/dotnet/UI.Blazor.App/Components/ChatMessageEditor
git commit -m "feat(attachments): quality menu labels and estimated sizes"
```

---

## Task 14: Verification and docs

**Files:**
- Modify: `docs/api-index-ts.md`, `docs/api-index.md`, `docs/superpowers/specs/2026-09-12-image-presets-and-placeholders-design.md`

- [ ] **Step 1: Full automated run**

Run: `npm run build:Verify`, `npm run test:unit`, `dotnet build ActualChat.CI.slnf`, `dotnet test tests/Core.Server.UnitTests --filter "FullyQualifiedName~Uploads"`, `dotnet test tests/Media.UnitTests`, `dotnet test tests/Chat.UI.Blazor.UnitTests`, `scripts/derive-bcms.cmd --check`, `scripts/derive-max.cmd --check`
Expected: all PASS. Report any failure with its output; don't mark the task done.

- [ ] **Step 2: Index the new APIs**

`docs/api-index-ts.md`, shared section:

```markdown
- `fitWithinBudget` (function) - Fits an image to a pixel budget and a long-side cap.
- `encodePlaceholder` (function) - Encodes a 64px blurred placeholder into its container.
- `needsPreviewConversion` (function) - True for formats a Chromium WebView cannot paint.
```

`docs/api-index.md`, UI.Blazor.App section:

```markdown
- `ImageSizeEstimator` (class) - Predicts an upload's size from the source's bytes and dimensions.
- `ImagePlaceholder` (class) - Rebuilds a stored placeholder into a data URL.
- `ImageQualityBudget` (record struct) - A preset's pixel budget and long-side cap.
```

- [ ] **Step 3: Record what shipped**

Append an "Implementation notes" section to the spec: the preset enum order versus menu order, the placeholder prefix constant and the rule that changing it needs a new format mark, the measured jpegli ceiling (240 MP) and the mobile encode guard, and anything the implementation had to do differently.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs(image-processing): index new APIs, record implementation notes"
```

- [ ] **Step 5: Hand back**

The runtime matrix — Chrome, Safari on the Mac Mini, Android, iPhone, the Windows app — is the controller's to run with the user's rig, not this task's. Report what automated checks covered and what remains.

---

## Deferred, with reasons

- **Tiling for very large images.** Measured: correct, but each `createImageBitmap` crop re-decodes the whole file, so cost scales with tile count (31 s for 8×8 on desktop against 0.9 s for one scaled decode). A scaled decode already avoids the memory problem.
- **Android's 75% decode ceiling.** Chromium silently returns 112 MP for a 199.8 MP source, so no preset can recode such a photo at full resolution there. Passthrough preserves it; nothing else can.
- **Backfilling placeholders** for media that predates this change. Old rows return `""` and fall back to the grey skeleton.
