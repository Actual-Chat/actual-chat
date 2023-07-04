using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ActualChat.App.Server.Module;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ActualChat.App.Server;

/// <summary>
/// Console formatter for use with Google Cloud Logging. <br/>
/// Based on <see href="https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Console/src/JsonConsoleFormatter.cs" />
/// </summary>
public sealed class GoogleCloudConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;
    private JsonConsoleFormatterOptions _options;

    /// <summary>
    /// Constructor accepting just an options, to simplify testing.
    /// </summary>
    /// <param name="options">The formatter options. Must not be null.</param>
    private GoogleCloudConsoleFormatter(JsonConsoleFormatterOptions options)
        : base(nameof(GoogleCloudConsoleFormatter))
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _optionsReloadToken = null;
    }

    /// <summary>
    /// Constructs a new formatter which uses the specified monitor to retrieve
    /// options and watch for options changes.
    /// </summary>
    /// <param name="optionsMonitor">The monitor to observe for changes in options. Must not be null.</param>
    public GoogleCloudConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> optionsMonitor)
        : this(optionsMonitor.CurrentValue)
        => _optionsReloadToken = optionsMonitor.OnChange(options => _options = options);

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter!.Invoke(logEntry.State, logEntry.Exception);
        if (logEntry.Exception == null && message == null!)
            return;

        const int defaultBufferSize = 1024;
        using var output = new PooledByteBufferWriter(defaultBufferSize);
        using var writer = new Utf8JsonWriter(output, _options.JsonWriterOptions);

        writer.WriteStartObject();
        writer.WriteString("message", message);
        writer.WriteString("version", ServerAppModule.AppVersion);
        if (logEntry.Exception != null) {
            writer.WriteString("exception", logEntry.Exception.ToString());
        }
        writer.WriteString("severity", GetSeverity(logEntry.LogLevel));
        writer.WriteString("category", logEntry.Category);
        if (logEntry.State != null!) {
            writer.WriteStartObject("state");
            writer.WriteString("message", logEntry.State.ToString());
            if (logEntry.State is IEnumerable<KeyValuePair<string, object>> stateProperties) {
                foreach (var item in stateProperties) {
                    WriteItem(writer, item);
                }
            }
            writer.WriteEndObject();
        }
        WriteScopeInformation(writer, scopeProvider);
        writer.WriteEndObject();
        writer.Flush();

        textWriter.WriteLine(Encoding.UTF8.GetString(output.WrittenMemory.Span));

        static string GetSeverity(LogLevel logLevel) => logLevel switch {
            LogLevel.Trace => "DEBUG",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARNING",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRITICAL",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider? scopeProvider)
    {
        if (!_options.IncludeScopes || scopeProvider == null)
            return;

        writer.WriteStartArray("scopes");
        scopeProvider.ForEachScope((scope, state) => {
            if (scope is IEnumerable<KeyValuePair<string, object>> scopeItems) {
                state.WriteStartObject();
                state.WriteString("message", scope.ToString());
                foreach (KeyValuePair<string, object> item in scopeItems) {
                    WriteItem(state, item);
                }
                state.WriteEndObject();
            }
            else {
                state.WriteStringValue(ToInvariantString(scope));
            }
        }, writer);
        writer.WriteEndArray();
    }

    private static void WriteItem(Utf8JsonWriter writer, KeyValuePair<string, object> item)
    {
        var key = item.Key;
        switch (item.Value) {
        case bool boolValue:
            writer.WriteBoolean(key, boolValue);
            break;
        case byte byteValue:
            writer.WriteNumber(key, byteValue);
            break;
        case sbyte sbyteValue:
            writer.WriteNumber(key, sbyteValue);
            break;
        case char charValue:
            writer.WriteString(key, MemoryMarshal.CreateSpan(ref charValue, 1));
            break;
        case decimal decimalValue:
            writer.WriteNumber(key, decimalValue);
            break;
        case double doubleValue:
            writer.WriteNumber(key, doubleValue);
            break;
        case float floatValue:
            writer.WriteNumber(key, floatValue);
            break;
        case int intValue:
            writer.WriteNumber(key, intValue);
            break;
        case uint uintValue:
            writer.WriteNumber(key, uintValue);
            break;
        case long longValue:
            writer.WriteNumber(key, longValue);
            break;
        case ulong ulongValue:
            writer.WriteNumber(key, ulongValue);
            break;
        case short shortValue:
            writer.WriteNumber(key, shortValue);
            break;
        case ushort ushortValue:
            writer.WriteNumber(key, ushortValue);
            break;
        case null:
            writer.WriteNull(key);
            break;
        default:
            writer.WriteString(key, ToInvariantString(item.Value));
            break;
        }
    }

    public void Dispose() => _optionsReloadToken?.Dispose();

    private static string? ToInvariantString(object? obj) => Convert.ToString(obj, CultureInfo.InvariantCulture);

    /// <summary>
    /// <see href="https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/Json/PooledByteBufferWriter.cs"/>
    /// </summary>
    private sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _rentedBuffer;
        private int _index;

        private const int MinimumBufferSize = 256;

        public PooledByteBufferWriter(int initialCapacity)
        {
            Debug.Assert(initialCapacity > 0);

            _rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _index = 0;
        }

        public ReadOnlyMemory<byte> WrittenMemory {
            get {
                Debug.Assert(_rentedBuffer != null);
                Debug.Assert(_index <= _rentedBuffer.Length);
                return _rentedBuffer.AsMemory(0, _index);
            }
        }

        public int WrittenCount {
            get {
                Debug.Assert(_rentedBuffer != null);
                return _index;
            }
        }

        public int Capacity {
            get {
                Debug.Assert(_rentedBuffer != null);
                return _rentedBuffer.Length;
            }
        }

        public int FreeCapacity {
            get {
                Debug.Assert(_rentedBuffer != null);
                return _rentedBuffer.Length - _index;
            }
        }

        public void Clear() => ClearHelper();

        private void ClearHelper()
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(_index <= _rentedBuffer.Length);

            _rentedBuffer.AsSpan(0, _index).Clear();
            _index = 0;
        }

        // Returns the rented buffer back to the pool
        public void Dispose()
        {
            if (_rentedBuffer == null!)
                return;

            ClearHelper();
            byte[] toReturn = _rentedBuffer;
            _rentedBuffer = null!;
            ArrayPool<byte>.Shared.Return(toReturn);
        }

        public void Advance(int count)
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(count >= 0);
            Debug.Assert(_index <= _rentedBuffer.Length - count);

            _index += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsMemory(_index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsSpan(_index);
        }

        internal Task WriteToStreamAsync(Stream destination, CancellationToken cancellationToken)
            => destination.WriteAsync(_rentedBuffer, 0, _index, cancellationToken);

        internal void WriteToStream(Stream destination) => destination.Write(_rentedBuffer, 0, _index);

        private void CheckAndResizeBuffer(int sizeHint)
        {
            Debug.Assert(_rentedBuffer != null);
            Debug.Assert(sizeHint >= 0);

            if (sizeHint == 0) {
                sizeHint = MinimumBufferSize;
            }

            int availableSpace = _rentedBuffer.Length - _index;

            if (sizeHint > availableSpace) {
                int currentLength = _rentedBuffer.Length;
                int growBy = Math.Max(sizeHint, currentLength);

                int newSize = currentLength + growBy;

                if ((uint)newSize > int.MaxValue) {
                    newSize = currentLength + sizeHint;
                    if ((uint)newSize > int.MaxValue) {
                        ThrowHelper.ThrowOutOfMemoryException_BufferMaximumSizeExceeded((uint)newSize);
                    }
                }

                byte[] oldBuffer = _rentedBuffer;

                _rentedBuffer = ArrayPool<byte>.Shared.Rent(newSize);

                Debug.Assert(oldBuffer.Length >= _index);
                Debug.Assert(_rentedBuffer.Length >= _index);

                Span<byte> previousBuffer = oldBuffer.AsSpan(0, _index);
                previousBuffer.CopyTo(_rentedBuffer);
                previousBuffer.Clear();
                ArrayPool<byte>.Shared.Return(oldBuffer);
            }

            Debug.Assert(_rentedBuffer.Length - _index > 0);
            Debug.Assert(_rentedBuffer.Length - _index >= sizeHint);
        }
    }

    private static class ThrowHelper
    {
#pragma warning disable MA0012
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowOutOfMemoryException_BufferMaximumSizeExceeded(uint capacity)
            => throw new OutOfMemoryException(
                $"Cannot allocate a buffer of size {capacity.Format()}.");
    }
}
