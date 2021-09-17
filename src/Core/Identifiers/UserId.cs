using Stl.Text;

namespace ActualChat
{
    public partial struct UserId
    {
        public static implicit operator UserId(Symbol value) => new(value);
    }
}