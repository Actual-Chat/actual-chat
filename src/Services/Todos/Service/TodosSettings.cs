using Stl.DependencyInjection;

namespace ActualChat.Todos
{
    [RegisterSettings("ActualChat.Todos")]
    public class TodosSettings
    {
        // DBs
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_todos;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";
    }
}
