## Design decisions

### LangServe
For easier integration between different services all bots must be running under LangServe.
This ensures the same REST API for all bot implementations.

### RemoteRunnable C# implementation
At the moment of writing there was no known public C# implementation available.
