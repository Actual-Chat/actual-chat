using FFMpegCore;
using Google.Api.Gax.ResourceNames;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Google.Cloud.Video.Transcoder.V1;
using SixLabors.ImageSharp;
using TranscoderAudioStream = Google.Cloud.Video.Transcoder.V1.AudioStream;
using TranscoderVideoStream = Google.Cloud.Video.Transcoder.V1.VideoStream;

namespace ActualChat.Uploads;

public sealed class GoogleCloudVideoUploadProcessor : IUploadProcessor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(14);
    private static readonly TimeSpan SignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly StorageClient _storageClient;
    private readonly string _bucket;
    private readonly string _projectId;
    private readonly string _regionId;

    private ILogger Log { get; }

    public GoogleCloudVideoUploadProcessor(
        StorageClient storageClient,
        string bucket,
        string projectId,
        string regionId,
        ILogger<GoogleCloudVideoUploadProcessor> log)
    {
        _storageClient = storageClient;
        _bucket = bucket;
        _projectId = projectId;
        _regionId = regionId;
        Log = log;
    }

    public bool Supports(string contentType)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        progress?.Report(0);

        if (upload is not UploadedBlobFile blobFile)
            throw new InvalidOperationException(
                $"{nameof(GoogleCloudVideoUploadProcessor)} requires {nameof(UploadedBlobFile)} but received {upload.GetType().Name}.");

        var stateObjectName = GetStateObjectName(blobFile.BlobPath);
        var savedState = await TryReadState(stateObjectName, cancellationToken).ConfigureAwait(false);
        var signedUrl = await GenerateSignedReadUrl(blobFile.BlobPath).ConfigureAwait(false);

        // 1. Video info — from saved state or by analyzing
        Size size;
        TimeSpan duration;
        double frameRate;
        bool mustConvert;

        if (savedState != null) {
            Log.LogDebug("Resuming transcoder job '{JobName}' for '{FileName}'",
                savedState.JobName, upload.FileName);
            size = new Size(savedState.VideoWidth, savedState.VideoHeight);
            duration = TimeSpan.FromSeconds(savedState.Duration);
            frameRate = 0; // Not important since we're resuming a job.
            mustConvert = true;
        }
        else {
            IMediaAnalysis? mediaInfo = null;
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

            mustConvert = UploadProcessorHelper.MustConvertVideo(videoStream)
                || UploadProcessorHelper.MustConvertVideo(mediaInfo!.Format);
            (size, duration, frameRate) = UploadProcessorHelper.AnalyzeVideo(videoStream);
        }

        progress?.Report(10);

        // 2. Snapshot
        stepSw.Restart();
        var snapshot = await UploadProcessorHelper.Snapshot(new Uri(signedUrl), upload.FileName, duration).ConfigureAwait(false);
        Log.LogDebug("Snapshot extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (snapshot is null)
            return new ProcessedFile(upload.AsBinaryFile(), size);

        progress?.Report(15);

        if (!mustConvert)
            return new ProcessedFile(UploadProcessorHelper.EnsureMp4Extension(upload), size, snapshot);

        // 3. Create or resume a transcoder job + poll
        try {
            progress?.Report(20);

            string outputPrefix;
            string jobName;

            if (savedState != null) {
                outputPrefix = savedState.OutputPrefix;
                jobName = savedState.JobName;
            }
            else {
                outputPrefix = $"transcode-output/{Guid.NewGuid():N}/";
                var inputGcsUri = $"gs://{_bucket}/{blobFile.BlobPath}";
                var outputGcsUri = $"gs://{_bucket}/{outputPrefix}";

                stepSw.Restart();
                var createdJob = await CreateTranscoderJob(inputGcsUri, outputGcsUri, size, frameRate, cancellationToken).ConfigureAwait(false);
                jobName = createdJob.Name;
                Log.LogDebug("Transcoder job created in {Elapsed:N0}ms: '{JobName}'",
                    stepSw.ElapsedMilliseconds, jobName);

                // Save state BEFORE polling so we can resume after a restart
                await WriteState(stateObjectName, jobName, outputPrefix, size, duration, cancellationToken).ConfigureAwait(false);
            }

            // 4. Poll for completion
            stepSw.Restart();
            var job = await PollJobUntilComplete(jobName, progress, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Transcoding completed in {Elapsed:N0}ms, state: {State}, job: '{JobName}'",
                stepSw.ElapsedMilliseconds, job.State, job.Name);

            progress?.Report(95);

            if (job.State == Job.Types.ProcessingState.Failed) {
                Log.LogError("Transcoder job '{JobName}' failed: {Error}", job.Name, job.Error);
                return new ProcessedFile(upload.AsBinaryFile(), size, snapshot);
            }

            // 5. Build result
            var outputObjectName = outputPrefix + "output.mp4";
            var outputLength = await GetObjectLength(outputObjectName, cancellationToken).ConfigureAwait(false);
            progress?.Report(98);

            Log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
            return new ProcessedFile(
                new UploadedBlobFile(
                    Path.ChangeExtension(upload.FileName, ".mp4"),
                    "video/mp4",
                    outputLength,
                    outputObjectName,
                    () => OpenGcsObject(outputObjectName)),
                size,
                snapshot) {
                OnDispose = () => {
                    _ = CleanupGcsOutputAsync(outputPrefix);
                    _ = DeleteState(stateObjectName);
                },
            };
        }
        catch (Exception e) {
            Log.LogError(e, "Cloud transcoding failed for '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            _ = DeleteState(stateObjectName);
            snapshot.Delete();
            throw;
        }
    }

    // Private methods

    private async Task<string> GenerateSignedReadUrl(string objectName)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
        var urlSigner = UrlSigner.FromCredential(credential);
        return await urlSigner.SignAsync(_bucket, objectName, SignedUrlExpiry, cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<Job> CreateTranscoderJob(
        string inputGcsUri, string outputGcsUri, Size size, double frameRate, CancellationToken cancellationToken)
    {
        var client = await TranscoderServiceClient.CreateAsync(cancellationToken).ConfigureAwait(false);
        var parent = LocationName.FromProjectLocation(_projectId, _regionId);
        const string videoStreamKey = "video_stream0";
        const string audioStreamKey = "audio_stream0";
        var job = new Job {
            InputUri = inputGcsUri,
            OutputUri = outputGcsUri,
            Config = new JobConfig {
                ElementaryStreams = {
                    new ElementaryStream {
                        Key = videoStreamKey,
                        VideoStream = new TranscoderVideoStream {
                            H264 = new TranscoderVideoStream.Types.H264CodecSettings {
                                BitrateBps = EstimateVideoBitrate(size, frameRate),
                                CrfLevel = 23,
                                FrameRate = frameRate,
                                HeightPixels = size.Height,
                                WidthPixels = size.Width,
                            },
                        },
                    },
                    new ElementaryStream {
                        Key = audioStreamKey,
                        AudioStream = new TranscoderAudioStream {
                            Codec = "aac",
                            BitrateBps = 128_000,
                        },
                    },
                },
                MuxStreams = {
                    new MuxStream {
                        Key = "output",
                        Container = "mp4",
                        FileName = "output.mp4",
                        ElementaryStreams = { videoStreamKey, audioStreamKey },
                    },
                },
            },
        };
        return await client.CreateJobAsync(parent, job, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Job> PollJobUntilComplete(string jobName, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var client = await TranscoderServiceClient.CreateAsync(cancellationToken).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + JobTimeout;
        var pollCount = 0;

        while (DateTime.UtcNow < deadline) {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            var job = await client.GetJobAsync(jobName, cancellationToken).ConfigureAwait(false);
            pollCount++;

            switch (job.State) {
            case Job.Types.ProcessingState.Succeeded:
            case Job.Types.ProcessingState.Failed:
                return job;
            case Job.Types.ProcessingState.Running:
                // Estimate progress between 20% and 95% based on elapsed polls
                // The job timeout is 30min with 5s polls = ~360 max polls
                if (progress is not null) {
                    var estimatedProgress = Math.Min(20 + (0.75 * pollCount / 3.6), 94);
                    progress.Report(estimatedProgress);
                }
                break;
            }
        }

        throw new TimeoutException($"Transcoder job '{jobName}' did not complete within {JobTimeout}.");
    }

    private async Task<long> GetObjectLength(string objectName, CancellationToken cancellationToken)
    {
        var obj = await _storageClient.GetObjectAsync(_bucket, objectName, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (long)(obj.Size ?? 0);
    }

    private async Task<Stream> OpenGcsObject(string objectName)
    {
        var stream = new MemoryStream();
        await _storageClient.DownloadObjectAsync(_bucket, objectName, stream, cancellationToken: default).ConfigureAwait(false);
        stream.Position = 0;
        return stream;
    }

    private static int EstimateVideoBitrate(Size size, double frameRate)
    {
        // Estimate a reasonable bitrate based on resolution and frame rate.
        // Formula: pixels * frameFactor * bitsPerPixel, clamped to [500Kbps, 20Mbps].
        var pixels = size.Width * size.Height;
        var frameFactor = Math.Max(frameRate, 24) / 30.0;
        var bps = (int)(pixels * frameFactor * 0.1);
        return Math.Clamp(bps, 500_000, 20_000_000);
    }

    private async Task CleanupGcsOutputAsync(string prefix)
    {
        try {
            var objects = _storageClient.ListObjectsAsync(_bucket, prefix);
            await foreach (var obj in objects.ConfigureAwait(false))
                await _storageClient.DeleteObjectAsync(obj, cancellationToken: default).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to cleanup GCS transcoder output at '{Prefix}'", prefix);
        }
    }

    // State file management for resumable transcoding

    private static string GetStateObjectName(string blobPath)
        => $"transcode-state/{blobPath}.json";

    private async Task<TranscoderState?> TryReadState(string stateObjectName, CancellationToken cancellationToken)
    {
        try {
            var stream = MemoryStreamManager.Default.GetStream();
            await using (stream.ConfigureAwait(false)) {
                await _storageClient.DownloadObjectAsync(_bucket, stateObjectName, stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Position = 0;
                return await JsonSerializer.DeserializeAsync<TranscoderState>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Google.GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound) {
            return null;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to read transcoder state '{StateObject}', proceeding with new job", stateObjectName);
            return null;
        }
    }

    private async Task WriteState(
        string stateObjectName, string jobName, string outputPrefix,
        Size size, TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var state = new TranscoderState(jobName, outputPrefix, size.Width, size.Height, duration.TotalSeconds);
        var stream = MemoryStreamManager.Default.GetStream();
        await using (stream.ConfigureAwait(false)) {
            await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            await _storageClient.UploadObjectAsync(_bucket, stateObjectName, "application/json", stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Saved transcoder state '{StateObject}': job '{JobName}'", stateObjectName, jobName);
        }
    }

    private async Task DeleteState(string stateObjectName)
    {
        try {
            await _storageClient.DeleteObjectAsync(_bucket, stateObjectName, cancellationToken: default).ConfigureAwait(false);
            Log.LogDebug("Deleted transcoder state '{StateObject}'", stateObjectName);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to delete transcoder state '{StateObject}'", stateObjectName);
        }
    }

    // Nested types

    private sealed record TranscoderState(
        string JobName,
        string OutputPrefix,
        int VideoWidth,
        int VideoHeight,
        double Duration);
}
