namespace ActualChat.Core.UnitTests.DependencyInjection;

public class CompositeServiceProviderTest
{
    [Fact]
    public async Task BasicTest()
    {
        var nonLazy = new From();
        var lazy = new FromLazy();
        var nonLazyServices = new ServiceCollection()
            .AddScoped(_ => nonLazy)
            .AddSingleton<Singleton>()
            .AddScoped<Scoped>()
            .BuildServiceProvider();

        var lazyServicesTsc = TaskCompletionSourceExt.New<IServiceProvider>();
        await using var lazyServices = new CompositeServiceProvider(
            nonLazyServices,
            lazyServicesTsc.Task,
            t => t != typeof(LazyScopedFiltered),
            nonLazyServices);

        var whenTested = Task.Run(async () => {
            // ReSharper disable AccessToDisposedClosure
            await Test(lazyServices.CreateScope().ServiceProvider, lazyServices);
            await Test(lazyServices);
            // ReSharper restore AccessToDisposedClosure
        });

        var lazyServicesSource = new ServiceCollection()
            .AddScoped(_ => (From)lazy)
            .AddScoped<NonLazyServiceAccessor>()
            .AddSingleton(c => c.GetRequiredService<NonLazyServiceAccessor>().GetRequiredService<Singleton>())
            .AddScoped(c => c.GetRequiredService<NonLazyServiceAccessor>().GetRequiredService<Scoped>())
            .AddSingleton<LazySingleton>()
            .AddScoped<LazyScoped>()
            .AddScoped<LazyScopedFiltered>()
            .BuildServiceProvider();
        lazyServicesTsc.SetResult(lazyServicesSource);
        await whenTested;

        async Task Test(IServiceProvider services, IServiceProvider? root = null)
        {
            var single = services.GetRequiredService<Singleton>();
            single.From.Should().Be(nonLazy);
            var scoped = services.GetRequiredService<Scoped>();
            scoped.From.Should().Be(nonLazy);

            var lazySingle = services.GetRequiredService<LazySingleton>();
            lazySingle.From.Should().Be(lazy);
            var lazyScoped = services.GetRequiredService<LazyScoped>();
            lazyScoped.From.Should().Be(lazy);

            services.GetService<LazyScopedFiltered>().Should().BeNull();

            if (root != null) {
                var rootSingle = root.GetRequiredService<Singleton>();
                var rootScoped = root.GetRequiredService<Scoped>();

                single.Should().BeSameAs(rootSingle);
                lazySingle.Should().BeSameAs(root.GetRequiredService<LazySingleton>());
                lazySingle.Singleton.Should().BeSameAs(rootSingle);
                lazyScoped.Singleton.Should().BeSameAs(rootSingle);
                lazyScoped.Scoped.Should().NotBeSameAs(rootScoped);

                await DisposableExt.DisposeUnknownSilently(services);
                single.IsDisposed.Should().BeFalse();
                scoped.IsDisposed.Should().BeTrue();
                lazySingle.IsDisposed.Should().BeFalse();
                lazyScoped.IsDisposed.Should().BeTrue();
            }
            else {
                await DisposableExt.DisposeUnknownSilently(services);
                single.IsDisposed.Should().BeTrue();
                scoped.IsDisposed.Should().BeTrue();
                lazySingle.IsDisposed.Should().BeTrue();
                lazyScoped.IsDisposed.Should().BeTrue();
            }
        }
    }

    // Nested types

    public record Base: IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    public record From();
    public record FromLazy() : From;
    public record Singleton(From From) : Base;
    public record Scoped(From From) : Base;
    public record LazySingleton(From From, Singleton Singleton) : Base;
    public record LazyScoped(From From, Singleton Singleton, Scoped Scoped) : Base;
    public record LazyScopedFiltered(From From) : Base;
}
