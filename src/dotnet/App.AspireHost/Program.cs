using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);
// AddHost("aio", 7080, "1:OneServer");
AddHost("api", 7080, "2:OneApiServer");
AddHost("backend1", 7081, "2:OneBackendServer");
AddHost("backend2", 7082, "2:OneBackendServer");
var app = builder.Build();
app.Run();

IResourceBuilder<ProjectResource> AddHost(string name, int port, string role)
{
    var args = $"-role:{role} -kb -distributed".Split(' ');
    return builder.AddProject<Projects.App_Server>(name, options => { options.ExcludeLaunchProfile = true; })
        .WithHttpEndpoint(port)
        .WithEnvironment("HostSettings__IsAspireManaged", "true")
        .WithEnvironment("DOTNET_ENVIRONMENT", "Development") // Optional
        .WithArgs(args);
}
