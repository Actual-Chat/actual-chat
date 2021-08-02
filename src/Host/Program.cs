using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ActualChat.Host;
using ActualChat.Todos;
using ActualChat.Users;
using ActualChat.Voice;

var host = Host.CreateDefaultBuilder()
    .ConfigureHostConfiguration(builder => {
        // Looks like there is no better way to set _default_ URL
        builder.Sources.Insert(0, new MemoryConfigurationSource() {
            InitialData = new Dictionary<string, string>() {
                {WebHostDefaults.ServerUrlsKey, "http://localhost:7080"},
            }
        });
    })
    .ConfigureWebHostDefaults(builder => builder
        .UseDefaultServiceProvider((ctx, options) => {
            if (ctx.HostingEnvironment.IsDevelopment()) {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            }
        })
        .UseStartup<Startup>())
    .Build();

// Ensure the DBs are created
var usersDbContextFactory = host.Services.GetRequiredService<IDbContextFactory<UsersDbContext>>();
await using var usersDbContext = usersDbContextFactory.CreateDbContext();
await usersDbContext.Database.EnsureDeletedAsync();
await usersDbContext.Database.EnsureCreatedAsync();

var todosDbContextFactory = host.Services.GetRequiredService<IDbContextFactory<TodosDbContext>>();
await using var todosDbContext = todosDbContextFactory.CreateDbContext();
await todosDbContext.Database.EnsureDeletedAsync();
await todosDbContext.Database.EnsureCreatedAsync();

var voiceDbContextFactory = host.Services.GetRequiredService<IDbContextFactory<VoiceDbContext>>();
await using var voiceDbContext = voiceDbContextFactory.CreateDbContext();
await voiceDbContext.Database.EnsureDeletedAsync();
await voiceDbContext.Database.EnsureCreatedAsync();

await host.RunAsync();
