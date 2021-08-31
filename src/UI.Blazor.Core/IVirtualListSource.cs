using System.Collections.Generic;
using System.Threading.Tasks;

namespace ActualChat.UI.Blazor
{
    public interface IVirtualListSource<TItem>
    {
        Task<double> GetPosition(string key);
        Task<KeyValuePair<string, TItem>> GetPage(double position, int count);
        Task<KeyValuePair<string, TItem>> GetPage(string fromKeyExclusive, int count, bool precedingFromKey = false);
    }
}
