using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class AuthorsBackend
{
    // Not a [ComputeMethod]!
    public async Task<AuthorFull[]> ListChanged(
        ChangedAuthorsQuery query,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var authorsQuery = query.LastId is null
            ? dbContext.Authors.Where(x => x.Version >= query.MinVersion && x.Version <= query.MaxVersion)
            : dbContext.Authors.Where(x => (x.Version > query.MinVersion && x.Version <= query.MaxVersion)
                || (x.Version==query.MinVersion && string.Compare(x.Id, query.LastId.Value) > 0));

        var dbAuthors = await authorsQuery
            .WhereIf(x => x.IsPlaceAuthor == query.IsPlaceAuthor, query.IsPlaceAuthor != null)
            .OrderBy(x => x.Version)
            .ThenBy(x => x.Id)
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbAuthors.Select(x => x.ToModel()).ToArray();
    }
}
