# Login Flow

This document describes how to authenticate in Voxt for testing purposes. Understanding the login flow is a prerequisite for most browser-based testing scenarios.

## Test Accounts

For automated testing, special test accounts are available that bypass the normal OTP email flow:

| Email Pattern | OTP Code | Description |
|---------------|----------|-------------|
| `test-*@actual.chat` | `111111` | AI agent and automation test accounts |

**Examples:**
- `test-agent1@actual.chat`
- `test-bot@actual.chat`
- `test-playwright@actual.chat`

### Restrictions

Test accounts only work on local development domains:
- `local.voxt.ai`
- `local.actual.chat`
- `localhost`

They are explicitly **disabled** on:
- Production: `voxt.ai`, `actual.chat`
- Staging: `dev.voxt.ai`, `dev.actual.chat`

### How It Works

When a `test-*@actual.chat` email is used:
1. No actual email is sent (the send is skipped)
2. The OTP code `111111` is always accepted
3. The user is signed in normally

Implementation: `src/dotnet/Users.Service/Email/EmailAuth.cs`

## Debug UI Methods

After signing in, you may need to dismiss onboarding dialogs and tutorial bubbles. Use these console methods:

### Skip Onboarding

```javascript
// Skip all onboarding steps and close the modal
debugUI.resetOnboarding(false);

// Re-enable all onboarding steps (for testing the onboarding flow)
debugUI.resetOnboarding(true);
```

### Skip Bubbles

```javascript
// Suppress all tutorial bubbles (including future ones)
debugUI.resetBubbles(false);

// Re-enable all tutorial bubbles
debugUI.resetBubbles(true);
```

## Login Steps

The typical login flow for Playwright automation:

### 1. Handle Cookie Consent

```typescript
const cookieButton = page.locator('button.cookie-btn:has-text("Accept all cookies")');
if (await cookieButton.isVisible({ timeout: 3000 }).catch(() => false)) {
    await cookieButton.click();
}
```

### 2. Sign In with Test Account

```typescript
// Click sign-in button (location depends on page context)
// On chat pages, the sign-in button is in the footer:
await page.locator('div.signin-footer button').first().click();
// On the landing page, use the header button:
// await page.locator('button.signin-button-group').click();

// Select email option if shown
const emailOption = page.locator('button:has-text("Email")');
if (await emailOption.isVisible({ timeout: 5000 }).catch(() => false)) {
    await emailOption.click();
}

// Enter test email
await page.locator('input[type="email"]').fill('test-agent@actual.chat');
await page.locator('button:has-text("Continue")').click();

// Enter OTP code (6 individual digit inputs)
const digitInputs = page.locator('input[maxlength="1"]');
for (let i = 0; i < 6; i++) {
    await digitInputs.nth(i).fill('1');
}
```

### 3. Skip Onboarding and Bubbles

```typescript
await page.evaluate(() => {
    const debugUI = (window as any).debugUI;
    if (debugUI) {
        debugUI.resetOnboarding(false);
        debugUI.resetBubbles(false);
    }
});
```

### 4. Join Chat if Needed

Some chats require joining before you can interact:

```typescript
const joinButton = page.locator('button:has-text("Join this chat")');
if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
    await joinButton.click();
}
```

### 5. Send a Message

```typescript
const messageInput = page.locator('.message-input[contenteditable="true"]');
await messageInput.click();
await page.keyboard.type('Hello from Playwright!');
await page.locator('button.post-message').click();
```

## Example Script

A complete example is available at [`docs/tests/signin-and-message.ts`](../tests/signin-and-message.ts).

```bash
# Install playwright if needed
npm install playwright

# Run the test script
npx tsx docs/tests/signin-and-message.ts
```

## Troubleshooting

### Sign-in button not visible

The sign-in button location depends on the page:
- **Chat pages**: The sign-in button is inside a footer overlay: `div.signin-footer button`
- **Landing page**: Use the header button: `button.signin-button-group`

If navigating directly to a chat URL (e.g., `/chat/the-actual-one`), look for the footer button first.

### Onboarding modal blocks interaction

Call `debugUI.resetOnboarding(false)` after sign-in and again after navigation.

### Bubbles appear after reset

The bubble reset must be called after the bubble system initializes. Call it after page navigation with a short delay, or call it multiple times.

### Message input not found

Some chats require joining first. Check for and click the "Join this chat" button before looking for the message input.
