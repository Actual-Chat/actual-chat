using ActualChat.Media.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Media;

public interface IMediaDbContext
{
    DbSet<DbMedia> Media { get; set; }
}
