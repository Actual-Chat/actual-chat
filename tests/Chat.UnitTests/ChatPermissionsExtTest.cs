namespace ActualChat.Chat.UnitTests;

public class ChatPermissionsExtTest
{
    [Fact]
    public void WriteImpliesContentTypeFlags()
    {
        // act
        var permissions = ChatPermissions.Write.AddImplied();

        // assert
        permissions.Has(ChatPermissions.Read).Should().BeTrue();
        permissions.Has(ChatPermissions.Upload).Should().BeTrue();
        permissions.Has(ChatPermissions.WriteAudio).Should().BeTrue();
        permissions.Has(ChatPermissions.WriteVideo).Should().BeTrue();
        permissions.Has(ChatPermissions.ReadAudio).Should().BeTrue();
        permissions.Has(ChatPermissions.ReadVideo).Should().BeTrue();
    }

    [Fact]
    public void ReadImpliesAudioVideoReadFlags()
    {
        // act
        var permissions = ChatPermissions.Read.AddImplied();

        // assert
        permissions.Has(ChatPermissions.ReadAudio).Should().BeTrue();
        permissions.Has(ChatPermissions.ReadVideo).Should().BeTrue();
        permissions.Has(ChatPermissions.WriteAudio).Should().BeFalse();
        permissions.Has(ChatPermissions.WriteVideo).Should().BeFalse();
        permissions.Has(ChatPermissions.Upload).Should().BeFalse();
        permissions.Has(ChatPermissions.Write).Should().BeFalse();
    }

    [Fact]
    public void OwnerImpliesAllContentTypeFlags()
    {
        // act
        var permissions = ChatPermissions.Owner.AddImplied();

        // assert
        permissions.Has(ChatPermissions.Read).Should().BeTrue();
        permissions.Has(ChatPermissions.Write).Should().BeTrue();
        permissions.Has(ChatPermissions.Upload).Should().BeTrue();
        permissions.Has(ChatPermissions.WriteAudio).Should().BeTrue();
        permissions.Has(ChatPermissions.WriteVideo).Should().BeTrue();
        permissions.Has(ChatPermissions.ReadAudio).Should().BeTrue();
        permissions.Has(ChatPermissions.ReadVideo).Should().BeTrue();
    }

    [Fact]
    public void StrippingContentFlagsKeepsReadAndWrite()
    {
        // arrange
        var full = ChatPermissions.Write.AddImplied();

        // act - same masking used by GetPeerChatRules for non-contact peer chats
        var stripped = full & ~(ChatPermissions.Upload
            | ChatPermissions.WriteAudio
            | ChatPermissions.WriteVideo
            | ChatPermissions.ReadAudio
            | ChatPermissions.ReadVideo);

        // assert
        stripped.Has(ChatPermissions.Read).Should().BeTrue();
        stripped.Has(ChatPermissions.Write).Should().BeTrue();
        stripped.Has(ChatPermissions.Upload).Should().BeFalse();
        stripped.Has(ChatPermissions.WriteAudio).Should().BeFalse();
        stripped.Has(ChatPermissions.WriteVideo).Should().BeFalse();
        stripped.Has(ChatPermissions.ReadAudio).Should().BeFalse();
        stripped.Has(ChatPermissions.ReadVideo).Should().BeFalse();
    }
}
