namespace ActualChat.Core.Server.IntegrationTests.Internal;

// Renames the SG-emitted resolver from the default MessagePack.GeneratedMessagePackResolver
// to a project-local name, avoiding CS0436 collisions with identically-named resolvers
// emitted into referenced assemblies (Core.Server, Api, ...).
[GeneratedMessagePackResolver]
internal sealed partial class CoreServerIntegrationTestsMessagePackResolver;
