using Microsoft.AspNetCore.Builder;

namespace ActualChat.Web.Module
{
    public interface IWebModule
    {
        void ConfigureApp(IApplicationBuilder app);
    }
}
