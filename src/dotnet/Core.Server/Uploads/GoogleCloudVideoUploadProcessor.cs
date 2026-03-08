using System.Diagnostics;
using System.Net.Mime;
using ActualChat.Media;
using ActualLab.IO;
using FFMpegCore;
using Google.Api.Gax.ResourceNames;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Google.Cloud.Video.Transcoder.V1;
using SixLabors.ImageSharp;
using TranscoderAudioStream = Google.Cloud.Video.Transcoder.V1.AudioStream;
using TranscoderVideoStream = Google.Cloud.Video.Transcoder.V1.VideoStream;

namespace ActualChat.Uploads;

public class GoogleCloudVideoUploadProcessor : IUploadProcessor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SignedUrlExpiry = TimeSpan.FromHours(1);

    private readonly StorageClient _storageClient;
    private readonly string _bucket;
    private readonly string _projectId;
    private readonly string _regionId;
    private readonly ILogger _log;

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
        _log = log;
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

        var inputGcsUri = $"gs://{_bucket}/{blobFile.BlobPath}";
        var signedUrl = await GenerateSignedReadUrl(blobFile.BlobPath).ConfigureAwait(false);

        // FFProbe via signed URL — reads headers only, fast
        var (mustConvert, size, duration, frameRate) = await GetVideoInfo(upload, signedUrl, cancellationToken).ConfigureAwait(false);
        _log.LogDebug("Video analysis completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (size is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        progress?.Report(10);

        // Thumbnail via signed URL
        stepSw.Restart();
        var thumbnail = await GetThumbnail(upload, signedUrl, duration).ConfigureAwait(false);
        _log.LogDebug("Thumbnail extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (thumbnail is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        progress?.Report(15);

        if (!mustConvert)
            return new ProcessedFile(upload, size, thumbnail);

        try {
            progress?.Report(20);

            // Create and run transcoder job
            var outputPrefix = $"transcode-output/{Guid.NewGuid():N}/";
            var outputGcsUri = $"gs://{_bucket}/{outputPrefix}";

            stepSw.Restart();
            var job = await CreateTranscoderJob(inputGcsUri, outputGcsUri, size.Value, frameRate, cancellationToken).ConfigureAwait(false);
            _log.LogDebug("Transcoder job created in {Elapsed:N0}ms: '{JobName}'",
                stepSw.ElapsedMilliseconds, job.Name);

            // Poll for completion
            stepSw.Restart();
            job = await PollJobUntilComplete(job.Name, progress, cancellationToken).ConfigureAwait(false);
            _log.LogDebug("Transcoding completed in {Elapsed:N0}ms, state: {State}, job: '{JobName}'",
                stepSw.ElapsedMilliseconds, job.State, job.Name);
            progress?.Report(95);

            if (job.State == Job.Types.ProcessingState.Failed) {
                _log.LogError("Transcoder job '{JobName}' failed: {Error}", job.Name, job.Error);
                // Fall back to returning original file
                return new ProcessedFile(upload.AsBinaryFile(), size, thumbnail);
            }

            // Build UploadedBlobFile pointing to transcoder output (same bucket, server-side copy later)
            var outputObjectName = outputPrefix + "output.mp4";
            var outputLength = await GetObjectLength(outputObjectName, cancellationToken).ConfigureAwait(false);
            progress?.Report(98);

            _log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
            return new ProcessedFile(
                new UploadedBlobFile(
                    Path.ChangeExtension(upload.FileName, ".mp4"),
                    "video/mp4",
                    outputLength,
                    outputObjectName,
                    () => OpenGcsObject(outputObjectName)),
                size,
                thumbnail) {
                OnDispose = () => _ = CleanupGcsOutputAsync(outputPrefix),
            };
        }
        catch (Exception e) {
            _log.LogError(e, "Cloud transcoding failed for '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            thumbnail.Delete();
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

    private async Task<(bool MustConvert, Size? Size, TimeSpan Duration, double FrameRate)> GetVideoInfo(
        UploadedFile videoUpload, string signedUrl, CancellationToken cancellationToken)
    {
        try {
            var media = await FFProbe.AnalyseAsync(new Uri(signedUrl), cancellationToken: cancellationToken).ConfigureAwait(false);
            var video = media.PrimaryVideoStream;
            var size = video is null ? (Size?)null : UploadHelper.GetEffectiveSize(video);
            var frameRate = video?.AvgFrameRate ?? 0;
            return (UploadHelper.MustConvertVideo(media, videoUpload.FileName), size, media.Duration, frameRate);
        }
        catch (Exception e) {
            _log.LogDebug(e, "Failed to extract video info from '{FileName}'", videoUpload.FileName);
            return (false, null, TimeSpan.Zero, 0);
        }
    }

    private async Task<UploadedTempFile?> GetThumbnail(UploadedFile videoUpload, string signedUrl, TimeSpan totalVideoDuration)
    {
        if (totalVideoDuration <= TimeSpan.Zero)
            return null;

        try {
            var at = (totalVideoDuration * 0.1).Clamp(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            var thumbnailPath = FilePath.GetApplicationTempDirectory() | $"snapshot_{Guid.NewGuid()}.jpg";

            await FFMpegArguments.FromUrlInput(new Uri(signedUrl), options => options.Seek(at))
                .OutputToFile(thumbnailPath, false, options => options.WithVideoCodec("mjpeg").WithFrameOutputCount(1))
                .ProcessAsynchronously()
                .ConfigureAwait(false);

            var thumbnailFileName = videoUpload.FileName.ChangeExtension(".thumbnail.jpg");
            return new UploadedTempFile(thumbnailFileName, MediaTypeNames.Image.Jpeg, thumbnailPath);
        }
        catch (Exception e) {
            _log.LogError(e, "Failed to extract thumbnail for '{FileName}'", videoUpload.FileName);
            return null;
        }
    }

    private async Task<Job> CreateTranscoderJob(
        string inputGcsUri, string outputGcsUri, Size size, double frameRate, CancellationToken cancellationToken)
    {
        var client = await TranscoderServiceClient.CreateAsync(cancellationToken).ConfigureAwait(false);
        var parent = LocationName.FromProjectLocation(_projectId, _regionId);
        var job = new Job {
            InputUri = inputGcsUri,
            OutputUri = outputGcsUri,
            Config = new JobConfig {
                ElementaryStreams = {
                    new ElementaryStream {
                        Key = "video_stream0",
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
                        Key = "audio_stream0",
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
                        ElementaryStreams = { "video_stream0", "audio_stream0" },
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
            _log.LogDebug(e, "Failed to cleanup GCS transcoder output at '{Prefix}'", prefix);
        }
    }
}
