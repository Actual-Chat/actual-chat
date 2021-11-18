using ActualChat.Audio.WebM.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Audio.WebM;

[StructLayout(LayoutKind.Sequential)]
public ref struct WebMReader
{
    private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
    private readonly Stack<(BaseModel Container, EbmlElement ContainerElement)> _containers;

    private SpanReader _spanReader;
    private EbmlElement _element;
    private BaseModel _entry;
    private bool _resume;

    public static ILogger Log { get; set; } = NullLogger.Instance;

    public WebMReadResultKind ReadResultKind { get; private set; }

    public BaseModel ReadResult => ReadResultKind switch {
        WebMReadResultKind.Ebml => (EBML)_entry,
        WebMReadResultKind.Segment => (Segment)_entry,
        WebMReadResultKind.BeginCluster => (Cluster)_entry,
        WebMReadResultKind.CompleteCluster => (Cluster)_entry,
        WebMReadResultKind.Block => (Block)_entry,
        WebMReadResultKind.BlockGroup => (BlockGroup)_entry,
        _ => throw new InvalidOperationException($"Invalid ReadResultKind = {ReadResultKind}"),
    };

    public ReadOnlySpan<byte> Span => _spanReader.Span;

    public EbmlElementDescriptor CurrentDescriptor => _element.Descriptor;

    public ReadOnlySpan<byte> Tail => _spanReader.Tail;

    public WebMReader(ReadOnlySpan<byte> span)
    {
        _spanReader = new SpanReader(span);
        _element = EbmlElement.Empty;
        _containers = new Stack<(BaseModel Container, EbmlElement ContainerElement)>(16);
        _entry = BaseModel.Empty;
        _resume = false;
        ReadResultKind = WebMReadResultKind.None;
    }

    private WebMReader(State state, ReadOnlySpan<byte> span)
    {
        _spanReader = new SpanReader(span);
        _element = EbmlElement.Empty;
        _containers = state.Containers ?? new Stack<(BaseModel Container, EbmlElement ContainerElement)>(16);
        _entry = state.Entry;
        _resume = state.NotCompleted;
        ReadResultKind = WebMReadResultKind.None;
    }

    public static WebMReader FromState(State state) => new (state, ReadOnlySpan<byte>.Empty);

    public WebMReader WithNewSource(ReadOnlySpan<byte> span) => new (GetState(), span);

    public State GetState()
    {
        if (_containers.Count == 0)
            return new State(
                _resume,
                0,
                _spanReader.Length,
                _entry,
                _containers);

        return new State(
            _resume,
            _spanReader.Position,
            _spanReader.Length - _spanReader.Position,
            _entry,
            _containers);
    }

    public bool Read()
    {
        Log.LogInformation("Read()...");
        ReadResultKind = WebMReadResultKind.None;
        if (_resume) {
            _resume = false;
            Log.LogInformation("Read: Resume with ReadInternal()");
            return ReadInternal(true);
        }
        var hasElement = ReadElement(_spanReader.Length);
        if (!hasElement) {
            if (_spanReader.Position != _spanReader.Length - 1)
                _resume = true;
            Log.LogInformation("Read: return false where !hasElement. Resume: {Resume}", _resume);
            return false;
        }

        if (_element.Type is not (EbmlElementType.Binary or EbmlElementType.MasterElement))
            return false;

        Log.LogInformation("Read: ReadInternal()");
        var result = ReadInternal();
        return result;
    }

    private bool ReadInternal(bool resume = false)
    {
        Log.LogInformation("ReadInternal()...");
        var (container, containerElement) = _element.Type switch {
            EbmlElementType.MasterElement when !resume => EnterContainer(_element),
            _ => _containers.Peek(),
        };

        var beginPosition = _spanReader.Position;
        var endPosition = containerElement.Size == UnknownSize
            ? int.MaxValue
            : beginPosition + (int)containerElement.Size;
        while (true) {
            if (endPosition <= _spanReader.Position)
                break;

            Log.LogInformation("ReadInternal: before ReadElement()");
            var canRead = ReadElement(endPosition);
            Log.LogInformation("ReadInternal: after ReadElement()");
            if (!canRead) {
                _entry = container;
                _element = containerElement;
                _spanReader.Position = beginPosition;
                if (_spanReader.Position < _spanReader.Length)
                    _resume = true;
                else if (resume)
                    _resume = true;
                Log.LogInformation("ReadInternal: return false when !canRead");
                return false;
            }
            Log.LogInformation("ReadInternal: element Descriptor: {Descriptor}", _element.Descriptor);

            if (_element.Identifier.EncodedValue == MatroskaSpecification.Cluster) {
                if (containerElement.Identifier.EncodedValue != MatroskaSpecification.Cluster)
                    LeaveContainer();

                _entry = container;
                _element = containerElement;
                _spanReader.Position = beginPosition;
                ReadResultKind = container.Descriptor.Identifier.EncodedValue switch {
                    MatroskaSpecification.Segment => WebMReadResultKind.Segment,
                    MatroskaSpecification.Cluster => WebMReadResultKind.CompleteCluster,
                    _ => throw new InvalidOperationException($"Unexpected Ebml element type {container.Descriptor}"),
                };
                return true;
            }
            if (_element.Descriptor.Type == EbmlElementType.MasterElement) {
                Log.LogInformation("ReadInternal: MasterElement");
                var complex = _element.Descriptor;
                Log.LogInformation("ReadInternal: before recursive ReadInternal()");
                if (ReadInternal()) {
                    if (CurrentDescriptor.ListEntry)
                        container.FillListEntry(containerElement.Descriptor, complex, _entry!);
                    else
                        container.FillComplex(containerElement.Descriptor, complex, _entry!);
                }
                else {
                    if (_spanReader.Position < _spanReader.Length)
                        _resume = true;
                    return false;
                }
            }
            else {
                Log.LogInformation("ReadInternal: not MasterElement");
                if (CurrentDescriptor.ListEntry) {
                    Log.LogInformation("ReadInternal: ListEntry");
                    if (container is Cluster cluster)
                        if (cluster.BlockGroups == null
                            && cluster.SimpleBlocks == null
                            && cluster.EncryptedBlocks == null) {
                            switch (_element.Descriptor.Identifier.EncodedValue) {
                                case MatroskaSpecification.SimpleBlock:
                                    cluster.SimpleBlocks = new List<SimpleBlock>();
                                    break;
                                case MatroskaSpecification.BlockGroup:
                                    cluster.BlockGroups = new List<BlockGroup>();
                                    break;
                                case MatroskaSpecification.EncryptedBlock:
                                    cluster.EncryptedBlocks = new List<EncryptedBlock>();
                                    break;
                            }

                            _entry = container;
                            _element = containerElement;
                            _spanReader.Position = beginPosition;
                            ReadResultKind = WebMReadResultKind.BeginCluster;
                            _resume = true;
                            Log.LogInformation("ReadInternal: return true when Cluster starts");
                            return true;
                        }

                    Log.LogInformation("ReadInternal: before fill block");
                    switch (CurrentDescriptor.Identifier.EncodedValue) {
                        case MatroskaSpecification.Block:
                            var block = new Block();
                            _entry = block;
                            block.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(containerElement.Descriptor, CurrentDescriptor, block);
                            break;
                        case MatroskaSpecification.BlockAdditional:
                            var blockAdditional = new BlockAdditional();
                            _entry = blockAdditional;
                            blockAdditional.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(containerElement.Descriptor, CurrentDescriptor, blockAdditional);
                            break;
                        case MatroskaSpecification.BlockVirtual:
                            var blockVirtual = new BlockVirtual();
                            _entry = blockVirtual;
                            blockVirtual.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(containerElement.Descriptor, CurrentDescriptor, blockVirtual);
                            break;
                        case MatroskaSpecification.EncryptedBlock:
                            var encryptedBlock = new EncryptedBlock();
                            _entry = encryptedBlock;
                            encryptedBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(containerElement.Descriptor, CurrentDescriptor, encryptedBlock);
                            break;
                        case MatroskaSpecification.SimpleBlock:
                            var simpleBlock = new SimpleBlock();
                            _entry = simpleBlock;
                            simpleBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(containerElement.Descriptor, CurrentDescriptor, simpleBlock);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }

                    ReadResultKind = WebMReadResultKind.Block;
                    _resume = true;
                    Log.LogInformation("ReadInternal: return true after block was filled");
                    return true;
                }

                Log.LogInformation("ReadInternal: not ListEntry");
                if (CurrentDescriptor.Identifier.EncodedValue == MatroskaSpecification.Void)
                    _spanReader.Position += (int)_element.Size;
                else
                    Log.LogInformation(
                        "ReadInternal: before container.FillScalar(), Container: {Container}, CurrentDescriptor: {CurrentDescriptor}",
                        _container.Descriptor,
                        CurrentDescriptor);
                    container.FillScalar(containerElement.Descriptor,
                        CurrentDescriptor,
                        (int)_element.Size,
                        ref _spanReader);
            }

            beginPosition = _spanReader.Position;
            Log.LogInformation("ReadInternal: before end of while(true) cycle");
        }

        LeaveContainer();
        _entry = container;
        ReadResultKind = container.Descriptor.Identifier.EncodedValue switch {
            MatroskaSpecification.EBML => WebMReadResultKind.Ebml,
            MatroskaSpecification.Segment => WebMReadResultKind.Segment,
            MatroskaSpecification.Cluster => WebMReadResultKind.CompleteCluster,
            _ => WebMReadResultKind.None,
        };

        Log.LogInformation("ReadInternal: complete");
        return true;
    }

    private bool ReadElement(int readUntil)
    {
        Log.LogInformation("ReadElement()...");
        var identifier = _spanReader.ReadVInt();
        if (!identifier.HasValue)
            return false;

        Log.LogInformation("ReadElement: identifier has value");
        var idValue = identifier.Value;
        if (idValue.IsReserved) {
            var start = _spanReader.Position > 8
                ? _spanReader.Position - 8
                : 0;
            var end = _spanReader.Position < _spanReader.Length - 4
                ? _spanReader.Position + 4
                : _spanReader.Length - 1;
            var errorBlock = BitConverter.ToString(_spanReader.Span[start..end].ToArray());
            throw new EbmlDataFormatException(
                "Invalid element identifier value. Id: "
                + idValue
                + "; Position: "
                + _spanReader.Position
                + "; Error At: "
                + errorBlock);
        }

        var isValid = MatroskaSpecification.ElementDescriptors.TryGetValue(idValue, out var elementDescriptor);
        if (!isValid) {
            Log.LogInformation("ReadElement: identifier is invalid");
            var start = _spanReader.Position > 8
                ? _spanReader.Position - 8
                : 0;
            var end = _spanReader.Position < _spanReader.Length - 4
                ? _spanReader.Position + 4
                : _spanReader.Length - 1;
            var errorBlock = BitConverter.ToString(_spanReader.Span[start..end].ToArray());
            throw new EbmlDataFormatException(
                "Invalid element identifier value - not white-listed. Id: "
                + idValue
                + "; Position: "
                + _spanReader.Position
                + "; Error At: "
                + errorBlock);
        }

        Log.LogInformation("ReadElement: before read size ReadVInt()");
        var size = _spanReader.ReadVInt(8);
        if (!size.HasValue)
            return false;

        var eof = Math.Min(readUntil, _spanReader.Length);
        var sizeValue = size.Value.Value;
        if (_spanReader.Position + (int)sizeValue > eof) {
            Log.LogInformation("ReadElement: position > eof");
            if (idValue.EncodedValue != MatroskaSpecification.Cluster
                && idValue.EncodedValue != MatroskaSpecification.Segment) {
                _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);
                Log.LogInformation("ReadElement: position > eof - OK for cluster and return false");
                return false;
            }
        }

        _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);

        Log.LogInformation("ReadElement: completed");
        return true;
    }

    private (BaseModel Container, EbmlElement ContainerElement) EnterContainer(EbmlElement containerElement)
    {
        if (_element.Type != EbmlElementType.None && _element.Type != EbmlElementType.MasterElement)
            throw new InvalidOperationException();
        if (_element.Type == EbmlElementType.MasterElement && _element.HasInvalidIdentifier)
            throw new InvalidOperationException();

        var result = (containerElement.Descriptor.CreateInstance(), containerElement);
        _containers.Push(result);
        _element = EbmlElement.Empty;

        return result;
    }

    private void LeaveContainer()
    {
        if (_containers.Count == 0) throw new InvalidOperationException();

        var (_, containerElement) = _containers.Pop();
        _element = containerElement;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct State
    {
        public readonly bool NotCompleted;
        public readonly int Position;
        public readonly int Remaining;
        public readonly BaseModel Entry;
        public readonly Stack<(BaseModel Container, EbmlElement ContainerElement)>? Containers;

        public State(
            bool resume,
            int position,
            int remaining,
            BaseModel entry,
            Stack<(BaseModel Container, EbmlElement ContainerElement)> containers)
        {
            NotCompleted = resume;
            Position = position;
            Remaining = remaining;
            Entry = entry;
            Containers = containers;
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        public bool IsEmpty => Containers == null || Containers.Count == 0;
    }
}
