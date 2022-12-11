namespace ActualChat;

public static class DbModelExt
{
    public static TDbModel RequireSameId<TDbModel>(this TDbModel dbModel, string id)
        where TDbModel : IHasId<string>
    {
        if (OrdinalEquals(dbModel.Id, id))
            return dbModel;
        throw StandardError.Internal("Model is updated from a source with unexpected Id.");
    }

    public static TDbModel RequireSameOrEmptyId<TDbModel>(this TDbModel dbModel, string id)
        where TDbModel : IHasId<string>
    {
        if (dbModel.Id.IsNullOrEmpty())
            return dbModel;
        if (OrdinalEquals(dbModel.Id, id))
            return dbModel;
        throw StandardError.Internal("Model is updated from a source with unexpected Id.");
    }
}
