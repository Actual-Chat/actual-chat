namespace ActualChat.Db;

public static class DbEntityExt
{
    extension<TDbEntity, TModel>(IDbEntity<TDbEntity, TModel> entity)
        where TDbEntity : IDbEntity<TDbEntity, TModel>, new()
    {
        public static IDbEntity<TDbEntity, TModel> FromModel(TModel model)
        {
            var e = new TDbEntity();
            e.UpdateFrom(model);
            return e;
        }
    }
}
