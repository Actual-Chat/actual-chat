using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

/// <summary>
/// Primary author of an user. <br />
/// </summary>
[Table("UserAuthors")]
public class DbUserAuthor : IAuthorInfo
{
    [Key] public string UserId { get; set; } = null!;

    /// <summary> The url of the author avatar. </summary>
    public string? Picture { get; set; }

    /// <summary> @{Nickame}, e.g. @ivan </summary>
    public string Nickname { get; set; } = "";

    /// <summary> e.g. Ivan Ivanov </summary>
    public string Name { get; set; } = "";

    /// <summary> Is user want to be anonymous in chats by default. </summary>
    public bool IsAnonymous { get; set; }

    public UserAuthor ToModel() => new() {
        Name = Name,
        Nickname = Nickname,
        Picture = Picture,
        IsAnonymous = IsAnonymous,
    };
}
