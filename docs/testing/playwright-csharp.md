# Playwright (C#)

> **Note:** C# Playwright tests are currently not working and need maintenance. This documentation is preserved for reference.

This guide covers the C# Playwright testing infrastructure for browser-based UI tests.

## Test Projects

Playwright tests are located in:
- `tests/*.PlaywrightTests/` - Browser-based UI tests

## Setup

For local development, install Playwright browsers:

```bash
pwsh -c "dotnet tool run playwright install chromium"
```

## Writing Playwright Tests

Use the `PlaywrightTester` class for browser-based tests:

```csharp
public class MyPlaywrightTest : AppHostTestBase
{
    [Fact]
    public async Task ShouldNavigateToChat()
    {
        await using var tester = AppHost.NewPlaywrightTester(Out);
        await tester.SignInAsNew("TestUser");

        var (page, response) = await tester.NewPage("/chat/the-actual-one");

        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // Interact with the page
        await page.WaitForSelectorAsync(".chat-view");
    }
}
```

## Key PlaywrightTester Methods

| Method | Description |
|--------|-------------|
| `GetPlaywright()` | Returns the Playwright instance |
| `GetBrowser()` | Returns the Chromium browser instance |
| `NewContext()` | Creates a new browser context with session cookie |
| `NewPage(relativeUri)` | Creates a new page and navigates to the URI |

## Browser Context Configuration

The `PlaywrightTester` automatically:
- Sets the base URL from `UrlMapper`
- Bypasses Content Security Policy for testing
- Injects the session cookie for authentication

## Test Base Class

```csharp
public class MyTest : AppHostTestBase
{
    public MyTest(AppHostFixture fixture, ITestOutputHelper @out)
        : base(fixture, @out) { }

    [Fact]
    public async Task MyTestMethod()
    {
        await using var tester = AppHost.NewPlaywrightTester(Out);
        // Test code here
    }
}
```

## Test Collections

Tests are organized into collections to manage shared resources:

```csharp
[Collection(nameof(ChatCollection))]
public class ChatTests : AppHostTestBase
{
    // Tests share the same AppHost instance
}
```
