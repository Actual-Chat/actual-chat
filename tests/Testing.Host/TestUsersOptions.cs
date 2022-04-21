using System.ComponentModel.DataAnnotations;

namespace ActualChat.Testing.Host;

public class TestUsersOptions
{
    [Required] public TestUserCredentials User1 { get; set; } = null!;
}
