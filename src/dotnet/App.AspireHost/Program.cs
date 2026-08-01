using System;
using System.IO;
using ActualChat;
using Microsoft.Extensions.Configuration;
using ActualLab;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DotNetEnv.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration.AddDotNetEnv(Path.Combine(AppContext.BaseDirectory, ".env"), new(setEnvVars: false));
var basePort = GetBasePort(builder.Configuration); // 7080, 7090, etc.
var meshLockSubspace = Alphabet.AlphaNumeric.Generator8.Next();
var meshLockOptionsPreset = "Default"; // Change it to "DebugFriendly" for debugging purposes
// AddHost("aio", basePort, "1:OneServer");
AddHost("api", basePort, "2:OneApiServer");
AddHost("backend1", basePort + 1, "2:OneBackendServer");
AddHost("backend2", basePort + 2, "2:OneBackendServer");
var app = builder.Build();
app.Run();

IResourceBuilder<ProjectResource> AddHost(string name, int port, string role)
{
    var args = $"-role:{role} -kb -distributed".Split(' ');
    return builder.AddProject<Projects.App_Server>(name, options => { options.ExcludeLaunchProfile = true; })
        .WithHttpEndpoint(port, isProxied: false)
        .WithEnvironment("HostSettings__IsAspireManaged", "true")
        .WithEnvironment("DOTNET_ENVIRONMENT", "Development") // Optional
        .WithEnvironment("HostSettings__MeshLockSubspace", meshLockSubspace)
        .WithEnvironment("HostSettings__MeshLockOptionsPreset", meshLockOptionsPreset)
        .WithArgs(args);
}

int GetBasePort(IConfiguration configuration)
{
    // HostSettings__BasePort is set by ai.ps1 in .env (e.g. 7090 for worktrees)
    if (int.TryParse(configuration["HostSettings:BasePort"], out var basePort))
        return basePort;
    return 7080; // Default matches nginx config
}
