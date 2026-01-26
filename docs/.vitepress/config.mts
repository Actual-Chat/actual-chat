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
  ignoreDeadLinks: false,
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
            ],
          },
          {
            text: "Development",
            collapsed: false,
            items: [
              { text: "Running Voxt", link: "/running-voxt" },
              { text: "Coding Style", link: "/CODING_STYLE" },
              { text: "AI Agent Guide", link: "/AGENTS" },
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
          {
            text: "Plans",
            collapsed: true,
            items: [
              { text: "Big Tasks", link: "/plans/BigTasks" },
              { text: "Small Tasks", link: "/plans/SmallTasks" },
              { text: "Search", link: "/plans/Search" },
            ],
          },
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
