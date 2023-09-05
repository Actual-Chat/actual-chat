namespace ActualChat.UI.Blazor.Components;

public class DialogFrameNarrowViewSettingsBuilder
{
    private DialogButtonInfo? _lastSubmitButtonInfo;
    private DialogFrameNarrowViewSettings? _lastNarrowViewSettings;
    private IReadOnlyCollection<DialogButtonInfo>? _lastButtonInfos;

    public DialogFrameNarrowViewSettings GetFrom(IReadOnlyCollection<DialogButtonInfo>? buttonInfos)
    {
        var submitButtonInfo = buttonInfos?.FirstOrDefault(c => c.IsSubmit);

        if (_lastNarrowViewSettings == null
            || !ReferenceEquals(_lastButtonInfos, buttonInfos)
            || !ReferenceEquals(_lastSubmitButtonInfo, submitButtonInfo)) {
            _lastButtonInfos = buttonInfos;
            if (_lastSubmitButtonInfo != null)
                _lastSubmitButtonInfo.CanExecuteChanged -= OnCanExecuteChanged;
            _lastSubmitButtonInfo = submitButtonInfo;
            if (_lastSubmitButtonInfo != null)
                _lastSubmitButtonInfo.CanExecuteChanged += OnCanExecuteChanged;
            if (_lastSubmitButtonInfo == null)
                _lastNarrowViewSettings = DialogFrameNarrowViewSettings.Stretch with { UseInteractiveHeader = true };
            else
                _lastNarrowViewSettings = DialogFrameNarrowViewSettings.SubmitButton(_lastSubmitButtonInfo.Execute!, _lastSubmitButtonInfo.Title);
        }
        if (_lastSubmitButtonInfo != null)
            _lastNarrowViewSettings.CanSubmit = _lastSubmitButtonInfo.CanExecute;
        return _lastNarrowViewSettings;
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
        => _lastNarrowViewSettings!.CanSubmit = _lastSubmitButtonInfo!.CanExecute;
}
