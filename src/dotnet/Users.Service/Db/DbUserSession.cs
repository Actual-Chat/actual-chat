using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("user_sessions")]
[Index(nameof(UserId))]
public class DbUserSession
{
    [StringLength(256)]
    public string UserId { get; set; } = "";

    [StringLength(256)]
    public string SessionId { get; set; } = "";

    public bool IsApiKey { get; set; }
}
