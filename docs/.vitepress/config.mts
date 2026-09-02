import { defineConfig } from "vitepress";
import { withMermaid } from "vitepress-plugin-mermaid";

// Grafana theme with Inter font (from modern_mermaid)
const mermaidConfig = {
  theme: 'base' as const,
  themeVariables: {
    darkMode: true,
    background: '#1e1b4b',
    primaryColor: '#1F2428',
    primaryTextColor: '#D8D9DA',
    primaryBorderColor: '#3D434B',
    lineColor: '#5794F2',
    secondaryColor: '#262B31',
    tertiaryColor: '#2C3235',
    fontFamily: '"Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    fontSize: '14px',
  },
  themeCSS: `
    /* Grafana-inspired style with Inter font */
    .node rect, .node circle, .node polygon {
      fill: #1F2428 !important;
      stroke: #3D434B !important;
      stroke-width: 2px !important;
      rx: 3px !important;
      ry: 3px !important;
      filter: drop-shadow(0 2px 4px rgba(0, 0, 0, 0.3));
    }
    .node .label {
      font-family: "Inter", sans-serif;
      font-weight: 500;
      fill: #D8D9DA !important;
      font-size: 14px;
    }
    .edgePath .path {
      stroke: #5794F2 !important;
      stroke-width: 2px !important;
      stroke-linecap: round;
    }
    .arrowheadPath {
      fill: #5794F2 !important;
      stroke: #5794F2 !important;
    }
    .edgeLabel {
      background-color: #1e1b4b !important;
      color: #D8D9DA !important;
      font-family: "Inter", sans-serif;
      font-size: 13px;
    }
    .cluster rect {
      fill: rgba(87, 148, 242, 0.05) !important;
      stroke: #3D434B !important;
      stroke-width: 2px !important;
      stroke-dasharray: 6 4 !important;
      rx: 3px !important;
    }
    .cluster text {
      fill: #73BF69 !important;
      font-family: "Inter", sans-serif;
      font-weight: 600;
    }
  `,
};

// https://vitepress.dev/reference/site-config
export default withMermaid(defineConfig({
  lang: "en-US",
  title: "Voxt",
  description:
    "Voxt is a real-time communication platform built on ActualLab.Fusion. This site hosts Voxt architecture and development documentation.",
  head: [
    ["link", { rel: "icon", href: "/favicon.ico" }],
    ["style", {}, `.vp-doc .mermaid p { line-height: normal !important; padding: 2px 0 !important; } .mermaid { background-color: #1e1b4b; background-image: radial-gradient(ellipse at 30% 20%, rgba(59, 130, 246, 0.3) 0%, transparent 40%), radial-gradient(ellipse at 70% 60%, rgba(168, 85, 247, 0.3) 0%, transparent 40%), radial-gradient(ellipse at 50% 80%, rgba(34, 211, 238, 0.2) 0%, transparent 40%); border-radius: 8px; padding: 16px; margin: 16px 0; }`],
  ],
  srcExclude: [
    "node_modules",
  ],
  // Links to repo files outside docs/ and to local dev servers can't resolve in the rendered site
  ignoreDeadLinks: [
    /^https?:\/\/localhost/,
    /\.editorconfig$/,
    /\.\.\/src\//,
    /\.\.\/tests\//,
    /\.\.\/scripts\//,
  ],
  appearance: 'dark',
  themeConfig: {
    search: {
      provider: "local",
    },
    sidebar: [
      {
        text: "Documentation",
        items: [
          { text: "Overview", link: "/" },
          {
            text: "Architecture",
            collapsed: false,
            items: [
              { text: "Overview", link: "/architecture/overview" },
              { text: "Service Design", link: "/architecture/service-design" },
              { text: "Project Structure", link: "/architecture/project-structure" },
              { text: "RPC Method Hashes", link: "/architecture/rpc-method-hashes" },
              { text: "Serialization", link: "/architecture/serialization" },
              { text: "Server Clock Sync", link: "/architecture/server-clock-sync" },
              { text: "Offline Render Path", link: "/architecture/offline-render-path" },
              { text: "Notifications", link: "/notifications" },
            ],
          },
          {
            text: "Live Video Pipeline",
            collapsed: true,
            items: [
              { text: "Overview", link: "/live-video/README" },
              { text: "End-to-end", link: "/live-video/01-end-to-end" },
              { text: "Sender", link: "/live-video/02-sender" },
              { text: "Codecs and layers", link: "/live-video/03-codecs-and-layers" },
              { text: "RPC and framing", link: "/live-video/04-rpc-and-framing" },
              { text: "Server publish", link: "/live-video/05-server-publish" },
              { text: "Server fan-out", link: "/live-video/06-server-fanout" },
              { text: "Receiver", link: "/live-video/07-receiver" },
              { text: "Quality control", link: "/live-video/08-quality-control" },
              { text: "Diagnostics", link: "/live-video/09-diagnostics" },
              { text: "Glossary", link: "/live-video/10-glossary" },
              { text: "Buffering and A/V sync", link: "/live-video/11-buffering-and-av-sync" },
            ],
          },
          {
            text: "Live Audio Pipeline",
            collapsed: true,
            items: [
              { text: "Overview", link: "/live-audio/README" },
              { text: "End-to-end", link: "/live-audio/01-end-to-end" },
              { text: "Recorder", link: "/live-audio/02-recorder" },
              { text: "Codec and VAD", link: "/live-audio/03-codec-and-vad" },
              { text: "RPC and formats", link: "/live-audio/04-rpc-and-formats" },
              { text: "Publish and transcribe", link: "/live-audio/05-server-publish-and-transcribe" },
              { text: "Server fan-out and replay", link: "/live-audio/06-server-fanout-and-replay" },
              { text: "Receiver", link: "/live-audio/07-receiver" },
              { text: "Diagnostics and tuning", link: "/live-audio/08-diagnostics-and-tuning" },
              { text: "Glossary", link: "/live-audio/09-glossary" },
              { text: "PTT", link: "/live-audio/10-push-to-talk" },
            ],
          },
          {
            text: "UI",
            collapsed: true,
            items: [
              { text: "Overview", link: "/ui/" },
              { text: "Component Guidelines", link: "/ui/components" },
              { text: "Walk-through", link: "/ui/walk-through" },
              { text: "The Virtual List", link: "/ui/virtual-list" },
              { text: "Safe Areas", link: "/ui/safe-areas" },
              { text: "Splash Screens", link: "/ui/splash-screen" },
            ],
          },
          {
            text: "Development",
            collapsed: false,
            items: [
              { text: "Running Voxt", link: "/running-voxt" },
              { text: "Build Tool (b)", link: "/build-tool" },
              { text: "Implementing Features", link: "/development/implementing-features" },
              { text: "Coding Style", link: "/CODING_STYLE" },
              { text: "API Index", link: "/api-index" },
              { text: "AI Agent Guide", link: "/AGENTS" },
              { text: "Localization (i18n)", link: "/i18n" },
              { text: "iOS-specific Behavior", link: "/ios-specific" },
              { text: "Android-specific Behavior", link: "/android-specific" },
              { text: "Native AOT", link: "/native-aot" },
              { text: "Startup Profiling", link: "/startup-profiling" },
              { text: "App Bundles", link: "/app-bundles" },
              { text: "Runtime Async", link: "/runtime-async" },
            ],
          },
          {
            text: "Testing",
            collapsed: false,
            items: [
              { text: "Overview", link: "/testing/overview" },
              { text: "Playwright (C#)", link: "/testing/playwright-csharp" },
              { text: "Playwright (AI)", link: "/testing/playwright-ai" },
              { text: "Login Flow", link: "/testing/login-flow" },
            ],
          },
          { text: "Plans", link: "/plans/" },
        ],
      },
    ],
    socialLinks: [
      { icon: "github", link: "https://github.com/ActualChat/ActualChat" },
    ],
    outline: [2, 3],
  },
  mermaid: mermaidConfig,
}));
