using System.Net.Http.Headers;
using System.Net.Http.Json;
using ActualLab.Generators;
using FFMpegCore;
using Google.Api.Gax.ResourceNames;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Google.Cloud.Video.Transcoder.V1;
using TranscoderAudioStream = Google.Cloud.Video.Transcoder.V1.AudioStream;
using TranscoderVideoStream = Google.Cloud.Video.Transcoder.V1.VideoStream;

namespace ActualChat.Uploads;

public sealed class GoogleCloudVideoUploadProcessor(
    StorageClient storageClient,
    IHttpClientFactory httpClientFactory,
    string bucket,
    string projectId,
    string regionId,
    string transcodeServiceUrl,
    ILogger<GoogleCloudVideoUploadProcessor> log)
    : IUploadProcessor
{
    public const string HttpClientName = "video-transcoder";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FastPollPeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan JobStartupDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(14);
    // A cold start measured on dev: ~3.6s
    private static readonly TimeSpan ServiceStartupDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SignedUrlExpiry = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxLocalTranscodeDuration = TimeSpan.FromMinutes(10);
    // The service keeps its output in memory until the upload
    private static readonly TimeSpan MaxServiceTranscodeDuration = TimeSpan.FromMinutes(60);
    private const string OutputContentType = "video/mp4";

    // ffprobe codec names of the input codecs Transcoder lists as supported:
    // https://docs.cloud.google.com/transcoder/docs/concepts/supported-input-and-output-formats
    private static readonly HashSet<string> TranscoderInputCodecs = new(StringComparer.OrdinalIgnoreCase) {
        "dnxhd", "dvvideo", "h261", "h263", "h264", "hevc", "mpeg1video", "mpeg2video",
        "mpeg4", "prores", "theora", "vc1", "vp8", "vp9", "wmv3",
    };

    private StorageClient StorageClient { get; } = storageClient;
    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;
    private string Bucket { get; } = bucket;
    private string ProjectId { get; } = projectId;
    private string RegionId { get; } = regionId;
    private string TranscodeServiceUrl { get; } = transcodeServiceUrl;
    private ILogger Log { get; } = log;

    public bool Supports(string contentType, MediaKind mediaKind)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(
        UploadedFile upload,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        progress?.Report(0);

        if (upload is not UploadedBlobFile blobFile)
            throw new InvalidOperationException($"{nameof(GoogleCloudVideoUploadProcessor)} requires "
                + $"{nameof(UploadedBlobFile)} but received {upload.GetType().Name}.");

        var stateObjectName = GetStateObjectName(blobFile.BlobPath);
        var savedState = await TryReadState(stateObjectName, cancellationToken).ConfigureAwait(false);
        var signedUrl = await GenerateSignedReadUrl(blobFile.BlobPath).ConfigureAwait(false);

        // 1. Video info — from saved state or by analyzing
        Size2D size;
        TimeSpan duration;
        double frameRate;
        VideoConversion conversion;
        bool hasAudio;
        IMediaAnalysis? mediaInfo = null;

        if (savedState is not null) {
            Log.LogDebug("Resuming transcoder job '{JobName}' for '{FileName}'",
                savedState.JobName, upload.FileName);
            size = new Size2D(savedState.VideoWidth, savedState.VideoHeight);
            duration = TimeSpan.FromSeconds(savedState.Duration);
            frameRate = 0; // Not important since we're resuming a job.
            conversion = VideoConversion.Transcode;
            hasAudio = false; // Irrelevant — job is already created with the right config.
        }
        else {
            try {
                mediaInfo = await FFProbe.AnalyseAsync(new Uri(signedUrl), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to extract video info from '{FileName}'", upload.FileName);
            }
            finally {
                Log.LogDebug("Video analysis completed in {Elapsed:N0}ms for '{FileName}'",
                    stepSw.ElapsedMilliseconds,
                    upload.FileName);
            }
            var videoStream = mediaInfo?.PrimaryVideoStream;
            if (videoStream is null)
                return new ProcessedFile(upload.AsBinaryFile(), null);

            (size, duration, frameRate) = UploadProcessorHelper.AnalyzeVideo(mediaInfo!);
            conversion = UploadProcessorHelper.GetConversion(mediaInfo!, upload.Length);
            hasAudio = mediaInfo!.PrimaryAudioStream is not null;
        }

        progress?.Report(10);

        // 2. Snapshot
        stepSw.Restart();
        var snapshot = await UploadProcessorHelper
            .Snapshot(new Uri(signedUrl), upload.FileName, duration)
            .ConfigureAwait(false);
        Log.LogDebug("Snapshot extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);

        progress?.Report(15);

        if (conversion is VideoConversion.None) {
            var renamed = UploadProcessorHelper.EnsureMp4Extension(upload);
            return new ProcessedFile(renamed, size, snapshot) { Duration = duration };
        }

        var original = new ProcessedFile(upload, size, snapshot) { Duration = duration };
        // 3. Create or resume a transcoder job + poll
        try {
            progress?.Report(20);
            var transcodedVideo = mediaInfo?.PrimaryVideoStream;
            var isTranscodable = transcodedVideo is null || CanTranscode(transcodedVideo);
            if (conversion is VideoConversion.Remux || !isTranscodable) {
                Log.LogInformation("Converting '{FileName}' without Transcoder: {Conversion}, codec '{Codec}'",
                    upload.FileName, conversion, transcodedVideo?.CodecName);
                return await Convert(original, signedUrl, mediaInfo, conversion, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            string outputPrefix;
            string jobName;

            if (savedState is not null) {
                outputPrefix = savedState.OutputPrefix;
                jobName = savedState.JobName;
            }
            else {
                outputPrefix = $"transcode-output/{RandomStringGenerator.Default.Next()}/";
                var inputGcsUri = $"gs://{Bucket}/{blobFile.BlobPath}";
                var outputGcsUri = $"gs://{Bucket}/{outputPrefix}";

                stepSw.Restart();
                if (UploadProcessorHelper.ExceedsFullHd(size))
                    size = UploadProcessorHelper.ScaleToFullHd(size);
                frameRate = Math.Max(frameRate, 24);
                var createdJob = await CreateTranscoderJob(
                        inputGcsUri, outputGcsUri, size, frameRate, hasAudio, cancellationToken)
                    .ConfigureAwait(false);
                jobName = createdJob.Name;
                Log.LogDebug("Transcoder job created in {Elapsed:N0}ms: '{JobName}'",
                    stepSw.ElapsedMilliseconds, jobName);

                // Save state BEFORE polling so we can resume after a restart
                var state = new TranscoderState(jobName, outputPrefix, size.Width, size.Height, duration.TotalSeconds);
                await WriteState(stateObjectName, state, cancellationToken).ConfigureAwait(false);
            }

            // 4. Poll for completion
            stepSw.Restart();
            var job = await PollJobUntilComplete(jobName, duration, progress, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Transcoding completed in {Elapsed:N0}ms, state: {State}, job: '{JobName}'",
                stepSw.ElapsedMilliseconds, job.State, job.Name);

            if (job.State is Job.Types.ProcessingState.Failed) {
                Log.LogError("Transcoder job '{JobName}' failed, converting '{FileName}' without it: {Error}",
                    job.Name, upload.FileName, job.Error);
                _ = DeleteState(stateObjectName);
                // The conversion starts over, and so does its progress
                return await Convert(original, signedUrl, mediaInfo, conversion, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            progress?.Report(95);

            // 5. Build result
            var result = await GetTranscodedFile(original, size, outputPrefix, stateObjectName, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
            return result;
        }
        catch (Exception e) {
            Log.LogError(e, "Cloud transcoding failed for '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            _ = DeleteState(stateObjectName);
            snapshot?.Delete();
            throw;
        }
    }

    public static bool CanTranscode(FFMpegCore.VideoStream videoStream)
        => TranscoderInputCodecs.Contains(videoStream.CodecName);

    // Private methods

    private Task<ProcessedFile> Convert(
        ProcessedFile original,
        string signedUrl,
        IMediaAnalysis? mediaInfo,
        VideoConversion conversion,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
        => conversion is VideoConversion.Transcode && !TranscodeServiceUrl.IsNullOrEmpty()
            ? TranscodeInService(original, signedUrl, mediaInfo, progress, cancellationToken)
            : ProcessLocally(original, signedUrl, mediaInfo, conversion, progress, cancellationToken);

    private async Task<ProcessedFile> TranscodeInService(
        ProcessedFile original,
        string signedUrl,
        IMediaAnalysis? mediaInfo,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = original.File.FileName;
        if (original.Duration > MaxServiceTranscodeDuration) {
            Log.LogWarning("'{FileName}' is too long to transcode ({Duration}), keeping the original",
                fileName, original.Duration);
            return original;
        }

        // Nothing is saved to resume from: the service stops transcoding once the request is gone
        var outputPrefix = $"transcode-output/{RandomStringGenerator.Default.Next()}/";
        var sw = Stopwatch.StartNew();
        try {
            mediaInfo ??= await FFProbe.AnalyseAsync(new Uri(signedUrl), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var (arguments, size) = UploadProcessorHelper.GetTranscodeArguments(mediaInfo);
            var outputUrl = await GenerateSignedWriteUrl(GetOutputObjectName(outputPrefix)).ConfigureAwait(false);
            var request = new TranscodeRequest(signedUrl, outputUrl, arguments);
            var response = await RequestTranscode(
                    request, original.Duration ?? TimeSpan.Zero, progress, cancellationToken)
                .ConfigureAwait(false);
            Log.LogInformation(
                "Transcode service converted '{FileName}' in {Elapsed:N0}ms: "
                + "ffmpeg {TranscodeMillis:N0}ms, upload of {Size:N0} bytes {UploadMillis:N0}ms",
                fileName, sw.ElapsedMilliseconds, response.TranscodeMillis, response.Size, response.UploadMillis);
            progress?.Report(95);
            return await GetTranscodedFile(original, size, outputPrefix, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogError(e,
                "Could not transcode '{FileName}' in the service after {Elapsed:N0}ms, keeping the original",
                fileName, sw.ElapsedMilliseconds);
            _ = CleanupGcsOutput(outputPrefix);
            return original;
        }
    }

    private async Task<TranscodeResponse> RequestTranscode(
        TranscodeRequest transcodeRequest,
        TimeSpan videoDuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // The service accepts only internal traffic, and only from callers holding run.invoker on it
        var credential = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
        var oidcToken = await credential
            .GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(TranscodeServiceUrl), cancellationToken)
            .ConfigureAwait(false);
        var idToken = await oidcToken.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ServiceTimeout);
        using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, TranscodeServiceUrl) {
            Content = JsonContent.Create(transcodeRequest),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        var responseTask = httpClient.SendAsync(request, cts.Token);

        // ~4.3x realtime measured for 1080p on 8 vCPUs; 0.3x leaves headroom
        var expectedDuration = (ServiceStartupDuration + (videoDuration * 0.3))
            .Clamp(ServiceStartupDuration, ServiceTimeout);
        var stopwatch = Stopwatch.StartNew();
        while (true) {
            var completedTask = await Task
                .WhenAny(responseTask, Task.Delay(FastPollInterval, CancellationToken.None))
                .ConfigureAwait(false);
            if (completedTask == responseTask)
                break;

            ReportEstimatedProgress(progress, stopwatch.Elapsed / expectedDuration);
        }

        using var response = await responseTask.ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw StandardError.External(
                $"The transcode service answered {(int)response.StatusCode} {response.StatusCode}: {error}");
        }

        var result = await response.Content
            .ReadFromJsonAsync<TranscodeResponse>(cancellationToken)
            .ConfigureAwait(false);
        return result ?? throw StandardError.External("The transcode service answered with no result.");
    }

    private async Task<string> GenerateSignedWriteUrl(string objectName)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
        var urlSigner = UrlSigner.FromCredential(credential);
        var contentHeaders = new Dictionary<string, IEnumerable<string>> {
            ["Content-Type"] = [OutputContentType],
        };
        var template = UrlSigner.RequestTemplate
            .FromBucket(Bucket)
            .WithObjectName(objectName)
            .WithHttpMethod(HttpMethod.Put)
            .WithContentHeaders(contentHeaders);
        return await urlSigner
            .SignAsync(template, UrlSigner.Options.FromDuration(SignedUrlExpiry), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<ProcessedFile> GetTranscodedFile(
        ProcessedFile original,
        Size2D size,
        string outputPrefix,
        string? stateObjectName,
        CancellationToken cancellationToken)
    {
        var outputObjectName = GetOutputObjectName(outputPrefix);
        var outputLength = await GetObjectLength(outputObjectName, cancellationToken).ConfigureAwait(false);
        var file = new UploadedBlobFile(
            original.File.FileName.ChangeExtension(".mp4"),
            OutputContentType,
            outputLength,
            outputObjectName,
            () => OpenGcsObject(outputObjectName));
        return new ProcessedFile(file, size, original.Thumbnail) {
            Duration = original.Duration,
            OnDispose = () => {
                _ = CleanupGcsOutput(outputPrefix);
                if (stateObjectName is not null)
                    _ = DeleteState(stateObjectName);
            },
        };
    }

    private async Task<ProcessedFile> ProcessLocally(
        ProcessedFile original,
        string signedUrl,
        IMediaAnalysis? mediaInfo,
        VideoConversion conversion,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = original.File.FileName;
        if (conversion is VideoConversion.Transcode && original.Duration > MaxLocalTranscodeDuration) {
            Log.LogWarning("'{FileName}' is too long to transcode locally ({Duration}), keeping the original",
                fileName, original.Duration);
            return original;
        }

        var sw = Stopwatch.StartNew();
        try {
            var source = new Uri(signedUrl);
            mediaInfo ??= await FFProbe.AnalyseAsync(source, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var (file, size) = await UploadProcessorHelper
                .ConvertLocally(
                    () => FFMpegArguments.FromUrlInput(source),
                    fileName,
                    mediaInfo,
                    conversion,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            Log.LogInformation("Local {Conversion} of '{FileName}' completed in {Elapsed:N0}ms",
                conversion, fileName, sw.ElapsedMilliseconds);
            return new ProcessedFile(file, size, original.Thumbnail) { Duration = original.Duration };
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
            Log.LogError(e, "Could not convert '{FileName}' locally after {Elapsed:N0}ms, keeping the original",
                fileName, sw.ElapsedMilliseconds);
            return original;
        }
    }

    private async Task<string> GenerateSignedReadUrl(string objectName)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
        var urlSigner = UrlSigner.FromCredential(credential);
        return await urlSigner
            .SignAsync(Bucket, objectName, SignedUrlExpiry, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task<Job> CreateTranscoderJob(
        string inputGcsUri, string outputGcsUri,
        Size2D size, double frameRate, bool hasAudio,
        CancellationToken cancellationToken)
    {
        var client = await TranscoderServiceClient.CreateAsync(cancellationToken).ConfigureAwait(false);
        var parent = LocationName.FromProjectLocation(ProjectId, RegionId);
        const string videoStreamKey = "video_stream0";
        const string audioStreamKey = "audio_stream0";

        var elementaryStreams = new List<ElementaryStream> {
            new ElementaryStream {
                Key = videoStreamKey,
                VideoStream = new TranscoderVideoStream {
                    H264 = new TranscoderVideoStream.Types.H264CodecSettings {
                        BitrateBps = UploadProcessorHelper.EstimateVideoBitrate(size, frameRate),
                        CrfLevel = 23,
                        FrameRate = frameRate,
                        HeightPixels = size.Height,
                        WidthPixels = size.Width,
                    },
                },
            },
        };

        var muxElementaryStreams = new List<string> { videoStreamKey };

        if (hasAudio) {
            elementaryStreams.Add(new ElementaryStream {
                Key = audioStreamKey,
                AudioStream = new TranscoderAudioStream {
                    Codec = "aac",
                    BitrateBps = 128_000,
                },
            });
            muxElementaryStreams.Add(audioStreamKey);
        }

        var job = new Job {
            InputUri = inputGcsUri,
            OutputUri = outputGcsUri,
            Config = new JobConfig {
                ElementaryStreams = { elementaryStreams },
                MuxStreams = {
                    new MuxStream {
                        Key = "output",
                        Container = "mp4",
                        FileName = "output.mp4",
                        ElementaryStreams = { muxElementaryStreams },
                    },
                },
            },
        };
        return await client.CreateJobAsync(parent, job, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Job> PollJobUntilComplete(
        string jobName,
        TimeSpan videoDuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var client = await TranscoderServiceClient.CreateAsync(cancellationToken).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + JobTimeout;
        // Measured on prod: a job runs ~2s plus ~0.2x the video's duration; 0.3x leaves headroom
        var expectedJobDuration = (JobStartupDuration + (videoDuration * 0.3)).Clamp(JobStartupDuration, JobTimeout);
        var stopwatch = Stopwatch.StartNew();

        while (DateTime.UtcNow < deadline) {
            // Most jobs finish within seconds, so the first polls are frequent
            var pollInterval = stopwatch.Elapsed < FastPollPeriod ? FastPollInterval : PollInterval;
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            var job = await client.GetJobAsync(jobName, cancellationToken).ConfigureAwait(false);

            switch (job.State) {
            case Job.Types.ProcessingState.Succeeded:
            case Job.Types.ProcessingState.Failed:
                return job;
            case Job.Types.ProcessingState.Running:
                ReportEstimatedProgress(progress, stopwatch.Elapsed / expectedJobDuration);
                break;
            }
        }

        throw new TimeoutException($"Transcoder job '{jobName}' did not complete within {JobTimeout}.");
    }

    private async Task<long> GetObjectLength(string objectName, CancellationToken cancellationToken)
    {
        var obj = await StorageClient
            .GetObjectAsync(Bucket, objectName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return (long)(obj.Size ?? 0);
    }

    private async Task<Stream> OpenGcsObject(string objectName)
    {
        var stream = new MemoryStream();
        await StorageClient
            .DownloadObjectAsync(Bucket, objectName, stream, cancellationToken: default)
            .ConfigureAwait(false);
        stream.Position = 0;
        return stream;
    }

    private async Task CleanupGcsOutput(string prefix)
    {
        try {
            var objects = StorageClient.ListObjectsAsync(Bucket, prefix);
            await foreach (var obj in objects.ConfigureAwait(false))
                await StorageClient.DeleteObjectAsync(obj, cancellationToken: default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to cleanup GCS transcoder output at '{Prefix}'", prefix);
        }
    }

    // State file management for resumable transcoding

    private async Task<TranscoderState?> TryReadState(string stateObjectName, CancellationToken cancellationToken)
    {
        try {
            var stream = MemoryStreamManager.Default.GetStream();
            await using (stream.ConfigureAwait(false)) {
                await StorageClient
                    .DownloadObjectAsync(Bucket, stateObjectName, stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                stream.Position = 0;
                return await JsonSerializer
                    .DeserializeAsync<TranscoderState>(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Google.GoogleApiException e) when (e.HttpStatusCode is System.Net.HttpStatusCode.NotFound) {
            return null;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to read transcoder state '{StateObject}', proceeding with new job",
                stateObjectName);
            return null;
        }
    }

    private async Task WriteState(string stateObjectName, TranscoderState state, CancellationToken cancellationToken)
    {
        var stream = MemoryStreamManager.Default.GetStream();
        await using (stream.ConfigureAwait(false)) {
            await JsonSerializer
                .SerializeAsync(stream, state, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            stream.Position = 0;
            await StorageClient
                .UploadObjectAsync(Bucket, stateObjectName, "application/json", stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Saved transcoder state '{StateObject}': job '{JobName}'", stateObjectName, state.JobName);
        }
    }

    private async Task DeleteState(string stateObjectName)
    {
        try {
            await StorageClient
                .DeleteObjectAsync(Bucket, stateObjectName, cancellationToken: default)
                .ConfigureAwait(false);
            Log.LogDebug("Deleted transcoder state '{StateObject}'", stateObjectName);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to delete transcoder state '{StateObject}'", stateObjectName);
        }
    }

    private static void ReportEstimatedProgress(IProgress<double>? progress, double elapsedFraction)
        // Neither Transcoder nor the service reports progress, so it's estimated from the elapsed time:
        // 20% → ~84% when the expected time is up → ~92% at twice that, never above 94%
        => progress?.Report(20 + (74 * (1 - Math.Exp(-2 * elapsedFraction))));

    private static string GetStateObjectName(string blobPath)
        => $"transcode-state/{blobPath}.json";

    private static string GetOutputObjectName(string outputPrefix)
        => outputPrefix + "output.mp4";

    // Nested types

    private sealed record TranscoderState(
        string JobName,
        string OutputPrefix,
        int VideoWidth,
        int VideoHeight,
        double Duration);

    // The JSON of services/video-transcoder/main.go

    private sealed record TranscodeRequest(string InputUrl, string OutputUrl, string[] Arguments);

    private sealed record TranscodeResponse(long Size, long TranscodeMillis, long UploadMillis);
}
