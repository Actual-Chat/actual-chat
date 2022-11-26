namespace ActualChat;

public static class DbModelExt
{
    public static TDbModel RequireSameId<TDbModel>(this TDbModel model, string id)
        where TDbModel : IHasId<string>
    {
        if (OrdinalEquals(model.Id, id))
            return model;
        throw StandardError.Internal("Model is updated from a source with unexpected Id.");
    }

    public static TDbModel RequireSameOrEmptyId<TDbModel>(this TDbModel model, string id)
        where TDbModel : IHasId<string>
    {
        if (model.Id.IsNullOrEmpty())
            return model;
        if (OrdinalEquals(model.Id, id))
            return model;
        throw StandardError.Internal("Model is updated from a source with unexpected Id.");
    }
}
