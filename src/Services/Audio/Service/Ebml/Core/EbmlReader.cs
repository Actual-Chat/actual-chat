using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using ActualChat.Audio.Ebml.Models;

namespace ActualChat.Audio.Ebml
{
    public ref struct EbmlReader
    {
        private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
        private readonly Stack<Element> _containers;
        
        private SpanReader _spanReader;

        private Element _container;
        private Element _element;
        private BaseModel _entry;


        public EbmlReader(ReadOnlySpan<byte> span) : this(span, (uint)span.Length)
        { }

        public EbmlReader(ReadOnlySpan<byte> span, uint maxBytesToRead)
        {
            _container = new Element(VInt.UnknownSize(2), maxBytesToRead, MatroskaSpecification.UnknownDescriptor);
            _element = Element.Empty;
            _spanReader = new SpanReader(span);
            _containers = new Stack<Element>(16);
            _entry = BaseModel.Empty;
        }

        public EbmlEntryType EbmlEntryType =>  _entry switch {
            null => EbmlEntryType.None,
            Cluster => EbmlEntryType.Cluster,
            Segment => EbmlEntryType.Segment,
            EBML => EbmlEntryType.EBML,
            _ => throw new InvalidOperationException()
        };

        public BaseModel Entry => _entry;

        public ElementDescriptor CurrentDescriptor => _element.Descriptor;

        public bool Read()
        {
            var hasElement = ReadElement(_spanReader.Length);
            if (!hasElement) return false;
            
            return _element.Type != ElementType.MasterElement || ReadInternal(_spanReader.Length);
        }

        private bool ReadInternal(int readUntil)
        {
            var isSuccess = true;
            EnterContainer();
            var container = _container.Descriptor.CreateInstance();
            var beginPosition = _spanReader.Position;
            var endPosition = _container.Size == UnknownSize 
                ? int.MaxValue 
                : beginPosition + (int)_container.Size;
            while (true) {
                if (endPosition <= _spanReader.Position)
                    break;

                var canRead = ReadElement(endPosition);
                if (!canRead) {
                    // isSuccess = false;
                    _entry = container;
                    _element = _container;
                    _spanReader.Position = beginPosition;
                    return false;
                }
                
                if (_element.Identifier.EncodedValue == MatroskaSpecification.Cluster) {
                    LeaveContainer();
                    _entry = container;
                    _element = _container;
                    _spanReader.Position = beginPosition;
                    return true;
                }
                if (_element.Descriptor.Type == ElementType.MasterElement) {
                    var complex = _element.Descriptor;
                    if (ReadInternal(endPosition)) {
                        if (CurrentDescriptor.ListEntry)
                            container.FillListEntry(_container.Descriptor, complex, _entry!);
                        else
                            container.FillComplex(_container.Descriptor, complex, _entry!);
                    }
                    else
                        return false;
                }
                else if (CurrentDescriptor.ListEntry)
                    switch (CurrentDescriptor.Identifier.EncodedValue) {
                        case MatroskaSpecification.Block:
                            var block = new Block();
                            block.Parse(_spanReader.ReadSpan((int)_element.Size));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, block);
                            break;
                        case MatroskaSpecification.BlockAdditional:
                            var blockAdditional = new BlockAdditional();
                            blockAdditional.Parse(_spanReader.ReadSpan((int)_element.Size));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockAdditional);
                            break;
                        case MatroskaSpecification.BlockVirtual:
                            var blockVirtual = new BlockVirtual();
                            blockVirtual.Parse(_spanReader.ReadSpan((int)_element.Size));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, blockVirtual);
                            break;
                        case MatroskaSpecification.EncryptedBlock:
                            var encryptedBlock = new EncryptedBlock();
                            encryptedBlock.Parse(_spanReader.ReadSpan((int)_element.Size));
                            container.FillListEntry(_container.Descriptor, CurrentDescriptor, encryptedBlock);
                            break;
                        case MatroskaSpecification.SimpleBlock:
                            var simpleBlock = new SimpleBlock();
                            simpleBlock.Parse(_spanReader.ReadSpan((int)_element.Size));
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

            return isSuccess;
        }
        
        
        private bool ReadElement(int readUntil)
        {
            var (idStatus,identifier) = _spanReader.ReadVInt(4);
            if (!idStatus)
                return false;
            
            if (identifier.IsReserved) throw new EbmlDataFormatException("invalid element identifier value");

            var isValid = MatroskaSpecification.ElementDescriptors.TryGetValue(identifier, out var elementDescriptor);
            if (!isValid)  throw new EbmlDataFormatException("invalid element identifier value - not white-listed");
            
            var (sizeStatus,size) = _spanReader.ReadVInt(8);
            if (!sizeStatus)
                return false;

            var eof = Math.Min(readUntil, _spanReader.Length);
            var sizeValue = size.Value;
            // TODO: decide whether we need to rollback position
            if (_spanReader.Position + (int)sizeValue > eof) {
                if (identifier.EncodedValue != MatroskaSpecification.Cluster &&
                    identifier.EncodedValue != MatroskaSpecification.Segment) {
                    _element = new Element(identifier, UnknownSize, elementDescriptor!);
                    return false;
                }
                if (sizeValue != UnknownSize) {
                    _element = new Element(identifier, sizeValue, elementDescriptor!);
                    return false;
                }
            }
            
            _element = new Element(identifier, sizeValue, elementDescriptor!);
         
            return true;
        }
        
        private void EnterContainer()
        {
            if (_element.Type != ElementType.None && _element.Type != ElementType.MasterElement)
                throw new InvalidOperationException();
            if (_element.Type == ElementType.MasterElement && _element.HasInvalidIdentifier)
                throw new InvalidOperationException();
            
            _containers.Push(_container);
            _container = _element;
            _element = Element.Empty;
        }
        
        private bool LeaveContainer()
        {
            if (_containers.Count == 0) throw new InvalidOperationException();
            
            _element = _container;
            _container = _containers.Pop();
            return true;
        }
        
        private struct Element
        {
            public static readonly Element Empty = new Element(VInt.UnknownSize(2), 0, MatroskaSpecification.UnknownDescriptor);

            public Element(VInt identifier, ulong sizeValue, ElementDescriptor descriptor)
            {
                Descriptor = descriptor;
                Identifier = identifier;
                Size = sizeValue;
            }

            public ElementDescriptor Descriptor { get; }

            public readonly VInt Identifier;

            public readonly ulong Size;

            public ElementType Type => Descriptor.Type;

            public bool IsEmpty => !Identifier.IsValidIdentifier && Size == 0 && Type == ElementType.None;

            public bool HasInvalidIdentifier => !Identifier.IsValidIdentifier;
        }
    }
}