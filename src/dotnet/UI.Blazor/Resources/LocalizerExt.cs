using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.Resources;

/// <summary>
/// One typed member per catalog key, as extension members on <see cref="IStringLocalizer"/>:
/// <c>L.Common_Save</c> rather than <c>L["Common_Save"]</c>, so a typo is a compile error.
/// <c>AppLocalizationTest</c> keeps the members and the English keys in exact correspondence.
/// </summary>
public static class LocalizerExt
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
        public string Common_Listen => l["Common_Listen"].Value;
        public string Common_Mute => l["Common_Mute"].Value;
        public string Common_Start => l["Common_Start"].Value;
        public string Common_Stop => l["Common_Stop"].Value;
        public string Common_Optional => l["Common_Optional"].Value;
        public string Common_Verified => l["Common_Verified"].Value;
        public string Common_Unverified => l["Common_Unverified"].Value;

        public string Settings_Title => l["Settings_Title"].Value;
        public string Settings_Language => l["Settings_Language"].Value;
        public string Settings_UILanguage => l["Settings_UILanguage"].Value;
        public string Settings_YourAccount => l["Settings_YourAccount"].Value;
        public string Settings_UserInterface => l["Settings_UserInterface"].Value;
        public string Settings_Transcription => l["Settings_Transcription"].Value;
        public string Settings_PushToTalk => l["Settings_PushToTalk"].Value;
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
        public string YourAccount_EditOrDelete => l["YourAccount_EditOrDelete"].Value;
        public string YourAccount_EditOrDeleteCaption => l["YourAccount_EditOrDeleteCaption"].Value;
        public string YourAccount_Share => l["YourAccount_Share"].Value;
        public string YourAccount_ShareYourContact => l["YourAccount_ShareYourContact"].Value;
        public string YourAccount_MyAvatars => l["YourAccount_MyAvatars"].Value;

        public string AppSettings_TelemetryDataCollection => l["AppSettings_TelemetryDataCollection"].Value;
        public string AppSettings_AllowTelemetry => l["AppSettings_AllowTelemetry"].Value;
        public string AppSettings_TelemetryAllowed => l["AppSettings_TelemetryAllowed"].Value;
        public string AppSettings_TelemetryDisallowed => l["AppSettings_TelemetryDisallowed"].Value;
        public string AppSettings_TelemetryDescription_Format(object arg0)
            => l["AppSettings_TelemetryDescription_Format", arg0].Value;

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
        public string Transcription_Automatic => l["Transcription_Automatic"].Value;
        public string Transcription_NoRetranscription_Format(object arg0)
            => l["Transcription_NoRetranscription_Format", arg0].Value;
        public string Transcription_Retranscription_Format(object arg0, object arg1)
            => l["Transcription_Retranscription_Format", arg0, arg1].Value;


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

        public string ChatList_TabAll => l["ChatList_TabAll"].Value;
        public string ChatList_TabGroups => l["ChatList_TabGroups"].Value;
        public string ChatList_TabPeople => l["ChatList_TabPeople"].Value;
        public string ChatList_TabThreads => l["ChatList_TabThreads"].Value;
        public string ChatList_SortBy => l["ChatList_SortBy"].Value;
        public string ChatList_SortByAnyonesActivity => l["ChatList_SortByAnyonesActivity"].Value;
        public string ChatList_SortByOwnActivity => l["ChatList_SortByOwnActivity"].Value;
        public string ChatList_SortByUnreadCount => l["ChatList_SortByUnreadCount"].Value;
        public string ChatList_SortByAlphabet => l["ChatList_SortByAlphabet"].Value;
        public string ChatList_ActiveChats => l["ChatList_ActiveChats"].Value;
        public string ChatList_ActiveChatsTooltip => l["ChatList_ActiveChatsTooltip"].Value;
        public string ChatList_Talking => l["ChatList_Talking"].Value;
        public string ChatList_Live_Format(object arg0) => l["ChatList_Live_Format", arg0].Value;
        public string ChatList_Thread_Format(object arg0) => l["ChatList_Thread_Format", arg0].Value;
        public string ChatList_SharedLiveLocation => l["ChatList_SharedLiveLocation"].Value;
        public string ChatList_SentLocation => l["ChatList_SentLocation"].Value;

        public string ChatMenu_AddMembers => l["ChatMenu_AddMembers"].Value;
        public string ChatMenu_EditContact => l["ChatMenu_EditContact"].Value;
        public string ChatMenu_LeaveChat => l["ChatMenu_LeaveChat"].Value;
        public string ChatMenu_BlockUser => l["ChatMenu_BlockUser"].Value;
        public string ChatMenu_UnblockUser => l["ChatMenu_UnblockUser"].Value;
        public string ChatMenu_Pin => l["ChatMenu_Pin"].Value;
        public string ChatMenu_Unpin => l["ChatMenu_Unpin"].Value;
        public string ChatMenu_PinToSidebar => l["ChatMenu_PinToSidebar"].Value;
        public string ChatMenu_UnpinFromSidebar => l["ChatMenu_UnpinFromSidebar"].Value;
        public string ChatMenu_StartAnonymousChat => l["ChatMenu_StartAnonymousChat"].Value;
        public string ChatMenu_StartRecording => l["ChatMenu_StartRecording"].Value;
        public string ChatMenu_StopRecording => l["ChatMenu_StopRecording"].Value;
        public string ChatMenu_RemoveFromActiveChats => l["ChatMenu_RemoveFromActiveChats"].Value;

        public string ErrorToast_ActionFailed => l["ErrorToast_ActionFailed"].Value;
        public string ErrorToast_UnknownError => l["ErrorToast_UnknownError"].Value;

        public string ChatView_ScrollUp => l["ChatView_ScrollUp"].Value;
        public string ChatView_ScrollDown => l["ChatView_ScrollDown"].Value;
        public string ChatView_NoChatSelected => l["ChatView_NoChatSelected"].Value;
        public string ChatView_NewMessages => l["ChatView_NewMessages"].Value;
        public string ChatView_NoMessagesYet => l["ChatView_NoMessagesYet"].Value;
        public string ChatView_WaitingForRequests => l["ChatView_WaitingForRequests"].Value;
        public string ChatView_PostTheFirstMessage => l["ChatView_PostTheFirstMessage"].Value;
        public string ChatView_SayHiTo_Prefix => l["ChatView_SayHiTo_Prefix"].Value;
        public string ChatView_SayHiTo_Suffix => l["ChatView_SayHiTo_Suffix"].Value;
        public string ChatView_ForwardedFrom => l["ChatView_ForwardedFrom"].Value;
        public string ChatView_MessageDeleted => l["ChatView_MessageDeleted"].Value;
        public string ChatView_SendingAttachments => l["ChatView_SendingAttachments"].Value;
        public string ChatView_AttachmentThumbnail => l["ChatView_AttachmentThumbnail"].Value;
        public string ChatView_ShowItems => l["ChatView_ShowItems"].Value;
        public string ChatView_HideItems => l["ChatView_HideItems"].Value;
        public string ChatView_JoinCall => l["ChatView_JoinCall"].Value;
        public string ChatView_GoToThread => l["ChatView_GoToThread"].Value;
        public string ChatView_CreatedBy => l["ChatView_CreatedBy"].Value;
        public string ChatView_Translation => l["ChatView_Translation"].Value;
        public string ChatView_You => l["ChatView_You"].Value;
        public string ChatView_Notified => l["ChatView_Notified"].Value;
        public string ChatView_And => l["ChatView_And"].Value;
        public string ChatView_PrevSearchResult => l["ChatView_PrevSearchResult"].Value;
        public string ChatView_NextSearchResult => l["ChatView_NextSearchResult"].Value;

        public string ChatFooter_ReadOnly => l["ChatFooter_ReadOnly"].Value;
        public string ChatFooter_YouBlockedUser => l["ChatFooter_YouBlockedUser"].Value;
        public string ChatFooter_Unblock => l["ChatFooter_Unblock"].Value;
        public string ChatFooter_BlockedByUser => l["ChatFooter_BlockedByUser"].Value;
        public string ChatFooter_ToPostOrTalk => l["ChatFooter_ToPostOrTalk"].Value;
        public string ChatFooter_JoinChat => l["ChatFooter_JoinChat"].Value;
        public string ChatFooter_JoinPlace_Format(object arg0) => l["ChatFooter_JoinPlace_Format", arg0].Value;
        public string ChatFooter_ToChatWith_Prefix => l["ChatFooter_ToChatWith_Prefix"].Value;
        public string ChatFooter_ToChatWith_Suffix => l["ChatFooter_ToChatWith_Suffix"].Value;
        public string ChatFooter_Or => l["ChatFooter_Or"].Value;
        public string ChatFooter_JoinAnonymously => l["ChatFooter_JoinAnonymously"].Value;
        public string ChatFooter_ToJoinThisChat => l["ChatFooter_ToJoinThisChat"].Value;

        public string ChatWelcome_Notes => l["ChatWelcome_Notes"].Value;
        public string ChatWelcome_ShareContact_Prefix => l["ChatWelcome_ShareContact_Prefix"].Value;
        public string ChatWelcome_ShareContact_Suffix => l["ChatWelcome_ShareContact_Suffix"].Value;
        public string ChatWelcome_ShareLink => l["ChatWelcome_ShareLink"].Value;
        public string ChatWelcome_Default => l["ChatWelcome_Default"].Value;
        public string ChatWelcome_PrivateJoinLink => l["ChatWelcome_PrivateJoinLink"].Value;
        public string ChatWelcome_PublicLink => l["ChatWelcome_PublicLink"].Value;

        public string MessageMenu_Reply => l["MessageMenu_Reply"].Value;
        public string MessageMenu_CopyText => l["MessageMenu_CopyText"].Value;
        public string MessageMenu_CopyLink => l["MessageMenu_CopyLink"].Value;
        public string MessageMenu_CopyCode => l["MessageMenu_CopyCode"].Value;
        public string MessageMenu_CopyImage => l["MessageMenu_CopyImage"].Value;
        public string MessageMenu_CopyMessageLink => l["MessageMenu_CopyMessageLink"].Value;
        public string MessageMenu_Forward => l["MessageMenu_Forward"].Value;
        public string MessageMenu_StartThread => l["MessageMenu_StartThread"].Value;
        public string MessageMenu_DeleteMessage => l["MessageMenu_DeleteMessage"].Value;
        public string MessageMenu_CopySelection => l["MessageMenu_CopySelection"].Value;
        public string MessageMenu_ForwardSelection => l["MessageMenu_ForwardSelection"].Value;
        public string MessageMenu_ClearSelection => l["MessageMenu_ClearSelection"].Value;
        public string MessageMenu_Unselect => l["MessageMenu_Unselect"].Value;
        public string MessageMenu_Select => l["MessageMenu_Select"].Value;
        public string MessageMenu_Play => l["MessageMenu_Play"].Value;
        public string MessageMenu_CancelSend => l["MessageMenu_CancelSend"].Value;
        public string MessageMenu_OpenLink => l["MessageMenu_OpenLink"].Value;
        public string MessageMenu_CopyLinkUrl => l["MessageMenu_CopyLinkUrl"].Value;
        public string MessageMenu_ForwardMessage => l["MessageMenu_ForwardMessage"].Value;
        public string MessageMenu_Preview => l["MessageMenu_Preview"].Value;
        public string MessageMenu_ShareFile => l["MessageMenu_ShareFile"].Value;
        public string MessageMenu_CopyDownloadLink => l["MessageMenu_CopyDownloadLink"].Value;
        public string MessageMenu_Download => l["MessageMenu_Download"].Value;

        public string ConversationMenu_Expand => l["ConversationMenu_Expand"].Value;
        public string ConversationMenu_Collapse => l["ConversationMenu_Collapse"].Value;
        public string ConversationMenu_ReSummarize => l["ConversationMenu_ReSummarize"].Value;
        public string ConversationMenu_CopySummary => l["ConversationMenu_CopySummary"].Value;
        public string ConversationMenu_CopySummaryLink => l["ConversationMenu_CopySummaryLink"].Value;

        public string ThreadMenu_GoToThread => l["ThreadMenu_GoToThread"].Value;
        public string ThreadMenu_CopyThread => l["ThreadMenu_CopyThread"].Value;

        public string LocationMessage_Live => l["LocationMessage_Live"].Value;
        public string LocationMessage_Static => l["LocationMessage_Static"].Value;
        public string LocationMessage_StopSharing => l["LocationMessage_StopSharing"].Value;
        public string LocationMessage_TrackingIsOff => l["LocationMessage_TrackingIsOff"].Value;
        public string LocationMessage_DistanceFromYou_Format(object arg0)
            => l["LocationMessage_DistanceFromYou_Format", arg0].Value;
        public string LocationMessage_ShareBack => l["LocationMessage_ShareBack"].Value;

        public string Editor_ChoosePhoto => l["Editor_ChoosePhoto"].Value;
        public string Editor_SelectVideo => l["Editor_SelectVideo"].Value;
        public string Editor_AddEmoji => l["Editor_AddEmoji"].Value;
        public string Editor_AddGif => l["Editor_AddGif"].Value;
        public string Editor_AttachFile => l["Editor_AttachFile"].Value;
        public string Editor_Location => l["Editor_Location"].Value;
        public string Editor_Mention => l["Editor_Mention"].Value;
        public string Editor_Attachment => l["Editor_Attachment"].Value;
        public string Editor_RemoveAll => l["Editor_RemoveAll"].Value;
        public string Editor_Files_One(object arg0) => l["Editor_Files_One", arg0].Value;
        public string Editor_Files_Other(object arg0) => l["Editor_Files_Other", arg0].Value;
        public string Editor_LargeTextTitle => l["Editor_LargeTextTitle"].Value;
        public string Editor_LargeTextMessage_Format(object arg0)
            => l["Editor_LargeTextMessage_Format", arg0].Value;
        public string Editor_SendAsFile => l["Editor_SendAsFile"].Value;
        public string Editor_KeepAsText => l["Editor_KeepAsText"].Value;

        public string Alert_Alert_Format(object arg0) => l["Alert_Alert_Format", arg0].Value;
        public string Alert_TargetEveryone => l["Alert_TargetEveryone"].Value;
        public string Alert_TargetCompanion => l["Alert_TargetCompanion"].Value;
        public string Alert_Confirm => l["Alert_Confirm"].Value;
        public string Alert_ConfirmTitle => l["Alert_ConfirmTitle"].Value;
        public string Alert_ConfirmUnread_Prefix => l["Alert_ConfirmUnread_Prefix"].Value;
        public string Alert_ConfirmUnread_Suffix => l["Alert_ConfirmUnread_Suffix"].Value;
        public string Alert_ConfirmRead => l["Alert_ConfirmRead"].Value;
        public string Alert_DisabledLargeGroup => l["Alert_DisabledLargeGroup"].Value;
        public string Alert_DisabledNoMessage => l["Alert_DisabledNoMessage"].Value;
        public string Alert_DisabledNoWritePermission => l["Alert_DisabledNoWritePermission"].Value;
        public string Alert_InfoIntro_Format(object arg0) => l["Alert_InfoIntro_Format", arg0].Value;
        public string Alert_InfoHighlight => l["Alert_InfoHighlight"].Value;
        public string Alert_InfoDetails => l["Alert_InfoDetails"].Value;
        public string Alert_InfoConsider_Prefix => l["Alert_InfoConsider_Prefix"].Value;
        public string Alert_InfoConsider_Suffix => l["Alert_InfoConsider_Suffix"].Value;
        public string Alert_AlertMentioned => l["Alert_AlertMentioned"].Value;
        public string Alert_AlertMentionedMembers => l["Alert_AlertMentionedMembers"].Value;
        public string Alert_ConfirmMentioned => l["Alert_ConfirmMentioned"].Value;
        public string Alert_AlertEveryone => l["Alert_AlertEveryone"].Value;
        public string Alert_NotifyPanel => l["Alert_NotifyPanel"].Value;
        public string Alert_CallAll => l["Alert_CallAll"].Value;
        public string Alert_ConfirmEveryone => l["Alert_ConfirmEveryone"].Value;

        public string Selection_Cancel => l["Selection_Cancel"].Value;
        public string Selection_Messages_One(object arg0) => l["Selection_Messages_One", arg0].Value;
        public string Selection_Messages_Other(object arg0) => l["Selection_Messages_Other", arg0].Value;
        public string Selection_ConvertToThread => l["Selection_ConvertToThread"].Value;
        public string Selection_ForwardSelected => l["Selection_ForwardSelected"].Value;
        public string Selection_DeleteSelected => l["Selection_DeleteSelected"].Value;
        public string Selection_CopySelectedAsText => l["Selection_CopySelectedAsText"].Value;
        public string Selection_DeleteOthersConfirm_Format(object arg0)
            => l["Selection_DeleteOthersConfirm_Format", arg0].Value;
        public string Selection_DeleteTitle_One => l["Selection_DeleteTitle_One"].Value;
        public string Selection_DeleteTitle_Other => l["Selection_DeleteTitle_Other"].Value;
        public string Selection_MessagesDeleted => l["Selection_MessagesDeleted"].Value;
        public string Selection_Undo => l["Selection_Undo"].Value;
        public string Selection_ForwardedToChat_Format(object arg0, object arg1)
            => l["Selection_ForwardedToChat_Format", arg0, arg1].Value;
        public string Selection_ForwardedToChats_Format(object arg0, object arg1)
            => l["Selection_ForwardedToChats_Format", arg0, arg1].Value;
        public string Selection_Navigate => l["Selection_Navigate"].Value;
        public string Selection_ForwardTitle => l["Selection_ForwardTitle"].Value;
        public string Selection_ForwardSubmit => l["Selection_ForwardSubmit"].Value;
        public string Selection_ForwardSearchPlaceholder => l["Selection_ForwardSearchPlaceholder"].Value;

        public string Navbar_NewChat => l["Navbar_NewChat"].Value;
        public string Navbar_NewPlace => l["Navbar_NewPlace"].Value;
        public string Navbar_Chats => l["Navbar_Chats"].Value;
        public string Navbar_Unread => l["Navbar_Unread"].Value;
        public string Navbar_Notes => l["Navbar_Notes"].Value;
        public string Navbar_Notifications => l["Navbar_Notifications"].Value;
        public string Navbar_Reload => l["Navbar_Reload"].Value;
        public string Navbar_SignIn => l["Navbar_SignIn"].Value;
        public string Navbar_TestPages => l["Navbar_TestPages"].Value;

        public string LeftPanel_AddNewMembers => l["LeftPanel_AddNewMembers"].Value;
        public string LeftPanel_JoinPlace => l["LeftPanel_JoinPlace"].Value;
        public string LeftPanel_PublicChatsAndPlaces => l["LeftPanel_PublicChatsAndPlaces"].Value;
        public string LeftPanel_Show => l["LeftPanel_Show"].Value;

        public string NoResults_Title => l["NoResults_Title"].Value;
        public string NoResults_TryAnotherRequest => l["NoResults_TryAnotherRequest"].Value;
        public string NoResults_SearchOutsidePlace => l["NoResults_SearchOutsidePlace"].Value;

        public string RightPanel_TabCall => l["RightPanel_TabCall"].Value;
        public string RightPanel_TabMembers => l["RightPanel_TabMembers"].Value;
        public string RightPanel_TabThreads => l["RightPanel_TabThreads"].Value;
        public string RightPanel_TabMedia => l["RightPanel_TabMedia"].Value;
        public string RightPanel_TabFiles => l["RightPanel_TabFiles"].Value;
        public string RightPanel_TabLinks => l["RightPanel_TabLinks"].Value;
        public string RightPanel_NoThreadsYet => l["RightPanel_NoThreadsYet"].Value;
        public string RightPanel_Summarize => l["RightPanel_Summarize"].Value;
        public string RightPanel_On => l["RightPanel_On"].Value;
        public string RightPanel_Off => l["RightPanel_Off"].Value;
        public string RightPanel_HiddenMembers_Prefix => l["RightPanel_HiddenMembers_Prefix"].Value;
        public string RightPanel_HiddenMembers_Suffix => l["RightPanel_HiddenMembers_Suffix"].Value;
        public string RightPanel_Notifications => l["RightPanel_Notifications"].Value;
        public string RightPanel_GroupTitle_Format(object arg0, object arg1)
            => l["RightPanel_GroupTitle_Format", arg0, arg1].Value;
        public string RightPanel_GroupOwners => l["RightPanel_GroupOwners"].Value;
        public string RightPanel_GroupModerators => l["RightPanel_GroupModerators"].Value;
        public string RightPanel_GroupOnline => l["RightPanel_GroupOnline"].Value;
        public string RightPanel_GroupOffline => l["RightPanel_GroupOffline"].Value;
        public string RightPanel_GroupUnreported => l["RightPanel_GroupUnreported"].Value;

        public string Search_LocationTitle => l["Search_LocationTitle"].Value;
        public string Search_LocationAnywhere => l["Search_LocationAnywhere"].Value;
        public string Search_LocationInChat => l["Search_LocationInChat"].Value;
        public string Search_LocationInPlace => l["Search_LocationInPlace"].Value;
        public string Search_TypeTitle => l["Search_TypeTitle"].Value;
        public string Search_TypeAnything => l["Search_TypeAnything"].Value;
        public string Search_TypePeople => l["Search_TypePeople"].Value;
        public string Search_TypeGroups => l["Search_TypeGroups"].Value;
        public string Search_TypeMessages => l["Search_TypeMessages"].Value;
        public string Search_TabTags => l["Search_TabTags"].Value;
        public string Search_RecentSearches => l["Search_RecentSearches"].Value;
        public string Search_RecentlyViewed => l["Search_RecentlyViewed"].Value;

        public string AudioPlayer_Backward => l["AudioPlayer_Backward"].Value;
        public string AudioPlayer_Forward => l["AudioPlayer_Forward"].Value;
        public string AudioPlayer_Seek => l["AudioPlayer_Seek"].Value;
        public string AudioPlayer_InterruptTitle => l["AudioPlayer_InterruptTitle"].Value;
        public string AudioPlayer_InterruptOne => l["AudioPlayer_InterruptOne"].Value;
        public string AudioPlayer_InterruptMany_Format(object arg0)
            => l["AudioPlayer_InterruptMany_Format", arg0].Value;
        public string AudioPlayer_Confirm => l["AudioPlayer_Confirm"].Value;

        public string Call_NoActiveCall => l["Call_NoActiveCall"].Value;
        public string Call_Dialing => l["Call_Dialing"].Value;
        public string Call_Rules => l["Call_Rules"].Value;
        public string Call_Manage => l["Call_Manage"].Value;
        public string Call_Transcription => l["Call_Transcription"].Value;
        public string Call_NoTranscription => l["Call_NoTranscription"].Value;
        public string Call_Voice => l["Call_Voice"].Value;
        public string Call_NoVoice => l["Call_NoVoice"].Value;
        public string Call_Video => l["Call_Video"].Value;
        public string Call_NoVideo => l["Call_NoVideo"].Value;
        public string Call_MuteAll => l["Call_MuteAll"].Value;
        public string Call_UnmuteAll => l["Call_UnmuteAll"].Value;
        public string Call_GroupHost => l["Call_GroupHost"].Value;
        public string Call_GroupOthers => l["Call_GroupOthers"].Value;
        public string Call_GroupExited => l["Call_GroupExited"].Value;
        public string Call_SharingScreen => l["Call_SharingScreen"].Value;
        public string Call_CameraOn => l["Call_CameraOn"].Value;
        public string Call_TurnOffRecording => l["Call_TurnOffRecording"].Value;
        public string Call_Recording => l["Call_Recording"].Value;
        public string Call_Listening => l["Call_Listening"].Value;
        public string Call_CantMutePeer => l["Call_CantMutePeer"].Value;
        public string Call_CantMuteOwner => l["Call_CantMuteOwner"].Value;
        public string Call_CantMuteOthers => l["Call_CantMuteOthers"].Value;

        public string NotificationMode_Title => l["NotificationMode_Title"].Value;
        public string NotificationMode_AllMessages => l["NotificationMode_AllMessages"].Value;
        public string NotificationMode_ImportantOnly => l["NotificationMode_ImportantOnly"].Value;
        public string NotificationMode_Muted => l["NotificationMode_Muted"].Value;

        public string AccountEditor_Title => l["AccountEditor_Title"].Value;
        public string AccountEditor_DeleteAccount => l["AccountEditor_DeleteAccount"].Value;

        public string Alias_LinkAvailable => l["Alias_LinkAvailable"].Value;
        public string Alias_LinkInUse => l["Alias_LinkInUse"].Value;
        public string Alias_PlaceLinkRequired => l["Alias_PlaceLinkRequired"].Value;
        public string Form_ChatTitle => l["Form_ChatTitle"].Value;
        public string Form_ChatDescription => l["Form_ChatDescription"].Value;
        public string Form_ThreadTitle => l["Form_ThreadTitle"].Value;
        public string Form_Description => l["Form_Description"].Value;
        public string Form_PlaceTitle => l["Form_PlaceTitle"].Value;
        public string Form_Title => l["Form_Title"].Value;
        public string Form_ChatId => l["Form_ChatId"].Value;
        public string Form_Name => l["Form_Name"].Value;
        public string Form_Bio => l["Form_Bio"].Value;
        public string Form_ConfirmDelete => l["Form_ConfirmDelete"].Value;
        public string Form_Email => l["Form_Email"].Value;
        public string Form_TimeZone => l["Form_TimeZone"].Value;
        public string Form_Phone => l["Form_Phone"].Value;
        public string Form_CustomLink => l["Form_CustomLink"].Value;
        public string DeleteAccount_Title => l["DeleteAccount_Title"].Value;
        public string DeleteAccount_Question_Prefix => l["DeleteAccount_Question_Prefix"].Value;
        public string DeleteAccount_QuestionTarget => l["DeleteAccount_QuestionTarget"].Value;
        public string DeleteAccount_Question_Suffix => l["DeleteAccount_Question_Suffix"].Value;
        public string DeleteAccount_Warning_Prefix => l["DeleteAccount_Warning_Prefix"].Value;
        public string DeleteAccount_WarningTarget => l["DeleteAccount_WarningTarget"].Value;
        public string DeleteAccount_Warning_Suffix => l["DeleteAccount_Warning_Suffix"].Value;
        public string DeleteAccount_Scope_Prefix => l["DeleteAccount_Scope_Prefix"].Value;
        public string DeleteAccount_ScopeTarget => l["DeleteAccount_ScopeTarget"].Value;
        public string DeleteAccount_Scope_Suffix => l["DeleteAccount_Scope_Suffix"].Value;
        public string DeleteAccount_Proposal_Prefix => l["DeleteAccount_Proposal_Prefix"].Value;
        public string DeleteAccount_Proposal_Suffix => l["DeleteAccount_Proposal_Suffix"].Value;
    }
}
