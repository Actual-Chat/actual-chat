using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    // TODO(AK): implement reading of Blocks (now the smallest entry is Cluster which has ~30 sec duration)!!!
    public ref struct WebMReader
    {
        private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
        private readonly Stack<EbmlElement> _containers;

        private SpanReader _spanReader;
        private WebMReadResultKind _readResultKind;
        private EbmlElement _container;
        private EbmlElement _element;
        private BaseModel _entry;
        private BaseModel _containerEntry;
        private bool _resume;


        public WebMReader(ReadOnlySpan<byte> span) : this(span, (uint)span.Length)
        { }

        public WebMReader(ReadOnlySpan<byte> span, uint maxBytesToRead)
        {
            _container = new EbmlElement(VInt.UnknownSize(2), maxBytesToRead, MatroskaSpecification.UnknownDescriptor);
            _element = EbmlElement.Empty;
            _spanReader = new SpanReader(span);
            _readResultKind = WebMReadResultKind.None;
            _containers = new Stack<EbmlElement>(16);
            _entry = BaseModel.Empty;
            _containerEntry = BaseModel.Empty;
            _resume = false;
        }

        private WebMReader(State state, ReadOnlySpan<byte> span)
        {
            _container = state.ContainerElement;
            _element = EbmlElement.Empty;
            _spanReader = new SpanReader(span);
            _readResultKind = WebMReadResultKind.None;
            _containers = new Stack<EbmlElement>(16);
            _entry = state.Container;
            _containerEntry = state.Container;
            _resume = state.NotCompleted;
        }

        public static WebMReader FromState(State state)
        {
            return new WebMReader(state, ReadOnlySpan<byte>.Empty);
        }

        public WebMReadResultKind ReadResultKind => _readResultKind;

        public BaseModel ReadResult => _readResultKind switch {
            WebMReadResultKind.Ebml => (EBML)_entry,
            WebMReadResultKind.Segment => (Segment)_entry,
            WebMReadResultKind.BeginCluster => (Cluster)_entry,
            WebMReadResultKind.CompleteCluster => (Cluster)_entry,
            WebMReadResultKind.Block => (Block)_entry,
            WebMReadResultKind.BlockGroup => (BlockGroup)_entry,
            _ => throw new InvalidOperationException($"Invalid ReadResultKind = {ReadResultKind}")
        };

        public ReadOnlySpan<byte> Span => _spanReader.Span;

        public EbmlElementDescriptor CurrentDescriptor => _element.Descriptor;

        public ReadOnlySpan<byte> Tail => _spanReader.Tail;

        public State GetState()
        {
            var remaining = _spanReader.Length - _spanReader.Position;
            return new State(_resume, _spanReader.Position, remaining, _container, _containerEntry);
        }

        public WebMReader WithNewSource(ReadOnlySpan<byte> span)
        {
            return new WebMReader(GetState(), span);
        }

        public bool Read()
        {
            _readResultKind = WebMReadResultKind.None;
            if (_resume) {
                _resume = false;
                return ReadInternal(true);
            }
            var hasElement = ReadElement(_spanReader.Length);
            if (!hasElement) {
                if (_spanReader.Position != _spanReader.Length - 1)
                    _resume = true;
                return false;
            }

            if (_element.Type is not (EbmlElementType.Binary or EbmlElementType.MasterElement))
                return false;

            var result = ReadInternal();
            _containerEntry = _entry;
            return result;
        }

        private bool ReadInternal(bool resume = false)
        {
            BaseModel container;
            if (_element.Type == EbmlElementType.MasterElement) {
                if (resume)
                    container = _containerEntry;
                else {
                    EnterContainer();
                    container = _container.Descriptor.CreateInstance();
                }
            }
            else
                container = _containerEntry;

            var beginPosition = _spanReader.Position;
            var endPosition = _container.Size == UnknownSize
                ? int.MaxValue
                : beginPosition + (int)_container.Size;
            while (true) {
                if (endPosition <= _spanReader.Position)
                    break;

                var canRead = ReadElement(endPosition);
                if (!canRead) {
                    _entry = container;
                    _element = _container;
                    _spanReader.Position = beginPosition;
                    if (_spanReader.Position < _spanReader.Length)
                        _resume = true;
                    else if (resume)
                        _resume = true;
                    return false;
                }

                if (_element.Identifier.EncodedValue == MatroskaSpecification.Cluster) {
                    if (_container.Identifier.EncodedValue != MatroskaSpecification.Cluster)
                        LeaveContainer();

                    _entry = container;
                    _element = _container;
                    _spanReader.Position = beginPosition;
                    _readResultKind = container.Descriptor.Identifier.EncodedValue switch {
                        MatroskaSpecification.Segment => WebMReadResultKind.Segment,
                        MatroskaSpecification.Cluster => WebMReadResultKind.CompleteCluster,
                        _ => throw new InvalidOperationException($"Unexpected Ebml element type {container.Descriptor}")
                    };
                    return true;
                }
                if (_element.Descriptor.Type == EbmlElementType.MasterElement) {
                    var complex = _element.Descriptor;
                    if (ReadInternal()) {
                        if (CurrentDescriptor.ListEntry)
                            container.FillListEntry(_container.Descriptor, complex, _entry!);
                        else
                            container.FillComplex(_container.Descriptor, complex, _entry!);
                    }
                    else {
                        if (_spanReader.Position < _spanReader.Length)
                            _resume = true;
                        return false;
                    }
                }
                else if (CurrentDescriptor.ListEntry) {
                    if (container is Cluster cluster) {
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
                            _element = _container;
                            _spanReader.Position = beginPosition;
                            _readResultKind = WebMReadResultKind.BeginCluster;
                            _resume = true;
                            return true;
                        }
                    }

                    switch (CurrentDescriptor.Identifier.EncodedValue) {
                        case MatroskaSpecification.Block:
                            var block = new Block();
                            _entry = block;
                            block.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, block);
                            break;
                        case MatroskaSpecification.BlockAdditional:
                            var blockAdditional = new BlockAdditional();
                            _entry = blockAdditional;
                            blockAdditional.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockAdditional);
                            break;
                        case MatroskaSpecification.BlockVirtual:
                            var blockVirtual = new BlockVirtual();
                            _entry = blockVirtual;
                            blockVirtual.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockVirtual);
                            break;
                        case MatroskaSpecification.EncryptedBlock:
                            var encryptedBlock = new EncryptedBlock();
                            _entry = encryptedBlock;
                            encryptedBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, encryptedBlock);
                            break;
                        case MatroskaSpecification.SimpleBlock:
                            var simpleBlock = new SimpleBlock();
                            _entry = simpleBlock;
                            simpleBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, simpleBlock);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }

                    _readResultKind = WebMReadResultKind.Block;
                    _resume = true;
                    return true;
                }
                else
                    container.FillScalar(_container.Descriptor, CurrentDescriptor, (int)_element.Size, ref _spanReader);

                beginPosition = _spanReader.Position;
            }

            LeaveContainer();
            _entry = container;
            _readResultKind = container.Descriptor.Identifier.EncodedValue switch {
                MatroskaSpecification.EBML => WebMReadResultKind.Ebml,
                MatroskaSpecification.Segment => WebMReadResultKind.Segment,
                MatroskaSpecification.Cluster => WebMReadResultKind.CompleteCluster,
                _ => WebMReadResultKind.None
            };

            return true;
        }

        private bool ReadElement(int readUntil)
        {
            var identifier = _spanReader.ReadVInt(4);
            if (!identifier.HasValue)
                return false;

            var idValue = identifier.Value;
            if (idValue.IsReserved) throw new EbmlDataFormatException("invalid element identifier value");

            var isValid = MatroskaSpecification.ElementDescriptors.TryGetValue(idValue, out var elementDescriptor);
            if (!isValid)  throw new EbmlDataFormatException("invalid element identifier value - not white-listed");

            var size = _spanReader.ReadVInt(8);
            if (!size.HasValue)
                return false;

            var eof = Math.Min(readUntil, _spanReader.Length);
            var sizeValue = size.Value.Value;
            if (_spanReader.Position + (int)sizeValue > eof) {
                if (idValue.EncodedValue != MatroskaSpecification.Cluster &&
                    idValue.EncodedValue != MatroskaSpecification.Segment) {
                    _element = new EbmlElement(idValue, UnknownSize, elementDescriptor!);
                    return false;
                }
                if (sizeValue != UnknownSize) {
                    _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);
                    return false;
                }
            }

            _element = new EbmlElement(idValue, sizeValue, elementDescriptor!);

            return true;
        }

        private void EnterContainer()
        {
            if (_element.Type != EbmlElementType.None && _element.Type != EbmlElementType.MasterElement)
                throw new InvalidOperationException();
            if (_element.Type == EbmlElementType.MasterElement && _element.HasInvalidIdentifier)
                throw new InvalidOperationException();

            _containers.Push(_container);
            _container = _element;
            _element = EbmlElement.Empty;
        }

        private bool LeaveContainer()
        {
            if (_containers.Count == 0) throw new InvalidOperationException();

            _element = _container;
            _container = _containers.Pop();
            return true;
        }

        public readonly struct State
        {
            public readonly bool NotCompleted;
            public readonly int Position;
            public readonly int Remaining;
            internal readonly EbmlElement ContainerElement;
            public readonly BaseModel Container;

            public State(bool resume, int position, int remaining, EbmlElement containerElement, BaseModel container)
            {
                NotCompleted = resume;
                Position = position;
                Remaining = remaining;
                ContainerElement = containerElement;
                Container = container;
            }

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            public bool IsEmpty => Container == null;
        }
    }
}
