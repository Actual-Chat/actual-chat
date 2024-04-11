using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;
using ICommand = ActualLab.CommandR.ICommand;

namespace ActualChat.Chat.IntegrationTests;

public class JsonSerializationOutputTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void DumpWarmupJson()
    {
        Dump(new UserLanguageSettings() { Primary = Languages.French });
        Dump(ApiArray.New(new ActiveChat(ChatId.ParseOrNone("dpwo1tm0tw"))));
        Dump(new UserOnboardingSettings() { IsAvatarStepCompleted = true });
        Dump(new ChatListSettings(ChatListOrder.ByAlphabet));
        Dump(new UserBubbleSettings() { ReadBubbles = ApiArray.New("x") });
    }

    [Fact]
    public void SerializeOperation()
    {
        var command = new NewtonsoftJsonSerialized<ICommand>() {
            Value = new ChatsBackend_ChangeEntry(ChatEntryId.None, null, Change.Create(new ChatEntryDiff())),
        };
        var data = command.Data;
        data.Should().NotContain("Attachments");
    }

    [Fact]
    public void DeserializeOperation()
    {
        const string op = """
                          {
                              "$type": "ActualChat.Chat.ChatsBackend_ChangeEntry, ActualChat.Chat.Contracts",
                              "ChatEntryId": "R6Y6HAwGZW:0:0",
                              "Change": {
                                  "Create": {
                                      "HasValue": true,
                                      "ValueOrDefault": {
                                          "AuthorId": "R6Y6HAwGZW:1",
                                          "ClientSideBeginsAt": {
                                              "HasValue": false
                                          },
                                          "EndsAt": {
                                              "HasValue": false
                                          },
                                          "ContentEndsAt": {
                                              "HasValue": false
                                          },
                                          "Content": "ggg",
                                          "SystemEntry": {
                                              "HasValue": false
                                          },
                                          "AudioEntryId": {
                                              "HasValue": false
                                          },
                                          "VideoEntryId": {
                                              "HasValue": false
                                          },
                                          "RepliedEntryLocalId": {
                                              "HasValue": true
                                          },
                                          "ForwardedChatEntryId": "",
                                          "ForwardedAuthorId": "",
                                          "ForwardedChatEntryBeginsAt": {
                                              "HasValue": true
                                          },
                                          "Attachments": []
                                      }
                                  },
                                  "Update": {
                                      "HasValue": false
                                  },
                                  "Remove": false
                              }
                          }
                          """;

        var command = new NewtonsoftJsonSerialized<ICommand>() {
            Data = op,
        };
        command.Value.Should().NotBeNull();
    }

    private void Dump<T>(T instance)
    {
        var s = SystemJsonSerializer.Default;
        Out.WriteLine($"{typeof(T).GetName()}:");
        Out.WriteLine("\"" + s.Write(instance).Replace("\"", "\\\"", StringComparison.OrdinalIgnoreCase) + "\"");
        Out.WriteLine("");
    }
}
