using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Resources;

public static class AppStrings
{
    extension(IStringLocalizer l)
    {
        public string Common_Save => l["Common_Save"].Value;
        public string Common_Cancel => l["Common_Cancel"].Value;
        public string Common_Close => l["Common_Close"].Value;
        public string Common_Delete => l["Common_Delete"].Value;
        public string Common_OK => l["Common_OK"].Value;
        public string Common_Loading => l["Common_Loading"].Value;
        public string Common_Error => l["Common_Error"].Value;
        public string Common_Search => l["Common_Search"].Value;
        public string Common_Settings => l["Common_Settings"].Value;
        public string Common_Back => l["Common_Back"].Value;
        public string Common_Next => l["Common_Next"].Value;
        public string Common_Done => l["Common_Done"].Value;
        public string Common_Yes => l["Common_Yes"].Value;
        public string Common_No => l["Common_No"].Value;
        public string Common_Add => l["Common_Add"].Value;
        public string Common_Edit => l["Common_Edit"].Value;
        public string Common_Remove => l["Common_Remove"].Value;
        public string Common_Copy => l["Common_Copy"].Value;
        public string Common_Share => l["Common_Share"].Value;
        public string Common_Disabled => l["Common_Disabled"].Value;

        public string Settings_Title => l["Settings_Title"].Value;
        public string Settings_Language => l["Settings_Language"].Value;
        public string Settings_UILanguage => l["Settings_UILanguage"].Value;
        public string Settings_YourAccount => l["Settings_YourAccount"].Value;
        public string Settings_UserInterface => l["Settings_UserInterface"].Value;
        public string Settings_Transcription => l["Settings_Transcription"].Value;
        public string Settings_Application => l["Settings_Application"].Value;
        public string Settings_Sessions => l["Settings_Sessions"].Value;
        public string Settings_ApiKeys => l["Settings_ApiKeys"].Value;
        public string Settings_Documents => l["Settings_Documents"].Value;
        public string Settings_DeveloperTools => l["Settings_DeveloperTools"].Value;
        public string Settings_LogViewer => l["Settings_LogViewer"].Value;
        public string Settings_Privacy => l["Settings_Privacy"].Value;
        public string Settings_LogOut => l["Settings_LogOut"].Value;
        public string Settings_Quit_Format(object arg0) => l["Settings_Quit_Format", arg0].Value;

        public string UserInterface_Font => l["UserInterface_Font"].Value;
        public string UserInterface_Theme => l["UserInterface_Theme"].Value;
        public string UserInterface_WalkThrough => l["UserInterface_WalkThrough"].Value;
        public string UserInterface_RestartWalkThrough => l["UserInterface_RestartWalkThrough"].Value;
        public string UserInterface_Onboarding => l["UserInterface_Onboarding"].Value;
        public string UserInterface_RestartOnboarding => l["UserInterface_RestartOnboarding"].Value;
        public string UserInterface_WalkThroughRestarted => l["UserInterface_WalkThroughRestarted"].Value;
        public string UserInterface_OnboardingRestarted => l["UserInterface_OnboardingRestarted"].Value;

        public string YourAccount_Information => l["YourAccount_Information"].Value;
        public string YourAccount_Name => l["YourAccount_Name"].Value;
        public string YourAccount_UserLink => l["YourAccount_UserLink"].Value;
        public string YourAccount_Email => l["YourAccount_Email"].Value;
        public string YourAccount_Phone => l["YourAccount_Phone"].Value;
        public string YourAccount_TimeZone => l["YourAccount_TimeZone"].Value;
        public string YourAccount_TimeZoneNotSet => l["YourAccount_TimeZoneNotSet"].Value;
        public string YourAccount_Share => l["YourAccount_Share"].Value;
        public string YourAccount_ShareYourContact => l["YourAccount_ShareYourContact"].Value;
        public string YourAccount_MyAvatars => l["YourAccount_MyAvatars"].Value;

        public string AppSettings_TelemetryDataCollection => l["AppSettings_TelemetryDataCollection"].Value;
        public string AppSettings_AllowTelemetry => l["AppSettings_AllowTelemetry"].Value;
        public string AppSettings_TelemetryAllowed => l["AppSettings_TelemetryAllowed"].Value;
        public string AppSettings_TelemetryDisallowed => l["AppSettings_TelemetryDisallowed"].Value;
        public string AppSettings_TelemetryDescription_Format(object arg0) => l["AppSettings_TelemetryDescription_Format", arg0].Value;

        public string ThemeSettings_Light => l["ThemeSettings_Light"].Value;
        public string ThemeSettings_LightLinkWater => l["ThemeSettings_LightLinkWater"].Value;
        public string ThemeSettings_Dark => l["ThemeSettings_Dark"].Value;
        public string ThemeSettings_MatchSystem => l["ThemeSettings_MatchSystem"].Value;

        public string Transcription_Languages => l["Transcription_Languages"].Value;
        public string Transcription_PrimaryLanguage => l["Transcription_PrimaryLanguage"].Value;
        public string Transcription_SecondLanguage => l["Transcription_SecondLanguage"].Value;
        public string Transcription_ThirdLanguage => l["Transcription_ThirdLanguage"].Value;
        public string Transcription_None => l["Transcription_None"].Value;
        public string Transcription_Engines => l["Transcription_Engines"].Value;

        public string TranscriptionEngine_Deepgram => l["TranscriptionEngine_Deepgram"].Value;
        public string TranscriptionEngine_GoogleCloud => l["TranscriptionEngine_GoogleCloud"].Value;

        public string Sessions_CurrentSession => l["Sessions_CurrentSession"].Value;
        public string Sessions_Current => l["Sessions_Current"].Value;
        public string Sessions_Sessions => l["Sessions_Sessions"].Value;
        public string Sessions_LastActive_Format(object arg0) => l["Sessions_LastActive_Format", arg0].Value;
        public string Sessions_Today => l["Sessions_Today"].Value;
        public string Sessions_DaysAgo_One(object arg0) => l["Sessions_DaysAgo_One", arg0].Value;
        public string Sessions_DaysAgo_Other(object arg0) => l["Sessions_DaysAgo_Other", arg0].Value;
        public string Sessions_SignOut => l["Sessions_SignOut"].Value;
        public string Sessions_NoOtherSessions => l["Sessions_NoOtherSessions"].Value;
        public string Sessions_SignOutAll => l["Sessions_SignOutAll"].Value;
        public string Sessions_SignOutAllConfirm => l["Sessions_SignOutAllConfirm"].Value;
        public string Sessions_SignOutAllTitle => l["Sessions_SignOutAllTitle"].Value;

        public string ApiKeys_CreateApiKey => l["ApiKeys_CreateApiKey"].Value;
        public string ApiKeys_YourApiKeys => l["ApiKeys_YourApiKeys"].Value;
        public string ApiKeys_Deactivated => l["ApiKeys_Deactivated"].Value;
        public string ApiKeys_UnnamedKey => l["ApiKeys_UnnamedKey"].Value;
        public string ApiKeys_Deactivate => l["ApiKeys_Deactivate"].Value;
        public string ApiKeys_Expires_Format(object arg0) => l["ApiKeys_Expires_Format", arg0].Value;
        public string ApiKeys_DeactivateAll => l["ApiKeys_DeactivateAll"].Value;
        public string ApiKeys_DeactivateAllConfirm => l["ApiKeys_DeactivateAllConfirm"].Value;
        public string ApiKeys_DeactivateAllTitle => l["ApiKeys_DeactivateAllTitle"].Value;

        public string DevTools_GeneralEarlyAccess => l["DevTools_GeneralEarlyAccess"].Value;
        public string DevTools_VideoStreaming => l["DevTools_VideoStreaming"].Value;
        public string DevTools_VideoStreamingEnabled => l["DevTools_VideoStreamingEnabled"].Value;
        public string DevTools_Host => l["DevTools_Host"].Value;
        public string DevTools_EarlyAccess => l["DevTools_EarlyAccess"].Value;
        public string DevTools_EarlyAccessFeatures => l["DevTools_EarlyAccessFeatures"].Value;
        public string DevTools_EarlyAccessEnabled => l["DevTools_EarlyAccessEnabled"].Value;
        public string DevTools_EarlyAccessUI => l["DevTools_EarlyAccessUI"].Value;
        public string DevTools_EarlyAccessUIEnabled => l["DevTools_EarlyAccessUIEnabled"].Value;
        public string DevTools_Troubleshooting => l["DevTools_Troubleshooting"].Value;
        public string DevTools_EnableLogViewer => l["DevTools_EnableLogViewer"].Value;

        public string Documents_PrivacyPolicy => l["Documents_PrivacyPolicy"].Value;
        public string Documents_TermsConditions => l["Documents_TermsConditions"].Value;
        public string Documents_Cookies => l["Documents_Cookies"].Value;

        public string RenderMode_Title => l["RenderMode_Title"].Value;
        public string RenderMode_Auto => l["RenderMode_Auto"].Value;
        public string RenderMode_AutoDescription => l["RenderMode_AutoDescription"].Value;
        public string RenderMode_Server => l["RenderMode_Server"].Value;
        public string RenderMode_ServerDescription => l["RenderMode_ServerDescription"].Value;
        public string RenderMode_Wasm => l["RenderMode_Wasm"].Value;
        public string RenderMode_WasmDescription => l["RenderMode_WasmDescription"].Value;

        public string TimeZone_SelectTitle => l["TimeZone_SelectTitle"].Value;
        public string TimeZone_Updated => l["TimeZone_Updated"].Value;

        public string NativeApp_AutoStart => l["NativeApp_AutoStart"].Value;

        public string Email_Title => l["Email_Title"].Value;
        public string Email_Label => l["Email_Label"].Value;

        public string ApiKeyCreate_Title => l["ApiKeyCreate_Title"].Value;
        public string ApiKeyCreate_Name => l["ApiKeyCreate_Name"].Value;
        public string ApiKeyCreate_Placeholder => l["ApiKeyCreate_Placeholder"].Value;
        public string ApiKeyCreate_ExpiresInDays => l["ApiKeyCreate_ExpiresInDays"].Value;
        public string ApiKeyCreate_Create => l["ApiKeyCreate_Create"].Value;

        public string ApiKeyReveal_CopyWarning => l["ApiKeyReveal_CopyWarning"].Value;
        public string ApiKeyReveal_Title => l["ApiKeyReveal_Title"].Value;
    }
}
