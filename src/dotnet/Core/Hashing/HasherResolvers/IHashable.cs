namespace ActualChat.Hashing;

public interface IHashable
{
    public int GetHashCode(HasherResolver resolver);
}
