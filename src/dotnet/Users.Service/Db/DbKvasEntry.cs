using System.ComponentModel.DataAnnotations;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbKvasEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    string IHasId<string>.Id => Key;
    [Key] public string Key { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Value { get; set; } = null!;
}
