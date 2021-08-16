/* Copyright (c) 2011-2020 Oleg Zee

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 * */

namespace ActualChat.Audio.Ebml.Backup
{
    /// <summary>
    ///     Root class for Ebml type schema
    /// </summary>
    public class DTDBase
    {
        protected static ElementDescriptor Container(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.MasterElement, true);
        }

        protected static ElementDescriptor Binary(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.Binary, true);
        }

        protected static ElementDescriptor Uint(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.UnsignedInteger, false);
        }

        protected static ElementDescriptor Int(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.SignedInteger, false);
        }

        protected static ElementDescriptor Ascii(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.AsciiString, false);
        }

        protected static ElementDescriptor Utf8(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.Utf8String, false);
        }

        protected static ElementDescriptor Float(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.Float, false);
        }

        protected static ElementDescriptor Date(long id, string name = "")
        {
            return new ElementDescriptor(id, name, ElementType.Date, false);
        }

        public class MasterElementDescriptor : ElementDescriptor
        {
            protected MasterElementDescriptor(long identifier, string name = "") 
                : base(identifier, name, ElementType.MasterElement, false)
            {
            }
        }
    }

    /// <summary>
    ///     Appendix C. EBML Standard definitions.
    /// </summary>
    public class StandardDtd : DTDBase
    {
        private StandardDtd()
        {
        }

        public static readonly EBMLDesc
            EBML = new(0x1a45dfa3);

        public static readonly SignatureSlotDesc
            SignatureSlot = new(0x1b538667);

        public static readonly ElementDescriptor
            CRC32 = Container(0xc3).Named(nameof(CRC32)),
            Void = Binary(0xec).Named(nameof(Void));

        public sealed class EBMLDesc : MasterElementDescriptor
        {
            internal EBMLDesc(long id) : base(id, "EBML")
            {
            }

            public readonly ElementDescriptor
                EBMLVersion = Uint(0x4286, nameof(EBMLVersion)),
                EBMLReadVersion = Uint(0x42f7, nameof(EBMLReadVersion)),
                EBMLMaxIDLength = Uint(0x42f2, nameof(EBMLMaxIDLength)),
                EBMLMaxSizeLength = Uint(0x42f3, nameof(EBMLMaxSizeLength)),
                DocType = Ascii(0x4282, nameof(DocType)),
                DocTypeVersion = Uint(0x4287, nameof(DocTypeVersion)),
                DocTypeReadVersion = Uint(0x4285, nameof(DocTypeReadVersion));
        }

        public sealed class SignatureSlotDesc : MasterElementDescriptor
        {
            internal SignatureSlotDesc(long id) : base(id, "SignatureSlot")
            {
            }

            public readonly ElementDescriptor
                SignatureAlgo = Uint(0x7e8a, nameof(SignatureAlgo)),
                SignatureHash = Uint(0x7e9a, nameof(SignatureHash)),
                SignaturePublicKey = Binary(0x7ea5, nameof(SignaturePublicKey)),
                Signature = Binary(0x7eb5, nameof(Signature)),
                SignatureElements = Container(0x7e5b, nameof(SignatureElements)),
                SignatureElementList = Container(0x7e7b, nameof(SignatureElementList)),
                SignedElement = Binary(0x6532, nameof(SignedElement));
        }
    }
}