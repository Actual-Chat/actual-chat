﻿using Stl.DependencyInjection;

namespace ActualChat.Chat
{
    public class ChatSettings
    {
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_chat;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07;Include Error Detail=true";

        public string DefaultChatId { get; set; } = null!;
    }
}
