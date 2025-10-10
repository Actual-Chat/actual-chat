namespace ActualChat.Db;

public interface IDbEntity<TDbEntity, TModel>
    where TDbEntity : IDbEntity<TDbEntity, TModel>, new()
{
    void UpdateFrom(TModel model);
    TModel ToModel();
}
