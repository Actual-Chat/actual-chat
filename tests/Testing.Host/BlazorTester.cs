using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Server;
using ActualChat.Chat;
using ActualChat.Notification;
using ActualChat.Search;
using ActualChat.Users;
using ActualLab.Versioning;
using Bunit;

namespace ActualChat.Testing.Host;

public class BlazorTester : TestContext, IWebTester
{
    private readonly IServiceScope _serviceScope;

    public AppHost AppHost { get; }
    [field: AllowNull, MaybeNull]
    public IServiceProvider AppServices => field ??= AppHost.Services;
    public IServiceProvider ScopedAppServices => _serviceScope.ServiceProvider;
    [field: AllowNull, MaybeNull]
    public ICommander Commander => field ??= AppServices.Commander();
    [field: AllowNull, MaybeNull]
    public IAuth Auth => field ??= AppServices.GetRequiredService<IAuth>();
    [field: AllowNull, MaybeNull]
    public IAccounts Accounts => field ??= AppServices.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    public IAccountsBackend AccountsBackend => field ??= AppServices.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    public IChats Chats => field ??= AppServices.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    public IPlaces Places => field ??= AppServices.GetRequiredService<IPlaces>();
    [field: AllowNull, MaybeNull]
    public ISearch Search => field ??= AppServices.GetRequiredService<ISearch>();
    [field: AllowNull, MaybeNull]
    public IAuthBackend AuthBackend => field ??= AppServices.GetRequiredService<IAuthBackend>();
    [field: AllowNull, MaybeNull]
    public INotificationsBackend NotificationsBackend  => field ??= AppServices.GetRequiredService<INotificationsBackend>();
    public Session Session { get; }
    [field: AllowNull, MaybeNull]
    public UrlMapper UrlMapper => field ??= AppServices.UrlMapper();
    [field: AllowNull, MaybeNull]
    public VersionGenerator<long> VersionGenerator => field ??= AppServices.VersionGenerator<long>();
    public ITestOutputHelper Out { get; }

    public BlazorTester(AppHost appHost, ITestOutputHelper @out)
    {
        AppHost = appHost;
        Out = @out;
        _serviceScope = AppServices.CreateScope();
        Services.AddFallbackServiceProvider(ScopedAppServices);

        Session = Session.New();
        var sessionResolver = ScopedAppServices.GetRequiredService<ISessionResolver>();
        sessionResolver.Session = Session;

        Services.AddTransient(_ => ScopedAppServices.StateFactory());
    }

#pragma warning disable CA2215 // Ensure method calls base.Dispose(bool)
    protected override void Dispose(bool disposing)
#pragma warning restore CA2215
    {
        if (disposing)
            _serviceScope.DisposeSilently();
        // base.Dispose(disposing);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
        return default;
    }
}
