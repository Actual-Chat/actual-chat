namespace ActualChat.App.Maui
{
    public partial class App : Application
    {
        private ILogger Log { get; }

        protected override void OnAppLinkRequestReceived(Uri uri)
        {
            Log.LogDebug("OnAppLinkRequestReceived: '{Uri}'", uri);
            Services.AppLinks.OnAppLinkRequestReceived(uri);
        }

        public App(MainPage mainPage, ILogger<App> log)
        {
            Log = log;
            InitializeComponent();
            MainPage = mainPage;
        }
    }
}
