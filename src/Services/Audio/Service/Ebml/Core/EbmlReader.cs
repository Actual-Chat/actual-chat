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
        private BaseModel? _entry;


        public EbmlReader(ReadOnlySpan<byte> span) : this(span, (uint)span.Length)
        { }

        public EbmlReader(ReadOnlySpan<byte> span, uint maxBytesToRead)
        {
            _container = new Element(VInt.UnknownSize(2), maxBytesToRead, MatroskaSpecification.UnknownDescriptor);
            _element = Element.Empty;
            _spanReader = new SpanReader(span);
            _containers = new Stack<Element>(16);
            _entry = null;
        }

        public EbmlEntryType EbmlEntryType =>  _entry switch {
            null => EbmlEntryType.None,
            Cluster => EbmlEntryType.Cluster,
            Segment => EbmlEntryType.Segment,
            EBML => EbmlEntryType.EBML,
            _ => throw new InvalidOperationException()
        };

        public BaseModel? Entry => _entry;

        public ElementDescriptor CurrentDescriptor => _element.Descriptor;

        public bool Read(int readUntil)
        {
            if (_container.Remaining == UnknownSize) {
                
            }
            // else {
            //     var skipped = Skip(_element.Remaining);
            //     if (!skipped) return false;
            //
            //     _container.Remaining -= _element.Size;
            //     _element = _container;
            //
            //     if (_element.Remaining < 1) {
            //         _element = Element.Empty;
            //         return false;
            //     }
            // }

            // var hasElement = ReadElement(containerSize);
            // if (!hasElement) return false;
            //
            // if (_element.Type != ElementType.MasterElement) return hasElement;
            
            EnterContainer();
            var container = _container.Descriptor.CreateInstance();
            var endPosition = _spanReader.Position + (int)_container.Size;
            while (ReadElement(endPosition))
                // if (_element.Descriptor.Type == ElementType.MasterElement) {
                //     if (Read(endPosition)) {
                //         if (CurrentDescriptor.ListEntry)
                //             ((Cluster)container).FillListEntry(CurrentDescriptor, _entry!);
                //         else
                //             ((EBML)container).FillComplex(CurrentDescriptor, _entry!);
                //     }
                //     else
                //         return false;
                // }
                // else if (CurrentDescriptor.ListEntry)
                //     switch (CurrentDescriptor.Identifier.EncodedValue) {
                //         case MatroskaSpecification.Block:
                //             var block = new Block();
                //             block.Parse(_spanReader.Tail[..(int)_element.Size]);
                //             ((Cluster)container).FillBlockEntry(CurrentDescriptor, block);
                //             break;
                //         case MatroskaSpecification.BlockAdditional:
                //             var blockAdditional = new BlockAdditional();
                //             blockAdditional.Parse(_spanReader.Tail[..(int)_element.Size]);
                //             ((Cluster)container).FillBlockEntry(CurrentDescriptor, blockAdditional);
                //             break;
                //         case MatroskaSpecification.BlockVirtual:
                //             var blockVirtual = new BlockVirtual();
                //             blockVirtual.Parse(_spanReader.Tail[..(int)_element.Size]);
                //             ((Cluster)container).FillBlockEntry(CurrentDescriptor, blockVirtual);
                //             break;
                //         case MatroskaSpecification.EncryptedBlock:
                //             var encryptedBlock = new EncryptedBlock();
                //             encryptedBlock.Parse(_spanReader.Tail[..(int)_element.Size]);
                //             ((Cluster)container).FillBlockEntry(CurrentDescriptor, encryptedBlock);
                //             break;
                //         case MatroskaSpecification.SimpleBlock:
                //             var simpleBlock = new EncryptedBlock();
                //             simpleBlock.Parse(_spanReader.Tail[..(int)_element.Size]);
                //             ((Cluster)container).FillBlockEntry(CurrentDescriptor, simpleBlock);
                //             break;
                //         default:
                //             throw new InvalidOperationException();
                //         
                //     }
                // else
                //     ((EBML)container).FillScalar(CurrentDescriptor, (int)_element.Size, _spanReader);

            LeaveContainer();
            _entry = container;

            return true;
        }
        
        // private bool Skip(ulong length)
        // {
        //     // sanity check
        //     if (length >= int.MaxValue)
        //         return false;
        //     
        //     if (!_element.IsEmpty && _element.Remaining < length) 
        //         return false;    
        //
        //     var intLength = (int)length;
        //     if (!_spanReader.Skip(intLength))
        //         return false;
        //
        //     _element.Remaining -= (uint)intLength;
        //
        //     return true;
        // }
        
        private bool ReadElement(int containerSize)
        {
            var (idStatus,identifier) = _spanReader.ReadVInt(4);
            if (!idStatus)
                return false;
            
            // if (_container.Remaining < identifier.Length)
            //     return false;
            //
            // _container.Remaining -= identifier.Length;

            if (identifier.IsReserved) throw new EbmlDataFormatException("invalid element identifier value");

            var isValid = MatroskaSpecification.ElementDescriptors.TryGetValue(identifier, out var elementDescriptor);
            if (!isValid)  throw new EbmlDataFormatException("invalid element identifier value - not white-listed");
            
            var (sizeStatus,size) = _spanReader.ReadVInt(8);
            if (!sizeStatus)
                return false;
            // if (_element.Remaining < size.Length)
            //     return false;
            //
            // _element.Remaining -= size.Length;
            
            var sizeValue = size.Value;
            // if (sizeValue > _container.Remaining) {
            //     if (identifier.EncodedValue != MatroskaSpecification.Cluster && identifier.EncodedValue != MatroskaSpecification.Segment) {
            //         _element = new Element(identifier, UnknownSize, elementDescriptor!);
            //         return false;
            //     }
            //
            //     if (sizeValue != UnknownSize) {
            //         _element = new Element(identifier, sizeValue, elementDescriptor!);
            //         return false;
            //     }
            // }
            
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
            // return Skip(_element.Remaining);
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