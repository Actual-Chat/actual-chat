// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Markdig.Helpers;
using Markdig.Syntax;

namespace Markdig.Parsers
{
    /// <summary>
    /// Block parser for a <see cref="HeadingBlock"/>.
    /// </summary>
    /// <seealso cref="BlockParser" />
    public class HeadingBlockParser : BlockParser, IAttributesParseable
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="HeadingBlockParser"/> class.
        /// </summary>
        public HeadingBlockParser()
        {
            OpeningCharacters = new[] {'#'};
        }

        /// <summary>
        /// Gets or sets the max count of the leading unescaped # characters
        /// </summary>
        public int MaxLeadingCount { get; set; } = 6;

        /// <summary>
        /// A delegates that allows to process attached attributes after #
        /// </summary>
        public TryParseAttributesDelegate? TryParseAttributes { get; set; }

        public override BlockState TryOpen(BlockProcessor processor)
        {
            // If we are in a CodeIndent, early exit
            if (processor.IsCodeIndent)
            {
                return BlockState.None;
            }

            // 4.2 ATX headings
            // An ATX heading consists of a string of characters, parsed as inline content, 
            // between an opening sequence of 1–6(configurable) unescaped # characters and an optional 
            // closing sequence of any number of unescaped # characters. The opening sequence 
            // of # characters must be followed by a space or by the end of line. The optional
            // closing sequence of #s must be preceded by a space and may be followed by spaces
            // only. The opening # character may be indented 0-3 spaces. The raw contents of 
            // the heading are stripped of leading and trailing spaces before being parsed as 
            // inline content. The heading level is equal to the number of # characters in the 
            // opening sequence.
            var column = processor.Column;
            var line = processor.Line;
            var sourcePosition = line.Start;
            var c = line.CurrentChar;
            var matchingChar = c;

            Debug.Assert(MaxLeadingCount > 0);
            int leadingCount = 0;
            while (c != '\0' && leadingCount <= MaxLeadingCount)
            {
                if (c != matchingChar)
                {
                    break;
                }
                c = processor.NextChar();
                leadingCount++;
            }

            // A space is required after leading #
            if (leadingCount > 0 && leadingCount <= MaxLeadingCount && (c.IsSpaceOrTab() || c == '\0'))
            {
                StringSlice trivia = StringSlice.Empty;
                if (processor.TrackTrivia && c.IsSpaceOrTab())
                {
                    trivia = new StringSlice(processor.Line.Text, processor.Start, processor.Start);
                    processor.NextChar();
                }
                // Move to the content
                var headingBlock = new HeadingBlock(this)
                {
                    HeaderChar = matchingChar,
                    TriviaAfterAtxHeaderChar = trivia,
                    Level = leadingCount,
                    Column = column,
                    Span = { Start = sourcePosition },
                    TriviaBefore = processor.UseTrivia(sourcePosition - 1),
                    LinesBefore = processor.UseLinesBefore(),
                    NewLine = processor.Line.NewLine,
                };
                processor.NewBlocks.Push(headingBlock);
                if (!processor.TrackTrivia)
                {
                    processor.GoToColumn(column + leadingCount + 1);
                }

                // Gives a chance to parse attributes
                TryParseAttributes?.Invoke(processor, ref processor.Line, headingBlock);

                // The optional closing sequence of #s must be preceded by a space and may be followed by spaces only.
                int endState = 0;
                int countClosingTags = 0;
                int sourceEnd = processor.Line.End;
                for (int i = processor.Line.End; i >= processor.Line.Start - 1; i--)  // Go up to Start - 1 in order to match the space after the first ###
                {
                    c = processor.Line.Text[i];
                    if (endState == 0)
                    {
                        if (c.IsSpaceOrTab())
                        {
                            continue;
                        }
                        endState = 1;
                    }
                    if (endState == 1)
                    {
                        if (c == matchingChar)
                        {
                            countClosingTags++;
                            continue;
                        }

                        if (countClosingTags > 0)
                        {
                            if (c.IsSpaceOrTab())
                            {
                                processor.Line.End = i - 1;
                            }
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Setup the source end position of this element
                headingBlock.Span.End = processor.Line.End;

                if (processor.TrackTrivia)
                {
                    var wsa = new StringSlice(processor.Line.Text, processor.Line.End + 1, sourceEnd);
                    headingBlock.TriviaAfter = wsa;
                    if (wsa.Overlaps(headingBlock.TriviaAfterAtxHeaderChar))
                    {
                        // prevent double whitespace allocation in case of closing # i.e. "# #"
                        headingBlock.TriviaAfterAtxHeaderChar = StringSlice.Empty;
                    }
                }

                // We expect a single line, so don't continue
                return BlockState.Break;
            }

            // Else we don't have an header
            processor.Line.Start = sourcePosition;
            processor.Column = column;
            return BlockState.None;
        }

        public override bool Close(BlockProcessor processor, Block block)
        {
            if (!processor.TrackTrivia)
            {
                var heading = (HeadingBlock)block;
                heading.Lines.Trim();
            }
            return true;
        }
    }
}