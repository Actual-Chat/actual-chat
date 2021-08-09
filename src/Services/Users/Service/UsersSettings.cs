using System;
using System.Text;
using Stl.DependencyInjection;

namespace ActualChat.Users
{
    public class UsersSettings
    {
        // DBs
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_users;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";

        // Auth provider settings
        public string MicrosoftAccountClientId { get; set; } = "6839dbf7-d1d3-4eb2-a7e1-ce8d48f34d00";
        public string MicrosoftAccountClientSecret { get; set; } =
            Encoding.UTF8.GetString(Convert.FromBase64String(
                "REFYeH4yNTNfcVNWX2h0WkVoc1V6NHIueDN+LWRxUTA2Zw=="));
        public string GitHubClientId { get; set; } = "7a38bc415f7e1200fee2";
        public string GitHubClientSecret { get; set; } =
            Encoding.UTF8.GetString(Convert.FromBase64String(
                "OGNkMTAzM2JmZjljOTk3ODc5MjhjNTNmMmE3Y2Q1NWU0ZmNlNjU0OA=="));

    }
}
