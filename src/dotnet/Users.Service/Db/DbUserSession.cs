using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("user_sessions")]
[Index(nameof(SessionId))]
public class DbUserSession
{
    [StringLength(128)]
    public string UserId { get; set; } = "";
    [StringLength(128)]
    public string SessionId { get; set; } = "";
}
