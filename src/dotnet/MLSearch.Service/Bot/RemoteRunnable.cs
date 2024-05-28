using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FFMpegCore.Arguments;
using Newtonsoft.Json;

namespace ActualChat.MLSearch.Bot;

public class RemoteRunnableOptions {
    /// <summary>
    /// Runtime values for attributes previously made configurable on this Runnable,  
    /// or sub-Runnables.
    /// </summary>
    public Dictionary<string, object>? configurable;

    /// <summary>
    /// Maximum number of times a call can recurse. If not provided, defaults to 25.
    /// </summary>
    public int recursionLimit;

    /// Maximum number of parallel calls to make.
    public int maxConcurrency;
    public Action<HttpHeaders>? AddHeaders;
}

record InvokeRequest {
    public required Dictionary<string, string> Input;
    public Dictionary<string, string>? Config;
    //Dictionary<string, string>? Kwargs;
}


public interface IRemoteRunnable {
    Task<string> InvokeAsync(IDictionary<string, string> input, IDictionary<string, string> options, CancellationToken cancellationToken);
}

// TODO: Decide if use of IHttpClientFactory is benefitial here.
public class RemoteRunnable (/*IHttpClientFactory httpClientFactory,*/ Uri url, RemoteRunnableOptions options): IRemoteRunnable
{

    
    // Method notes: Usually models return something barely structured. We should not expect that a remote model
    // produces a perfectly structured response.
    private async Task<string> Post<TRequest, TResponse>(string path, TRequest data, CancellationToken cancellationToken) 
    //where TRequest: ISerializable
    {
        //using (var client = httpClientFactory.CreateClient("RemoteRunnable"))
        using var client = new HttpClient();
        var json = JsonConvert.SerializeObject(data);
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url + path) {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        options.AddHeaders?.Invoke(request.Headers);

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            // Handle the error
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new Exception($"{response.StatusCode} Error: {errorMessage}");
        }
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> InvokeAsync(IDictionary<string, string> input, IDictionary<string, string> options, CancellationToken cancellationToken) {
        var data = new InvokeRequest {
            Input = input.ToDictionary(StringComparer.Ordinal),
            Config = options.ToDictionary(StringComparer.Ordinal),
        };
        return await this.Post<InvokeRequest, string>("/invoke", data, cancellationToken ).ConfigureAwait(false);
    }
    
    [Obsolete("This is not implemented. Implement if neccessary.", true)]
    public async Task<string> StreamLog() { throw new NotImplementedException(); }
}