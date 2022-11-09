// using Google.Protobuf;
//
// namespace ActualChat.Transcription.IntegrationTests;
//
// public class GoogleNativeTranscriberTest : TestBase
// {
//     public GoogleNativeTranscriberTest(ITestOutputHelper @out) : base(@out) { }
//
//
//     [Fact(Skip = "Manual")]
//     public async Task GoogleMultiFileStreamedRecognizeTest()
//     {
//         var audio1 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "1.webm"));
//         var audio2 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "2.webm"));
//         var audio3 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "3.webm"));
//         var audio4 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "4.webm"));
//         var audio56 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "56.webm"));
//         var audio789 = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "789.webm"));
//         var audioboy = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "boy.webm"));
//         var client = await SpeechClient.CreateAsync();
//         var config = new RecognitionConfig {
//             Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
//             SampleRateHertz = 48000,
//             LanguageCode = LanguageCodes.Russian.Russia,
//             EnableAutomaticPunctuation = true,
//             EnableWordConfidence = true,
//             EnableWordTimeOffsets = true,
//         };
//         var streamingRecognize = client.StreamingRecognize();
//         await streamingRecognize.WriteAsync(new () {
//             StreamingConfig = new () {
//                 Config = config,
//                 InterimResults = true,
//                 SingleUtterance = false,
//             },
//         });
//
//         var writeTask = WriteToStream(streamingRecognize,
//             audio1,
//             audio2,
//             audio3,
//             audio4,
//             audio56,
//             audio789,
//             audioboy);
//
//         await foreach (var response in streamingRecognize.GetResponseStream())
//             if (response.Error != null)
//                 Out.WriteLine(response.Error.Message);
//             else
//                 Out.WriteLine(response.ToString());
//
//         await writeTask;
//     }
//
//     [Fact(Skip = "Manual")]
//     public async Task GoogleRecognizeTest()
//     {
//         var audioBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"));
//         var audio = RecognitionAudio.FromBytes(audioBytes);
//         var client = await SpeechClient.CreateAsync();
//         var config = new RecognitionConfig {
//             Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
//             SampleRateHertz = 48000,
//             LanguageCode = LanguageCodes.Russian.Russia,
//             EnableAutomaticPunctuation = true,
//         };
//         var response = await client.RecognizeAsync(config, audio);
//         Out.WriteLine(response.ToString());
//     }
//
//     [Fact(Skip = "Manual")]
//     public async Task GoogleStreamedRecognizePunctuationTest()
//     {
//         var audioBytes =
//             await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "large-file.webm"));
//         var client = await SpeechClient.CreateAsync();
//         var config = new RecognitionConfig {
//             Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
//             SampleRateHertz = 48000,
//             LanguageCode = LanguageCodes.Russian.Russia,
//             EnableAutomaticPunctuation = true,
//             EnableWordConfidence = true,
//             EnableWordTimeOffsets = true,
//         };
//         var streamingRecognize = client.StreamingRecognize();
//         await streamingRecognize.WriteAsync(new () {
//             StreamingConfig = new () {
//                 Config = config,
//                 InterimResults = true,
//                 SingleUtterance = false,
//             },
//         });
//
//         var writeTask = WriteToStream(streamingRecognize, audioBytes);
//
//         await foreach (var response in streamingRecognize.GetResponseStream())
//             if (response.Error != null)
//                 Out.WriteLine(response.Error.Message);
//             else
//                 Out.WriteLine(response.ToString());
//
//         await writeTask;
//     }
//
//     [Fact(Skip = "Manual")]
//     public async Task GoogleStreamedRecognizeRepeatTest()
//     {
//         var audioBytes =
//             await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "pauses.webm"));
//         var client = await SpeechClient.CreateAsync();
//         var config = new RecognitionConfig {
//             Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
//             SampleRateHertz = 48000,
//             LanguageCode = LanguageCodes.Russian.Russia,
//             EnableAutomaticPunctuation = true,
//             EnableWordConfidence = true,
//             EnableWordTimeOffsets = true,
//         };
//         var streamingRecognize = client.StreamingRecognize();
//         await streamingRecognize.WriteAsync(new () {
//             StreamingConfig = new () {
//                 Config = config,
//                 InterimResults = true,
//                 SingleUtterance = false,
//             },
//         });
//
//         var writeTask = WriteToStream(streamingRecognize, audioBytes);
//
//         await foreach (var response in streamingRecognize.GetResponseStream())
//             if (response.Error != null)
//                 Out.WriteLine(response.Error.Message);
//             else
//                 Out.WriteLine(response.ToString());
//
//         await writeTask;
//     }
//
//     [Fact(Skip = "Manual")]
//     public async Task GoogleStreamedRecognizeTest()
//     {
//         var audioBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"));
//         var client = await SpeechClient.CreateAsync();
//         var config = new RecognitionConfig {
//             Encoding = RecognitionConfig.Types.AudioEncoding.WebmOpus,
//             SampleRateHertz = 48000,
//             LanguageCode = LanguageCodes.Russian.Russia,
//             EnableAutomaticPunctuation = true,
//             EnableWordConfidence = true,
//             EnableWordTimeOffsets = true,
//         };
//         var streamingRecognize = client.StreamingRecognize();
//         await streamingRecognize.WriteAsync(new () {
//             StreamingConfig = new () {
//                 Config = config,
//                 InterimResults = true,
//                 SingleUtterance = false,
//             },
//         });
//
//         var writeTask = WriteToStream(streamingRecognize, audioBytes);
//
//         await foreach (var response in streamingRecognize.GetResponseStream())
//             if (response.Error != null)
//                 Out.WriteLine(response.Error.Message);
//             else
//                 Out.WriteLine(response.ToString());
//
//         await writeTask;
//     }
//
//     private async Task WriteToStream(SpeechClient.StreamingRecognizeStream stream, params byte[][] byteArrays)
//     {
//         foreach (var bytes in byteArrays)
//         foreach (var chunk in bytes.Chunk(200)) {
//             await Task.Delay(30);
//             await stream.WriteAsync(new () {
//                 AudioContent = ByteString.CopyFrom(chunk.ToArray()),
//             });
//         }
//
//         await stream.WriteCompleteAsync();
//     }
// }
