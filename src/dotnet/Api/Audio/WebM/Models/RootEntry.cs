namespace ActualChat.Audio.WebM.Models;

public abstract class RootEntry : BaseModel
{
    public bool IsCompleted { get; private set; }

    public void Complete()
        => IsCompleted = true;
}
