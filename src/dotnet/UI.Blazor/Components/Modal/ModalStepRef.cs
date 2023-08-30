﻿using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public class ModalStepRef : IHasId<Symbol>
{
    private readonly HistoryStepRef _stepRef;
    private readonly TaskCompletionSource<bool> _whenClosedSource = TaskCompletionSourceExt.New<bool>();
    private bool _isModalClosing;

    public Symbol Id => _stepRef.Id;

    public ModalRef ModalRef { get; }

    public ModalStepRef? ParentStepRef { get; }

    public HistoryStepRef RawStepRef => _stepRef;

    public Task<bool> WhenClosed => _whenClosedSource.Task;

    public void Close(bool isModalClosing)
    {
        this._isModalClosing = isModalClosing;
        RawStepRef.Close();
    }

    public ModalStepRef(ModalRef modalRef, HistoryStepRef stepRef, ModalStepRef? parentStepRef)
    {
        _stepRef = stepRef;
        _ = _stepRef.WhenClosed
            .ContinueWith(_ => _whenClosedSource.TrySetResult(!_isModalClosing), TaskScheduler.Default);
        ModalRef = modalRef;
        ParentStepRef = parentStepRef;
    }
}
