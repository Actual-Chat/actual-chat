## Design decisions

### LangServe
For easier integration between different services all bots must be running under LangServe.
This ensures the same REST API for all bot implementations.

### RemoteRunnable C# implementation
At the moment of writing there was no known public C# implementation available.

### Expected .secrets fields
CLAUDE_API_KEY=
LANGFUSE_SECRET_KEY=
LANGFUSE_PUBLIC_KEY=
LANGFUSE_HOST=
BOT_TOOLS_BASE_URL=

### Start direct chat with the bot
- Make sure you have set "AllowPeerBotChat" server app config to true
- Message to <base url>/u/ml-search

