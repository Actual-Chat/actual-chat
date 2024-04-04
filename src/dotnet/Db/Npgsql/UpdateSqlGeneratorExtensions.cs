using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace ActualChat.Db.Npgsql;

public class UpdateSqlGeneratorExtensions<TGenerator> : IDbContextOptionsExtension
    where TGenerator : IUpdateSqlGenerator
{
    public DbContextOptionsExtensionInfo Info { get; }

    public UpdateSqlGeneratorExtensions()
        => Info = new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.RemoveAll(sd => sd.ServiceType == typeof(IUpdateSqlGenerator));
        services.AddSingleton(
            typeof(IUpdateSqlGenerator),
            typeof(TGenerator));
    }

    public void Validate(IDbContextOptions options)
    { }

    private class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => nameof(UpdateSqlGeneratorExtensions<TGenerator>);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => string.Equals(LogFragment, other.LogFragment, StringComparison.Ordinal);

        public override int GetServiceProviderHashCode() => 0;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
    }
}
