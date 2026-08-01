/**
 * Playwright test script: Sign in with test account and send a message
 *
 * Prerequisites:
 * - Chrome running with remote debugging: `ai chrome` (on Windows host)
 * - Server running: `ai os` then `/server-start`
 * - npm install playwright
 *
 * Usage:
 *   npx tsx tests/ts/signin-and-message.ts
 *
 * Screenshots are saved to tmp/ folder.
 */

import { chromium } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

// Load HostSettings__BaseUri from .env file
function loadBaseUrl(): string {
    const envPath = path.resolve(process.cwd(), '.env');
    if (fs.existsSync(envPath)) {
        const match = fs.readFileSync(envPath, 'utf-8').match(/^HostSettings__BaseUri=(.+)$/m);
        if (match) return match[1].trim();
    }
    return 'https://local.voxt.ai';
}

// Configuration
const CHROME_HOST = '192.168.65.254'; // Resolved IP for host.docker.internal
const CHROME_PORT = 9222;
const BASE_URL = loadBaseUrl();
const TEST_EMAIL = 'test-claude-agent@actual.chat';
const TEST_OTP = '111111';

// Ensure tmp directory exists
const tmpDir = path.join(process.cwd(), 'tmp');
if (!fs.existsSync(tmpDir)) {
    fs.mkdirSync(tmpDir, { recursive: true });
}

function screenshotPath(name: string): string {
    return path.join(tmpDir, `${name}.png`);
}

async function main() {
    console.log(`Connecting to host Chrome at ${CHROME_HOST}:${CHROME_PORT}...`);

    const browser = await chromium.connectOverCDP(`http://${CHROME_HOST}:${CHROME_PORT}`);
    console.log('Connected to browser');

    const contexts = browser.contexts();
    const context = contexts.length > 0 ? contexts[0] : await browser.newContext();
    const page = await context.newPage();

    try {
        console.log(`Navigating to ${BASE_URL}...`);
        await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(2000);

        // Handle cookie consent
        console.log('Looking for cookie consent...');
        const cookieButton = page.locator('button.cookie-btn:has-text("Accept all cookies")');
        if (await cookieButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            console.log('Clicking "Accept all cookies"...');
            await cookieButton.click();
            await page.waitForTimeout(1000);
        }

        // Check if already signed in
        const accountDropdown = page.locator('.account-dropdown');
        const isSignedIn = await accountDropdown.isVisible({ timeout: 2000 }).catch(() => false);

        if (!isSignedIn) {
            // Sign in flow
            console.log('Starting sign-in flow...');

            // Click "Sign in" button in header
            const signInButton = page.locator('button.signin-button-group, button.signin-button').first();
            await signInButton.waitFor({ state: 'visible', timeout: 10000 });
            await signInButton.click();
            await page.waitForTimeout(2000);
            await page.screenshot({ path: screenshotPath('after-signin-click') });

            // Look for email sign-in option
            console.log('Looking for email sign-in option...');
            const emailOption = page.locator('button:has-text("Email")');
            if (await emailOption.isVisible({ timeout: 5000 }).catch(() => false)) {
                console.log('Clicking email option...');
                await emailOption.click();
                await page.waitForTimeout(1000);
            }

            // Enter email address
            console.log('Looking for email input...');
            const emailInput = page.locator('input[type="email"], input[placeholder*="email" i]').first();
            await emailInput.waitFor({ timeout: 5000 });
            console.log(`Entering test email: ${TEST_EMAIL}`);
            await emailInput.fill(TEST_EMAIL);
            await page.waitForTimeout(500);

            // Click continue button
            const continueButton = page.locator('button[type="submit"], button:has-text("Continue"), button:has-text("Send")').first();
            await continueButton.click();
            await page.waitForTimeout(2000);
            await page.screenshot({ path: screenshotPath('after-email-submit') });

            // Handle "Account not found" - check "Register a new account" toggle and resubmit
            const accountError = page.locator('.c-account-error:has-text("Account not found")');
            if (await accountError.isVisible({ timeout: 2000 }).catch(() => false)) {
                console.log('Account not found - checking "Register a new account" toggle...');
                const registerToggle = page.locator('label:has-text("Register a new account")');
                await registerToggle.click();
                await page.waitForTimeout(500);

                // Resubmit the form
                const registerButton = page.locator('button[type="submit"]').first();
                await registerButton.click();
                await page.waitForTimeout(2000);
                await page.screenshot({ path: screenshotPath('after-register-toggle') });
            }

            // Enter OTP code
            console.log('Looking for OTP input...');
            const digitInputs = page.locator('input[maxlength="1"]');
            const digitCount = await digitInputs.count();

            if (digitCount >= 6) {
                console.log(`Found ${digitCount} digit inputs, entering code ${TEST_OTP}...`);
                for (let i = 0; i < 6; i++) {
                    await digitInputs.nth(i).fill(TEST_OTP[i]);
                    await page.waitForTimeout(50);
                }
            } else {
                const otpInput = page.locator('input[inputmode="numeric"], input[type="tel"]').first();
                if (await otpInput.isVisible({ timeout: 3000 }).catch(() => false)) {
                    console.log(`Found single OTP input, entering code ${TEST_OTP}...`);
                    await otpInput.fill(TEST_OTP);
                }
            }

            await page.waitForTimeout(1000);

            // Click verify button if needed
            const verifyButton = page.locator('button[type="submit"], button:has-text("Verify"), button:has-text("Continue")').first();
            if (await verifyButton.isVisible({ timeout: 2000 }).catch(() => false)) {
                console.log('Clicking verify button...');
                await verifyButton.click();
                await page.waitForTimeout(3000);
            }

            await page.screenshot({ path: screenshotPath('after-signin-complete') });
            console.log('Sign-in flow completed');
        } else {
            console.log('Already signed in');
        }

        // Skip onboarding and bubbles using debugUI
        console.log('Skipping onboarding and bubbles...');
        await page.evaluate(() => {
            const debugUI = (window as any).debugUI;
            if (debugUI) {
                debugUI.resetOnboarding(false);
                debugUI.resetBubbles(false);
            }
        });
        await page.waitForTimeout(1000);

        // Navigate to announcements chat
        console.log('Navigating to announcements chat...');
        await page.goto(`${BASE_URL}/chat/the-actual-one`, { waitUntil: 'domcontentloaded' });
        await page.waitForTimeout(3000);

        // Skip onboarding and bubbles again (in case they weren't loaded before)
        await page.evaluate(() => {
            const debugUI = (window as any).debugUI;
            if (debugUI) {
                debugUI.resetOnboarding(false);
                debugUI.resetBubbles(false);
            }
        });
        await page.waitForTimeout(1000);

        await page.screenshot({ path: screenshotPath('chat-page') });

        // Check if we need to join the chat first
        console.log('Checking if chat needs to be joined...');
        const joinButton = page.locator('button:has-text("Join this chat")');
        if (await joinButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            console.log('Clicking "Join this chat" button...');
            await joinButton.click();
            await page.waitForTimeout(2000);
        }

        // Find and interact with message input
        console.log('Looking for message input...');
        const messageInput = page.locator('.message-input[contenteditable="true"], #message-input').first();

        if (await messageInput.isVisible({ timeout: 5000 }).catch(() => false)) {
            console.log('Found message input, clicking...');
            await messageInput.click();
            await page.waitForTimeout(500);

            const testMessage = `Hello from Playwright! Test at ${new Date().toISOString()}`;
            console.log('Typing message...');
            await page.keyboard.type(testMessage);

            await page.waitForTimeout(500);
            await page.screenshot({ path: screenshotPath('before-send') });

            // Click send button
            console.log('Looking for send button...');
            const sendButton = page.locator('button.post-message');
            if (await sendButton.isVisible({ timeout: 3000 }).catch(() => false)) {
                console.log('Clicking send button...');
                await sendButton.click();
                await page.waitForTimeout(2000);
                console.log('Message sent!');
            } else {
                console.log('Send button not found, trying Ctrl+Enter...');
                await page.keyboard.press('Control+Enter');
                await page.waitForTimeout(2000);
            }

            await page.screenshot({ path: screenshotPath('after-send') });
            console.log('Done!');
        } else {
            console.log('Message input not found');
            await page.screenshot({ path: screenshotPath('no-message-input') });
        }

    } catch (error) {
        console.error('Error:', error);
        await page.screenshot({ path: screenshotPath('error') });
        throw error;
    }
}

main().catch(console.error);
