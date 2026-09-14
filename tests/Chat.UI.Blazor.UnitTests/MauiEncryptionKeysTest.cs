using ActualChat.Maui;
using Microsoft.Maui.Storage;
using Moq;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class MauiEncryptionKeysTest
{
    private const string KeyName = "db_encryption_key";
    private const string EncodedKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string LegacyKey = "\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\"";

    [Fact]
    public async Task LegacyKeyShouldMigrateWithoutChangingBytes()
    {
        // arrange
        var stores = new Stores { LegacyValue = LegacyKey };
        var keys = stores.CreateKeys();

        // act
        await keys.WhenReady;

        // assert
        keys.DbEncryptionKey.Should().Equal(Enumerable.Range(0, 32).Select(x => (byte)x));
        stores.SecureValue.Should().Be(EncodedKey);
        stores.LegacyValue.Should().BeNull();
        stores.Operations.Should().Equal(
            "read secure", "read preferences", "write secure", "read secure", "remove preferences");
    }

    [Fact]
    public async Task SecureKeyShouldWinAndRemoveLeftoverPreferences()
    {
        // arrange
        var stores = new Stores { SecureValue = EncodedKey, LegacyValue = "invalid legacy value" };
        var keys = stores.CreateKeys();

        // act
        await keys.WhenReady;

        // assert
        keys.DbEncryptionKey.Should().Equal(Convert.FromBase64String(EncodedKey));
        stores.LegacyValue.Should().BeNull();
        stores.Operations.Should().Equal("read secure", "remove preferences");
    }

    [Fact]
    public async Task MissingKeyShouldBeGeneratedAndSurviveRestart()
    {
        // arrange
        var stores = new Stores();
        var keys = stores.CreateKeys();

        // act
        await keys.WhenReady;
        var restartedKeys = stores.CreateKeys();
        await restartedKeys.WhenReady;

        // assert
        keys.DbEncryptionKey.Should().HaveCount(32).And.Contain(x => x != 0);
        restartedKeys.DbEncryptionKey.Should().Equal(keys.DbEncryptionKey);
        Convert.FromBase64String(stores.SecureValue!).Should().Equal(keys.DbEncryptionKey);
        stores.LegacyValue.Should().BeNull();
    }

    [Fact]
    public async Task ReadinessShouldBeLazyAndSharedAcrossConcurrentReaders()
    {
        // arrange
        var stores = new Stores();
        var readSource = TaskCompletionSourceExt.New<string?>();
        stores.Storage.SetupSequence(x => x.GetAsync(KeyName))
            .Returns(readSource.Task)
            .Returns(() => Task.FromResult(stores.SecureValue));
        var keys = stores.CreateKeys();
        stores.Operations.Should().BeEmpty();

        // act
        var readyTasks = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => Task.Run(() => (object)keys.WhenReady)));
        readSource.SetResult(null);
        await Task.WhenAll(readyTasks.Cast<Task>());

        // assert
        readyTasks.Should().OnlyContain(x => ReferenceEquals(x, keys.WhenReady));
        stores.Storage.Verify(x => x.GetAsync(KeyName), Times.Exactly(2));
        stores.Operations.Count(x => x == "write secure").Should().Be(1);
    }

    [Fact]
    public async Task FailedSecureWriteShouldPreserveLegacyKeyAndKeepKeyUnavailable()
    {
        // arrange
        var stores = new Stores { LegacyValue = LegacyKey, WriteError = new IOException("Storage unavailable") };
        var keys = stores.CreateKeys();

        // act
        var initialize = () => keys.WhenReady;

        // assert
        await initialize.Should().ThrowAsync<IOException>();
        stores.LegacyValue.Should().Be(LegacyKey);
        stores.SecureValue.Should().BeNull();
        var readKey = () => keys.DbEncryptionKey;
        readKey.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task FailedSecureReadShouldNotFallBackOrGenerate()
    {
        // arrange
        var stores = new Stores { LegacyValue = LegacyKey };
        stores.Storage.Setup(x => x.GetAsync(KeyName)).ThrowsAsync(new IOException("Storage unavailable"));
        var keys = stores.CreateKeys();

        // act
        var initialize = () => keys.WhenReady;

        // assert
        await initialize.Should().ThrowAsync<IOException>();
        stores.LegacyValue.Should().Be(LegacyKey);
        stores.SecureValue.Should().BeNull();
        stores.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad base64")]
    [InlineData("AAE=")]
    public async Task InvalidSecureKeyShouldNotBeReplaced(string value)
    {
        // arrange
        var stores = new Stores { SecureValue = value, LegacyValue = LegacyKey };
        var keys = stores.CreateKeys();

        // act
        var initialize = () => keys.WhenReady;

        // assert
        await initialize.Should().ThrowAsync<Exception>();
        stores.SecureValue.Should().Be(value);
        stores.LegacyValue.Should().Be(LegacyKey);
        stores.Operations.Should().Equal("read secure");
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad json")]
    [InlineData("null")]
    [InlineData("\"AAE=\"")]
    public async Task InvalidLegacyKeyShouldNotBeReplaced(string value)
    {
        // arrange
        var stores = new Stores { LegacyValue = value };
        var keys = stores.CreateKeys();

        // act
        var initialize = () => keys.WhenReady;

        // assert
        await initialize.Should().ThrowAsync<Exception>();
        stores.SecureValue.Should().BeNull();
        stores.LegacyValue.Should().Be(value);
        stores.Operations.Should().Equal("read secure", "read preferences");
    }

    [Fact]
    public async Task InterruptedCleanupShouldRecoverUsingPersistedKey()
    {
        // arrange
        var stores = new Stores { LegacyValue = LegacyKey, RemoveError = new IOException("Preferences unavailable") };
        var keys = stores.CreateKeys();
        var initialize = () => keys.WhenReady;
        await initialize.Should().ThrowAsync<IOException>();
        stores.SecureValue.Should().Be(EncodedKey);
        stores.LegacyValue.Should().Be(LegacyKey);

        // act
        stores.RemoveError = null;
        var restartedKeys = stores.CreateKeys();
        await restartedKeys.WhenReady;

        // assert
        restartedKeys.DbEncryptionKey.Should().Equal(Convert.FromBase64String(EncodedKey));
        stores.LegacyValue.Should().BeNull();
        stores.Operations.Count(x => x == "write secure").Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public async Task UnverifiedSecureWriteShouldPreserveLegacyKey(string? persistedValue)
    {
        // arrange
        var stores = new Stores { LegacyValue = LegacyKey };
        stores.Storage.Setup(x => x.SetAsync(KeyName, It.IsAny<string>()))
            .Callback(() => stores.SecureValue = persistedValue)
            .Returns(Task.CompletedTask);
        var keys = stores.CreateKeys();

        // act
        var initialize = () => keys.WhenReady;

        // assert
        await initialize.Should().ThrowAsync<IOException>();
        stores.LegacyValue.Should().Be(LegacyKey);
        var readKey = () => keys.DbEncryptionKey;
        readKey.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task MigrationShouldEvictThePreferencesCacheEntry()
    {
        // arrange
        MauiPreferences.RemoveCached(KeyName);
        MauiPreferences.Get<byte[]>(KeyName).Should().BeNull();
        var stores = new Stores { LegacyValue = LegacyKey };
        var keys = stores.CreateKeys();

        // act
        await keys.WhenReady;

        // assert
        var read = () => MauiPreferences.Get<byte[]>(KeyName,
            () => throw new InvalidOperationException("Cache miss"));
        read.Should().Throw<InvalidOperationException>().WithMessage("Cache miss");
    }

    // Nested types

    private sealed class Stores
    {
        public Mock<ISecureStorage> Storage { get; } = new(MockBehavior.Strict);
        public Mock<IPreferences> Preferences { get; } = new(MockBehavior.Strict);
        public List<string> Operations { get; } = [];
        public string? SecureValue { get; set; }
        public string? LegacyValue { get; set; }
        public Exception? WriteError { get; set; }
        public Exception? RemoveError { get; set; }

        public Stores()
        {
            Storage.Setup(x => x.GetAsync(KeyName)).Returns(() => {
                Operations.Add("read secure");
                return Task.FromResult(SecureValue);
            });
            Storage.Setup(x => x.SetAsync(KeyName, It.IsAny<string>())).Returns((string _, string value) => {
                Operations.Add("write secure");
                if (WriteError != null)
                    throw WriteError;

                SecureValue = value;
                return Task.CompletedTask;
            });
            Preferences.Setup(x => x.Get<string?>(KeyName, null, null)).Returns(() => {
                Operations.Add("read preferences");
                return LegacyValue;
            });
            Preferences.Setup(x => x.Remove(KeyName, null)).Callback(() => {
                Operations.Add("remove preferences");
                if (RemoveError != null)
                    throw RemoveError;

                LegacyValue = null;
            });
        }

        public MauiEncryptionKeys CreateKeys()
            => new(Storage.Object, Preferences.Object);
    }
}
