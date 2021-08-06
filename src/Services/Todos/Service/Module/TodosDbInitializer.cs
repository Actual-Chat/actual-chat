using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Todos.Module
{
    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class TodosDbInitializer : DbInitializer<TodosDbContext>
    {
        public TodosDbInitializer(IServiceProvider services) : base(services) { }
    }
}
