using ActualChat.DependencyInjection;
using IServiceProvider = System.IServiceProvider;

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
            .AddSingleton<Single>()
            .AddScoped<Scoped>()
            .BuildServiceProvider();

        var lazyServicesTsc = TaskCompletionSourceExt.New<IServiceProvider>();
        var lazyServices = new CompositeServiceProvider(
            nonLazyServices,
            lazyServicesTsc.Task,
            t => t != typeof(LazyScopedFiltered),
            nonLazyServices);

        var whenTested = Task.Run(async () => {
            await Test(lazyServices.CreateScope().ServiceProvider, lazyServices);
            await Test(lazyServices);
        });

        var lazyServicesSource = new ServiceCollection()
            .AddScoped(_ => (From)lazy)
            .AddScoped<NonLazyServiceAccessor>()
            .AddSingleton(c => c.GetRequiredService<NonLazyServiceAccessor>().GetRequiredService<Single>())
            .AddScoped(c => c.GetRequiredService<NonLazyServiceAccessor>().GetRequiredService<Scoped>())
            .AddSingleton<LazySingle>()
            .AddScoped<LazyScoped>()
            .AddScoped<LazyScopedFiltered>()
            .BuildServiceProvider();
        lazyServicesTsc.SetResult(lazyServicesSource);
        await whenTested;

        async Task Test(IServiceProvider services, IServiceProvider? root = null)
        {
            var single = services.GetRequiredService<Single>();
            single.From.Should().Be(nonLazy);
            var scoped = services.GetRequiredService<Scoped>();
            scoped.From.Should().Be(nonLazy);

            var lazySingle = services.GetRequiredService<LazySingle>();
            lazySingle.From.Should().Be(lazy);
            var lazyScoped = services.GetRequiredService<LazyScoped>();
            lazyScoped.From.Should().Be(lazy);

            services.GetService<LazyScopedFiltered>().Should().BeNull();

            if (root != null) {
                var rootSingle = root.GetRequiredService<Single>();
                var rootScoped = root.GetRequiredService<Scoped>();

                single.Should().BeSameAs(rootSingle);
                lazySingle.Should().BeSameAs(root.GetRequiredService<LazySingle>());
                lazySingle.Single.Should().BeSameAs(rootSingle);
                lazyScoped.Single.Should().BeSameAs(rootSingle);
                lazyScoped.Scoped.Should().NotBeSameAs(rootScoped);

                await services.SafelyDisposeAsync();
                single.IsDisposed.Should().BeFalse();
                scoped.IsDisposed.Should().BeTrue();
                lazySingle.IsDisposed.Should().BeFalse();
                lazyScoped.IsDisposed.Should().BeTrue();
            }
            else {
                await services.SafelyDisposeAsync();
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
            => IsDisposed = true;
    }

    public record From();
    public record FromLazy() : From;
    public record Single(From From) : Base;
    public record Scoped(From From) : Base;
    public record LazySingle(From From, Single Single) : Base;
    public record LazyScoped(From From, Single Single, Scoped Scoped) : Base;
    public record LazyScopedFiltered(From From) : Base;
}
