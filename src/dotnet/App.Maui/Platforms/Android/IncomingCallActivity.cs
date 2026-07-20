using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using ActualChat.UI.Blazor.App.Services;
using Android.Views;
using Android.Widget;
using Activity = Android.App.Activity;
using View = Android.Views.View;
using Button = Android.Widget.Button;
using Color = Android.Graphics.Color;

namespace ActualChat.App.Maui;

// Minimal native incoming-call screen shown over the lock screen (or when the app isn't in the
// foreground). It renders from the push payload — no WebView boot — and hands off to MainActivity
// only on accept. The reactive Blazor banner still owns the ring while the app is in the foreground.
[Activity(
    Theme = "@android:style/Theme.Black.NoTitleBar",
    ExcludeFromRecents = true,
    LaunchMode = LaunchMode.SingleTask,
    TaskAffinity = "chat.actual.incomingcall",
    Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.KeyboardHidden)]
public class IncomingCallActivity : Activity
{
    // Backstop that mirrors the server RingTimeout: the screen self-dismisses even if no dismissal
    // push arrives (offline device).
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(40);
    private static volatile IncomingCallActivity? _current;

    private ILogger Log => field ??= StaticLog.For<IncomingCallActivity>();
    private ChatId _chatId = null!;
    private ImageView? _avatar;
    private Handler? _timeoutHandler;

    // Called from the FCM service when the ring is cancelled/timed-out/answered elsewhere.
    public static void FinishCurrent()
    {
        var activity = _current;
        activity?.RunOnUiThread(() => {
            IncomingCallRinger.Stop();
            activity.Finish();
        });
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (OperatingSystem.IsAndroidVersionAtLeast(27)) {
            SetShowWhenLocked(true);
            SetTurnScreenOn(true);
        }
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        _current = this;

        var chatId = ChatId.TryParse(Intent?.GetStringExtra(IncomingCallNotifications.ChatIdExtraKey), allowNull: true);
        if (chatId is null) {
            Finish();
            return;
        }
        _chatId = chatId;

        var callerName = Intent?.GetStringExtra(IncomingCallNotifications.CallerNameExtraKey);
        if (callerName.IsNullOrEmpty())
            callerName = "Incoming call";
        var callText = Intent?.GetStringExtra(IncomingCallNotifications.CallTextExtraKey);
        if (callText.IsNullOrEmpty())
            callText = "Incoming call";

        SetContentView(BuildLayout(callerName!, callText!));
        LoadAvatar(Intent?.GetStringExtra(IncomingCallNotifications.ImageUrlExtraKey) ?? "");
        IncomingCallRinger.Start();

        _timeoutHandler = new Handler(Looper.MainLooper!);
        _timeoutHandler.PostDelayed(Finish, (long)RingTimeout.TotalMilliseconds);
    }

    protected override void OnDestroy()
    {
        _timeoutHandler?.RemoveCallbacksAndMessages(null);
        Interlocked.CompareExchange(ref _current, null, this);
        IncomingCallRinger.Stop();
        base.OnDestroy();
    }

#pragma warning disable CA1422 // OnBackPressed is obsolete on API 33+, but back must be inert on a ring screen
    public override void OnBackPressed()
    {
        // A ring screen isn't dismissable with Back — the user must Accept or Decline.
    }
#pragma warning restore CA1422

    // Private methods

    private View BuildLayout(string callerName, string callText)
    {
        var root = new LinearLayout(this) {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
        };
        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetBackgroundColor(Color.Argb(255, 16, 16, 20));
        root.SetPadding(Dp(24), Dp(48), Dp(24), Dp(48));

        root.AddView(Spacer(2));

        _avatar = new ImageView(this) {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(112), Dp(112)),
        };
        _avatar.SetBackgroundColor(Color.Argb(255, 44, 44, 52));
        root.AddView(_avatar);

        var nameView = new TextView(this) { Text = callerName };
        nameView.SetTextColor(Color.White);
        nameView.SetTextSize(Android.Util.ComplexUnitType.Sp, 24);
        nameView.SetPadding(0, Dp(20), 0, 0);
        nameView.Gravity = GravityFlags.Center;
        root.AddView(nameView);

        var labelView = new TextView(this) { Text = callText };
        labelView.SetTextColor(Color.Argb(255, 170, 170, 170));
        labelView.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);
        labelView.SetPadding(0, Dp(8), 0, 0);
        labelView.Gravity = GravityFlags.Center;
        root.AddView(labelView);

        root.AddView(Spacer(3));

        var buttons = new LinearLayout(this) {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
        };
        buttons.AddView(ActionButton("Decline", Color.Argb(255, 224, 52, 43), OnDecline, Dp(0), Dp(8)));
        buttons.AddView(ActionButton("Accept", Color.Argb(255, 47, 191, 79), OnAccept, Dp(8), Dp(0)));
        root.AddView(buttons);

        return root;
    }

    private Button ActionButton(string text, Color color, Action onClick, int marginLeft, int marginRight)
    {
        var button = new Button(this) { Text = text };
        button.SetTextColor(Color.White);
        button.SetBackgroundColor(color);
        var lp = new LinearLayout.LayoutParams(0, Dp(56), 1f);
        lp.SetMargins(marginLeft, 0, marginRight, 0);
        button.LayoutParameters = lp;
        button.Click += (_, _) => onClick();
        return button;
    }

    private View Spacer(float weight)
    {
        var spacer = new View(this) {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, weight),
        };
        return spacer;
    }

    private void LoadAvatar(string imageUrl)
    {
        if (imageUrl.IsNullOrEmpty())
            return;

        _ = BackgroundTask.Run(async () => {
            var bitmap = await Task.Run(() => NotificationHelper.GetImage(imageUrl)).ConfigureAwait(false);
            if (bitmap != null)
                RunOnUiThread(() => _avatar?.SetImageBitmap(bitmap));
        }, Log, "LoadAvatar failed");
    }

    private void OnAccept()
    {
        // Hypothesis-3 test: if the Blazor scope is alive, accept WITHOUT dismissing the keyguard and
        // keep this activity visible over the lock screen — so the mic FGS starts (or fails) from a
        // foreground-visible-over-keyguard state. logcat shows the outcome.
        if (AppServicesAccessor.TryGetScopedServices(out _)) {
            IncomingCallRinger.Stop();
            _timeoutHandler?.RemoveCallbacksAndMessages(null);
            Log.LogWarning("Accept over lock screen (no unlock), scope alive, chat #{ChatId}", _chatId);
            _ = AppServicesAccessor.DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().Accept(_chatId, withCamera: false, keepOverLockScreen: true),
                "IncomingCallUI.Accept(overLock)");
            return;
        }

        var keyguardManager = (KeyguardManager?)GetSystemService(KeyguardService);
        if (keyguardManager?.IsKeyguardLocked == true)
            keyguardManager.RequestDismissKeyguard(this, new DismissThenAccept(this));
        else
            ProceedAccept();
    }

    private void ProceedAccept()
    {
        IncomingCallRinger.Stop();
        var link = (string)Links.Chat(_chatId);
        var acceptIntent = NotificationHelper.CreateViewIntent(this, link)!;
        acceptIntent.PutExtra(IncomingCallNotifications.ChatIdExtraKey, _chatId.Value);
        acceptIntent.PutExtra(IncomingCallNotifications.AcceptExtraKey, true);
        acceptIntent.AddFlags(ActivityFlags.NewTask);
        StartActivity(acceptIntent);
        IncomingCallNotifications.Dismiss(_chatId);
        Finish();
    }

    private void OnDecline()
    {
        IncomingCallRinger.Stop();
        var declineIntent = new Intent(this, typeof(CallActionReceiver));
        declineIntent.SetAction(IncomingCallNotifications.DeclineAction);
        declineIntent.PutExtra(IncomingCallNotifications.ChatIdExtraKey, _chatId.Value);
        SendBroadcast(declineIntent);
        IncomingCallNotifications.Dismiss(_chatId);
        Finish();
    }

    private int Dp(int dp)
        => (int)(dp * (Resources?.DisplayMetrics?.Density ?? 1f));

    // Nested types

    private sealed class DismissThenAccept(IncomingCallActivity activity) : KeyguardManager.KeyguardDismissCallback
    {
        // Only accept once the screen is actually unlocked; on cancel/error keep ringing.
        public override void OnDismissSucceeded()
            => activity.ProceedAccept();
    }
}
