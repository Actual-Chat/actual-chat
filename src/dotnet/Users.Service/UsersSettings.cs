using System;
using System.Text;
using Castle.Core.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace ActualChat.Users
{
    public class UsersSettings
    {
        // DBs
        public string Db { get; set; } = null!;

        // Auth provider settings
        public string MicrosoftAccountClientId { get; set; } = null!;
        public string MicrosoftAccountClientSecret { get; set; } = null!;
        public string GitHubClientId { get; set; } = null!;
        public string GitHubClientSecret { get; set; } = null!;
    }
}
