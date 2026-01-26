# Testing

This guide covers testing approaches and test accounts for Voxt development.

## Running Tests

### Prerequisites

Ensure infrastructure services are running:

```bash
docker compose up -d --build --wait
```

### Running Tests

```bash
# Run all tests (uses CI solution filter)
dotnet test ActualChat.CI.slnf

# Run specific test project
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj

# Run tests with filter
dotnet test --filter "FullyQualifiedName~ChatTest"
```

### Test Categories

| Category | Location | Description |
|----------|----------|-------------|
| Unit Tests | `tests/*.UnitTests/` | Fast, isolated tests without external dependencies |
| Integration Tests | `tests/*.IntegrationTests/` | Tests requiring database, Redis, NATS |
| Playwright Tests | `tests/*.PlaywrightTests/` | Browser-based UI tests (C#) |

## Test Accounts

### AI Agent Test Accounts

For automated testing and AI agent authentication, special test accounts are available:

| Email Pattern | OTP Code | Notes |
|---------------|----------|-------|
| `test-*@actual.chat` | `111111` | Works on local domains and localhost only |

**Examples of valid test emails:**
- `test-agent1@actual.chat`
- `test-bot@actual.chat`
- `test-automation@actual.chat`

**Usage:**
1. Enter any email matching `test-*@actual.chat` in the sign-in form
2. Use code `111111` when prompted
3. No actual email is sent for these accounts

**Restrictions:**
- Does NOT work on production or staging domains:
  - Production: `voxt.ai`, `actual.chat`
  - Staging: `dev.voxt.ai`, `dev.actual.chat`
- Works on local dev domains and localhost:
  - `local.voxt.ai`, `local.actual.chat`
  - `localhost`

### Phone Auth Test Accounts

Phone authentication supports predefined TOTP codes configured via `UsersSettings.PredefinedTotps`. These are typically set via environment variables for specific test phone numbers.

### Programmatic Test Sign-In

For integration tests, use the `TestAuthExt` helper methods:

```csharp
// Sign in with a new test account
var account = await tester.SignInAsNew("TestUser");

// Sign in with specific account details
var account = new AccountFull("TestUser").WithClaim(ClaimTypes.GivenName, "Test");
await tester.SignIn(account);

// Sign out
await tester.SignOut();
```

## Integration Test Environment

### Docker Environment Detection

Integration tests detect the Docker environment via the `AC_OS` environment variable:

```csharp
var isDockerEnvironment = Environment.GetEnvironmentVariable("AC_OS") == "Linux in Docker";
```

### Service Connectivity

When running tests in Docker:

| Service | Host | Port |
|---------|------|------|
| PostgreSQL | localhost | 5432 |
| Redis | localhost | 6379 |
| NATS | localhost | 4222 |

These work because Docker uses `--network host` mode.

### Test Settings

Test configuration is loaded from `testsettings.json`. For Docker-specific overrides, the tests automatically use localhost-based configuration since `--network host` makes localhost = host.

## Test Fixtures and Collections

### AppHostTestBase

Base class for tests requiring the full application host:

```csharp
public class MyTest : AppHostTestBase
{
    public MyTest(AppHostFixture fixture, ITestOutputHelper @out)
        : base(fixture, @out) { }

    [Fact]
    public async Task MyTestMethod()
    {
        await using var tester = AppHost.NewWebClientTester(Out);
        // Test code here
    }
}
```

### Test Collections

Tests are organized into collections to manage shared resources:

```csharp
[Collection(nameof(ChatCollection))]
public class ChatTests : AppHostTestBase
{
    // Tests share the same AppHost instance
}
```

## Tips and Best Practices

### Avoiding Test Pollution

- Each test should create its own test accounts using `SignInAsNew()`
- Use unique prefixes to identify test data: `await tester.SignInAsNew("MyTest_User")`
- Clean up created resources when necessary

### Debugging Slow Tests

```bash
# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run single test
dotnet test --filter "FullyQualifiedName=Namespace.Class.Method"
```

### Parallel Test Execution

Integration tests use xUnit collections to control parallelism. Tests in the same collection run sequentially; different collections run in parallel.

### Handling Flaky Tests

For tests sensitive to timing:

```csharp
// Wait for computed value to update
var computed = await Computed.Capture(() => service.GetValue(id, ct));
computed = await computed
    .When(x => x.SomeCondition, ct)
    .WaitAsync(TimeSpan.FromSeconds(5), ct);
```
