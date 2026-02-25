# SVG/PNG Avatar Generation Endpoints

## Overview

Create server-side endpoints to generate Beam and Marble avatars as SVG or PNG files.

## Problem

Currently, avatars are generated client-side in TypeScript (`beam-avatar.lit.ts`, `marble-avatar.lit.ts`). Server-side generation will provide:
- Better caching and CDN integration
- Reduced client bundle size
- Consistent rendering across web and mobile
- Server-side rendering support

## Solution

Add server-side avatar generation endpoints that:
1. Generate SVG or PNG avatars on-demand based on a key/hash
2. Support both Beam and Marble avatar styles
3. Use aggressive HTTP caching (immutable, long max-age)
4. Allow customization via query parameters

## Existing Infrastructure

### ✅ Already Available

1. **C# Avatar Generators** (PNG-only):
   - `src/dotnet/Maui/Services/Avatars/BeamAvatars.cs` - generates Beam-style PNGs using SkiaSharp
   - `src/dotnet/Maui/Services/Avatars/MarbleAvatars.cs` - generates Marble-style PNGs using SkiaSharp
   - `src/dotnet/Maui/Services/Avatars/AvatarUtils.cs` - hash/color utilities (matches TypeScript version)

2. **Existing Patterns**:
   - `src/dotnet/Users.Service/Controllers/AvatarPicturesController.cs` - controller at `/api/avatars`
   - `src/dotnet/Media.Service/Controllers/ContentController.cs` - example of media serving with caching
   - `src/dotnet/Core.Server/AspNetCore/CacheControlImmutable.cs` - immutable caching attribute

3. **Dependencies**:
   - SkiaSharp available via `Svg.Skia` package in Maui project
   - Need to add SkiaSharp to Users.Service project

### 🔨 Need to Create

1. **SVG Generation**: Current C# implementations only generate PNG. Need to add SVG string generation methods similar to TypeScript versions.

2. **New Controller Endpoints**: Add avatar generation endpoints to handle GET requests.

## Implementation Plan

### 1. Copy Avatar Generation Code to Users.Service

**Approach**: Duplicate avatar generation code in Users.Service for now

- Copy `src/dotnet/Maui/Services/Avatars/AvatarUtils.cs` → `src/dotnet/Users.Service/Avatars/AvatarUtils.cs`
- Copy `src/dotnet/Maui/Services/Avatars/BeamAvatars.cs` → `src/dotnet/Users.Service/Avatars/BeamAvatars.cs`
- Copy `src/dotnet/Maui/Services/Avatars/MarbleAvatars.cs` → `src/dotnet/Users.Service/Avatars/MarbleAvatars.cs`

**Future**: Once API is stable, replace Maui generation with API URLs to eliminate duplication

### 2. Add SVG Generation Methods

For both `BeamAvatars` and `MarbleAvatars`, add static methods that generate SVG strings (port from TypeScript):

```csharp
public static class BeamAvatars
{
    // Existing
    public static void GeneratePng(string key, FilePath filePath, int? size = null) { }

    // NEW: Add these
    public static string GenerateSvg(string key, string[]? colors = null, bool square = false);
    public static byte[] GeneratePngBytes(string key, int size = 80, string[]? colors = null);
}

public static class MarbleAvatars
{
    // Existing
    public static void GeneratePng(string key, FilePath filePath, string title = "", bool doNotBlur = false, int? size = null) { }

    // NEW: Add these
    public static string GenerateSvg(string key, string[]? colors = null, string title = "", bool doNotBlur = false);
    public static byte[] GeneratePngBytes(string key, int size = 80, string[]? colors = null, string title = "", bool doNotBlur = false);
}
```

**SVG Generation Logic**:
- Port the `generateSvgString()` methods from `beam-avatar.lit.ts` and `marble-avatar.lit.ts`
- Keep sizes and design parameters identical to maintain visual consistency

### 3. Add Controller Endpoints

Modify `src/dotnet/Users.Service/Controllers/AvatarPicturesController.cs`:

```csharp
[ApiController, Route("api/avatars")]
public sealed class AvatarsController(IServiceProvider services) : ControllerBase
{
    // Existing upload endpoint...

    // NEW ENDPOINTS:

    [HttpGet("beam/{key}")]
    [CacheControlImmutable(Duration = 2592000)] // 30 days
    public ActionResult GetBeam(
        string key,
        [FromQuery] AvatarFormat format = AvatarFormat.Svg,  // Svg or Png (enum)
        [FromQuery] int? size = null,        // pixel size for PNG
        [FromQuery] bool square = false)     // rounded corners vs square
    {
        if (key.IsNullOrEmpty())
            return BadRequest("Key is required");

        if (format == AvatarFormat.Png) {
            size ??= 80;
            var pngBytes = BeamAvatars.GeneratePngBytes(key, size.Value, square: square);
            return File(pngBytes, "image/png");
        }

        var svg = BeamAvatars.GenerateSvg(key, square: square);
        return Content(svg, "image/svg+xml");
    }

    [HttpGet("marble/{key}")]
    [CacheControlImmutable(Duration = 2592000)] // 30 days
    public ActionResult GetMarble(
        string key,
        [FromQuery] AvatarFormat format = AvatarFormat.Svg,  // Svg or Png (enum)
        [FromQuery] int? size = null,
        [FromQuery] string? title = null,    // Initial letter to display
        [FromQuery] bool doNotBlur = false)  // Disable blur effect
    {
        if (key.IsNullOrEmpty())
            return BadRequest("Key is required");

        if (format == AvatarFormat.Png) {
            size ??= 80;
            var pngBytes = MarbleAvatars.GeneratePngBytes(key, size.Value, title: title ?? "", doNotBlur: doNotBlur);
            return File(pngBytes, "image/png");
        }

        var svg = MarbleAvatars.GenerateSvg(key, title: title ?? "", doNotBlur: doNotBlur);
        return Content(svg, "image/svg+xml");
    }
}
```

### 4. Add SkiaSharp Package Reference

Update `src/dotnet/Users.Service/Users.Service.csproj`:

```xml
<ItemGroup>
  <!-- Existing references... -->
  <PackageReference Include="SkiaSharp" />
</ItemGroup>
```

And add version to `Directory.Packages.props` if not already there (check Maui's version).


## File Changes

### Files to Create:
1. `src/dotnet/Users.Service/Avatars/AvatarUtils.cs` - copy from Maui
2. `src/dotnet/Users.Service/Avatars/BeamAvatars.cs` - copy from Maui + add SVG/PNG bytes methods
3. `src/dotnet/Users.Service/Avatars/MarbleAvatars.cs` - copy from Maui + add SVG/PNG bytes methods
4. `src/dotnet/Users.Service/Avatars/AvatarFormat.cs` - enum with Svg and Png values
5. `tests/Users.UnitTests/AvatarSvgGenerationTest.cs` - 13 unit tests for SVG generation
6. `tests/Users.IntegrationTests/AvatarEndpointsTest.cs` - 14 integration tests for HTTP endpoints

### Files to Modify:
1. `src/dotnet/Users.Service/Controllers/AvatarPicturesController.cs` - add new GET endpoints with enum parameter
2. `src/dotnet/Users.Service/Users.Service.csproj` - add Svg.Skia package reference

## URL Structure

### Beam Avatars:
- SVG: `GET /api/avatars/beam/{key}`
- SVG (square): `GET /api/avatars/beam/{key}?square=true`
- PNG: `GET /api/avatars/beam/{key}?format=Png&size=80`

### Marble Avatars:
- SVG: `GET /api/avatars/marble/{key}`
- SVG with title: `GET /api/avatars/marble/{key}?title=A`
- PNG: `GET /api/avatars/marble/{key}?format=Png&size=120&title=A`
- No blur: `GET /api/avatars/marble/{key}?doNotBlur=true`

**Format Parameter:** The `format` query parameter accepts `AvatarFormat` enum values (`Svg` or `Png`). ASP.NET Core binds enum values case-insensitively, so `format=png`, `format=Png`, and `format=PNG` all work. Default is `Svg`.

## Caching Strategy

1. **HTTP Caching**: `CacheControlImmutable` attribute with 30-day max-age
   - Avatars are deterministic (same key always produces same image)
   - Immutable + stale-while-revalidate allows aggressive browser caching

2. **In-Memory Caching**: Consider adding memory cache for frequently requested avatars (optional optimization)

## Testing

### Manual Testing:
1. Start the server
2. Navigate to:
   - `https://local.voxt.ai/api/avatars/beam/testuser123`
   - `https://local.voxt.ai/api/avatars/beam/testuser123?format=png&size=80`
   - `https://local.voxt.ai/api/avatars/marble/testuser456`
   - `https://local.voxt.ai/api/avatars/marble/testuser456?title=T&format=png`

3. Verify:
   - SVG is displayed correctly in browser
   - PNG is rendered at requested size
   - Same key produces same avatar every time
   - Response headers include proper caching (`Cache-Control: public, max-age=2592000, immutable`)

### Integration Testing:
1. Compare generated avatars with client-side versions to ensure visual consistency
2. Test different keys produce different avatars consistently

## Decisions Made

1. **Color customization**: ✅ Use default colors only
   - Keeps implementation simple and consistent with current behavior

2. **Code location**: ✅ Duplicate in Users.Service initially
   - Future: Replace Maui generation with API URLs once stable

3. **Response compression**: Auto-handled by ASP.NET Core (no explicit action needed)

## Benefits

1. ✅ **Reduces client bundle size** - Less JavaScript shipped to browser
2. ✅ **Server-side rendering** - Avatars available before JavaScript loads
3. ✅ **Consistent rendering** - Same avatar generation logic across web and mobile
4. ✅ **Better caching** - HTTP caching more effective than client-side blob URLs
5. ✅ **CDN-friendly** - Can serve avatars through CDN

## Migration Strategy

1. **Phase 1** (This Plan): Implement server endpoints in Users.Service
   - Duplicate avatar generation code from Maui
   - Add SVG generation methods
   - Create GET endpoints at `/api/avatars/beam/{key}` and `/api/avatars/marble/{key}`
   - Keep client-side generation in lit components unchanged for now

2. **Phase 2** (Future): Update Maui to use API endpoints
   - Replace local generation with API calls in Maui app
   - Remove duplicated avatar generation code from Maui
