namespace ActualChat.Db;

// NOTE(AY): This code requires C# 14, which isn't enabled on GitHub builders yet;
//           on the other hand, the type isn't used.
/*

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

*/
