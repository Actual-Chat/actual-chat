// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;

namespace Markdig.Syntax.Inlines
{
    /// <summary>
    /// A Raw HTML (Section 6.8 CommonMark specs).
    /// </summary>
    /// <seealso cref="LeafInline" />
    [DebuggerDisplay("{Tag}")]
    public class HtmlInline : LeafInline
    {
        public HtmlInline(string tag)
        {
            Tag = tag;
        }

        /// <summary>
        /// Gets or sets the full declaration of this tag.
        /// </summary>
        public string Tag { get; set; }
    }
}