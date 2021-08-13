using System;
using System.Collections.Generic;
using System.IO;
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
        private EbmlEntryType _entryType;
        

        public EbmlReader(ReadOnlySpan<byte> span) : this(span, (uint)span.Length)
        { }

        public EbmlReader(ReadOnlySpan<byte> span, uint maxBytesToRead)
        {
            _entryType = EbmlEntryType.None;
            _container = new Element(VInt.UnknownSize(2), maxBytesToRead, MatroskaSpecification.UnknownDescriptor);
            _element = Element.Empty;
            _spanReader = new SpanReader(span);
            _containers = new Stack<Element>(16);
        }

        public EbmlEntryType EbmlEntryType => _entryType;

        public ElementDescriptor CurrentDescriptor => _element.Descriptor;

        public bool Read()
        {
            if (_element.Remaining == UnknownSize) {
                
            }
            else {
                var skipped = Skip(_element.Remaining);
                if (!skipped) return false;

                _container.Remaining -= _element.Size;
                _element = _container;

                if (_element.Remaining < 1) {
                    _element = Element.Empty;
                    return false;
                }
            }

            var hasElement = ReadElement();
            if (!hasElement) return false;
            
            if (_element.Type != ElementType.MasterElement) return hasElement;
            
            EnterContainer();
            while (Read()) {
                        
            }
            LeaveContainer();

            return true;
        }
        
        private bool Skip(ulong length)
        {
            // sanity check
            if (length >= int.MaxValue)
                return false;
            
            if (!_element.IsEmpty && _element.Remaining < length) 
                return false;

            var intLength = (int)length;
            if (!_spanReader.Skip(intLength))
                return false;

            _element.Remaining -= (uint)intLength;

            return true;
        }
        
        private bool ReadElement()
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

            var sizeValue = size.Value;
            if (sizeValue > _container.Remaining) {
                if (identifier.EncodedValue != MatroskaSpecification.Cluster && identifier.EncodedValue != MatroskaSpecification.Segment) {
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
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || (_element.Type != ElementType.None && _element.Type != ElementType.MasterElement)) 
                throw new InvalidOperationException();
            
            _containers.Push(_container);
            _container = _element;
            _element = Element.Empty;
        }
        
        private bool LeaveContainer()
        {
            if (_containers.Count == 0) throw new InvalidOperationException();
            
            _container.Remaining -= _element.Size;
            _element = _container;
            _container = _containers.Pop();
            return Skip(_element.Remaining);
        }
        
        private struct Element
        {
            public static readonly Element Empty = new Element(VInt.UnknownSize(2), 0, MatroskaSpecification.UnknownDescriptor);

            public Element(VInt identifier, ulong sizeValue, ElementDescriptor descriptor)
            {
                Descriptor = descriptor;
                Identifier = identifier;
                Size = sizeValue;
                Remaining = sizeValue;
            }

            public ElementDescriptor Descriptor { get; }

            public readonly VInt Identifier;

            public readonly ulong Size;

            public ulong Remaining;

            public ElementType Type => Descriptor.Type;

            public bool IsEmpty => !Identifier.IsValidIdentifier && Size == 0 && Type == ElementType.None;

            public bool HasInvalidIdentifier => !Identifier.IsValidIdentifier;
        }
    }
}