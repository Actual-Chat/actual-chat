using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActualChat.Chat.UI.Blazor.Services
{
    public class TranscriptionLanguagePreferences
    {
        private string _language = "ru-Ru";

        public string Language {
            get => _language;
            set {
                if (_language != value) {
                    _language = value;
                    LanguageChanged();
                }
            }
        }

        public event Action LanguageChanged = delegate { };
    }
}
