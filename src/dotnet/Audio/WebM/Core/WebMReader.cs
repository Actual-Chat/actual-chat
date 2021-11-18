using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM;

[StructLayout(LayoutKind.Sequential)]
public ref struct WebMReader
{
    private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
    public static ILogger? DebugLog { get; set; }

    private readonly Stack<(BaseModel Container, EbmlElement ContainerElement)> _containers;
    private SpanReader _spanReader;
    private EbmlElement _element;
    private BaseModel _entry;
    private bool _resume;

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
        DebugLog?.LogInformation("Read()...");
        ReadResultKind = WebMReadResultKind.None;
        if (_resume) {
            _resume = false;
            DebugLog?.LogInformation("Read: resuming with ReadInternal()");
            return ReadInternal(true);
        }
        var hasElement = ReadElement(_spanReader.Length);
        if (!hasElement) {
            if (_spanReader.Position != _spanReader.Length - 1)
                _resume = true;
            DebugLog?.LogInformation("Read: return false where !hasElement. Resume: {Resume}", _resume);
            return false;
        }

        if (_element.Type is not (EbmlElementType.Binary or EbmlElementType.MasterElement))
            return false;

        var result = ReadInternal();
        return result;
    }

    private bool ReadInternal(bool resume = false)
    {
        DebugLog?.LogInformation("ReadInternal()...");
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

            DebugLog?.LogInformation("ReadInternal: before ReadElement()");
            var canRead = ReadElement(endPosition);
            DebugLog?.LogInformation("ReadInternal: after ReadElement()");
            if (!canRead) {
                _entry = container;
                _element = containerElement;
                _spanReader.Position = beginPosition;
                if (_spanReader.Position < _spanReader.Length)
                    _resume = true;
                else if (resume)
                    _resume = true;
                DebugLog?.LogInformation("ReadInternal: !canRead -> returning false");
                return false;
            }
            DebugLog?.LogInformation("ReadInternal: element Descriptor: {Descriptor}", _element.Descriptor);

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
                DebugLog?.LogInformation("ReadInternal: MasterElement");
                var complex = _element.Descriptor;
                DebugLog?.LogInformation("ReadInternal: before recursive ReadInternal()");
                if (ReadInternal()) {
                    if (CurrentDescriptor.ListEntry)
                        container.FillListEntry(containerElement.Descriptor, complex, _entry);
                    else
                        container.FillComplex(containerElement.Descriptor, complex, _entry);
                }
                else {
                    if (_spanReader.Position < _spanReader.Length)
                        _resume = true;
                    return false;
                }
            }
            else {
                DebugLog?.LogInformation("ReadInternal: not MasterElement");
                if (CurrentDescriptor.ListEntry) {
                    DebugLog?.LogInformation("ReadInternal: ListEntry");
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
                            DebugLog?.LogInformation("ReadInternal: Cluster start -> returning true");
                            return true;
                        }

                    DebugLog?.LogInformation("ReadInternal: before CurrentDescriptor.Identifier.EncodedValue switch");
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
                    DebugLog?.LogInformation("ReadInternal: block is filled -> returning true");
                    return true;
                }

                DebugLog?.LogInformation("ReadInternal: not ListEntry");
                if (CurrentDescriptor.Identifier.EncodedValue == MatroskaSpecification.Void)
                    _spanReader.Position += (int)_element.Size;
                else {
                    DebugLog?.LogInformation("ReadInternal: before container.FillScalar()");
                    container.FillScalar(containerElement.Descriptor,
                        CurrentDescriptor,
                        (int)_element.Size,
                        ref _spanReader);
                }
            }

            beginPosition = _spanReader.Position;
            DebugLog?.LogInformation("ReadInternal: before the end of while(true) cycle");
        }

        LeaveContainer();
        _entry = container;
        ReadResultKind = container.Descriptor.Identifier.EncodedValue switch {
            MatroskaSpecification.EBML => WebMReadResultKind.Ebml,
            MatroskaSpecification.Segment => WebMReadResultKind.Segment,
            MatroskaSpecification.Cluster => WebMReadResultKind.CompleteCluster,
            _ => WebMReadResultKind.None,
        };

        DebugLog?.LogInformation("ReadInternal: returning true");
        return true;
    }

    private bool ReadElement(int readUntil)
    {
        DebugLog?.LogInformation("ReadElement()...");
        var identifier = _spanReader.ReadVInt();
        if (!identifier.HasValue)
            return false;

        DebugLog?.LogInformation("ReadElement: identifier has value");
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
            DebugLog?.LogInformation("ReadElement: identifier is invalid");
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

        DebugLog?.LogInformation("ReadElement: before read size ReadVInt()");
        var size = _spanReader.ReadVInt(8);
        if (!size.HasValue)
            return false;

        var eof = Math.Min(readUntil, _spanReader.Length);
        var sizeValue = size.Value.Value;
        if (_spanReader.Position + (int)sizeValue > eof) {
            DebugLog?.LogInformation("ReadElement: position > EOF");
            if (idValue.EncodedValue != MatroskaSpecification.Cluster
                && idValue.EncodedValue != MatroskaSpecification.Segment) {
                _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);
                DebugLog?.LogInformation("ReadElement: position > EOF - OK for cluster -> returning false");
                return false;
            }
        }

        _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);

        DebugLog?.LogInformation("ReadElement: returning true");
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
