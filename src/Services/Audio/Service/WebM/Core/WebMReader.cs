using System;
using System.Collections.Generic;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    public ref struct WebMReader
    {
        private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
        private readonly Stack<EbmlElement> _containers;
        
        private SpanReader _spanReader;

        private EbmlElement _container;
        private EbmlElement _element;
        private BaseModel _entry;
        private bool _resume;


        public WebMReader(ReadOnlySpan<byte> span) : this(span, (uint)span.Length)
        { }

        public WebMReader(ReadOnlySpan<byte> span, uint maxBytesToRead)
        {
            _container = new EbmlElement(VInt.UnknownSize(2), maxBytesToRead, MatroskaSpecification.UnknownDescriptor);
            _element = EbmlElement.Empty;
            _spanReader = new SpanReader(span);
            _containers = new Stack<EbmlElement>(16);
            _entry = BaseModel.Empty;
            _resume = false;
        }
        
        private WebMReader(State state, ReadOnlySpan<byte> span)
        {
            _container = state.ContainerElement;
            _element = EbmlElement.Empty;
            _spanReader = new SpanReader(span);
            _containers = new Stack<EbmlElement>(16);
            _entry = state.Container;
            _resume = state.NotCompleted;
        }

        public EbmlEntryType EbmlEntryType =>  _entry switch {
            null => EbmlEntryType.None,
            Cluster => EbmlEntryType.Cluster,
            Segment => EbmlEntryType.Segment,
            EBML => EbmlEntryType.Ebml,
            _ => throw new InvalidOperationException()
        };

        public static WebMReader FromState(State state)
        {
            return new WebMReader(state, ReadOnlySpan<byte>.Empty);
        }

        public RootEntry Entry => _entry switch {
            Cluster cluster => cluster,
            EBML ebml => ebml,
            Segment segment => segment,
            _ => throw new InvalidOperationException($"Entry should be of type {nameof(RootEntry)}, but currently it is {_entry.GetType().Name}")
        };

        public EbmlElementDescriptor CurrentDescriptor => _element.Descriptor;

        public ReadOnlySpan<byte> Tail => _spanReader.Tail;

        public State GetState()
        {
            var remaining = _spanReader.Length - _spanReader.Position;
            return new State(_resume, _spanReader.Position, remaining, _container, _entry);
        }
        
        public WebMReader WithNewSource(ReadOnlySpan<byte> span)
        {
            return new WebMReader(GetState(), span);
        }

        public bool Read()
        {
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
            
            return _element.Type != EbmlElementType.MasterElement || ReadInternal();
        }

        private bool ReadInternal(bool resume = false)
        {
            BaseModel container;
            if (resume)
                container = _entry;
            else {
                EnterContainer();
                container = _container.Descriptor.CreateInstance();
            }

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
                    return false;
                }
                
                if (_element.Identifier.EncodedValue == MatroskaSpecification.Cluster) {
                    LeaveContainer();
                    _entry = container;
                    _element = _container;
                    _spanReader.Position = beginPosition;
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
                else if (CurrentDescriptor.ListEntry)
                    switch (CurrentDescriptor.Identifier.EncodedValue) {
                        case MatroskaSpecification.Block:
                            var block = new Block();
                            block.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, block);
                            break;
                        case MatroskaSpecification.BlockAdditional:
                            var blockAdditional = new BlockAdditional();
                            blockAdditional.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockAdditional);
                            break;
                        case MatroskaSpecification.BlockVirtual:
                            var blockVirtual = new BlockVirtual();
                            blockVirtual.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockVirtual);
                            break;
                        case MatroskaSpecification.EncryptedBlock:
                            var encryptedBlock = new EncryptedBlock();
                            encryptedBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, encryptedBlock);
                            break;
                        case MatroskaSpecification.SimpleBlock:
                            var simpleBlock = new SimpleBlock();
                            simpleBlock.Parse(_spanReader.ReadSpan((int)_element.Size, out _));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, simpleBlock);
                            break;
                        default:
                            throw new InvalidOperationException();

                    }
                else
                    container.FillScalar(_container.Descriptor, CurrentDescriptor, (int)_element.Size, ref _spanReader);

                beginPosition = _spanReader.Position;
            }

            LeaveContainer();
            _entry = container;

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

            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            public bool IsEmpty => Container == null;

            public State(bool resume, int position, int remaining, EbmlElement containerElement, BaseModel container)
            {
                NotCompleted = resume;
                Position = position;
                Remaining = remaining;
                ContainerElement = containerElement;
                Container = container;
            }
        }
    }
}