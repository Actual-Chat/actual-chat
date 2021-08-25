using System;
using ActualChat.Db;

namespace ActualChat.Todos.Module
{
    public class TodosDbInitializer : DbInitializer<TodosDbContext>
    {
        public TodosDbInitializer(IServiceProvider services) : base(services) { }
    }
}
