namespace ActualChat.Core.Server.UnitTests.Sharding;

public sealed class MaintenanceShardTest
{
    [Fact]
    public void MaintenanceShouldUseItsOwnSchemeOnUsersHosts()
    {
        // act
        var scheme = ShardScheme.ForType(typeof(IMaintenancesBackend));
        var definition = new BackendServiceDef(
            typeof(IMaintenancesBackend), typeof(object), ServiceMode.Distributed, HostRole.UsersBackend);

        // assert
        scheme.Should().BeSameAs(ShardScheme.MaintenanceBackend);
        definition.ShardScheme.Should().BeSameAs(scheme);
        scheme!.ShardCount.Should().Be((int)MaintenanceKey.ShardCount);
        scheme.HostRole.Should().Be(HostRole.UsersBackend);
        ShardScheme.ByBackendHostRole[HostRole.UsersBackend.Id].Should().BeSameAs(ShardScheme.UsersBackend);
        for (var shard = 0u; shard < MaintenanceKey.ShardCount; shard++) {
            var key = new MaintenanceKey(
                "same-key",
                new ShardKey(shard << 28).Head(MaintenanceKey.FullPartitionKeySize));
            scheme.GetShardIndex(key).Should().Be((int)shard);
        }
    }
}
