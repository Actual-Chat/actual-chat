// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using Markdig.Helpers;
using Markdig.Syntax;

namespace Markdig.Parsers
{
    /// <summary>
    /// Delegates called when processing a document
    /// </summary>
    /// <param name="document">The markdown document.</param>
    public delegate void ProcessDocumentDelegate(MarkdownDocument document);

    /// <summary>
    /// The Markdown parser.
    /// </summary>
    public static class MarkdownParser
    {
        /// <summary>
        /// Parses the specified markdown into an AST <see cref="MarkdownDocument"/>
        /// </summary>
        /// <param name="text">A Markdown text</param>
        /// <param name="pipeline">The pipeline used for the parsing.</param>
        /// <param name="context">A parser context used for the parsing.</param>
        /// <returns>An AST Markdown document</returns>
        /// <exception cref="ArgumentNullException">if reader variable is null</exception>
        public static MarkdownDocument Parse(string text, MarkdownPipeline? pipeline = null, MarkdownParserContext? context = null)
        {
            if (text is null) ThrowHelper.ArgumentNullException_text();

            pipeline ??= Markdown.DefaultPipeline;

            text = FixupZero(text);

            var document = new MarkdownDocument
            {
                IsOpen = true
            };

            if (pipeline.PreciseSourceLocation)
            {
                int roughLineCountEstimate = text.Length / 40;
                roughLineCountEstimate = Math.Min(4, Math.Max(512, roughLineCountEstimate));
                document.LineStartIndexes = new List<int>(roughLineCountEstimate);
            }

            var blockProcessor = BlockProcessor.Rent(document, pipeline.BlockParsers, context, pipeline.TrackTrivia);
            try
            {
                blockProcessor.Open(document);

                ProcessBlocks(blockProcessor, new LineReader(text));

                if (pipeline.TrackTrivia)
                {
                    Block? lastBlock = blockProcessor.LastBlock;
                    if (lastBlock is null && document.Count == 0)
                    {
                        // this means we have unassigned characters
                        var noBlocksFoundBlock = new EmptyBlock(null);
                        List<StringSlice> linesBefore = blockProcessor.UseLinesBefore();
                        noBlocksFoundBlock.LinesAfter = new List<StringSlice>();
                        noBlocksFoundBlock.LinesAfter.AddRange(linesBefore);
                        document.Add(noBlocksFoundBlock);
                    }
                    else if (lastBlock != null && blockProcessor.LinesBefore != null)
                    {
                        // this means we're out of lines, but still have unassigned empty lines.
                        // thus, we'll assign the empty unsassigned lines to the last block
                        // of the document.
                        var rootMostContainerBlock = Block.FindRootMostContainerParent(lastBlock);
                        rootMostContainerBlock.LinesAfter ??= new List<StringSlice>();
                        var linesBefore = blockProcessor.UseLinesBefore();
                        rootMostContainerBlock.LinesAfter.AddRange(linesBefore);
                    }
                }

                // At this point the LineIndex is the same as the number of lines in the document
                document.LineCount = blockProcessor.LineIndex;
            }
            finally
            {
                BlockProcessor.Release(blockProcessor);
            }

            var inlineProcessor = InlineProcessor.Rent(document, pipeline.InlineParsers, pipeline.PreciseSourceLocation, context, pipeline.TrackTrivia);
            inlineProcessor.DebugLog = pipeline.DebugLog;
            try
            {
                ProcessInlines(inlineProcessor, document);
            }
            finally
            {
                InlineProcessor.Release(inlineProcessor);
            }

            // Allow to call a hook after processing a document
            pipeline.DocumentProcessed?.Invoke(document);

            return document;
        }

        /// <summary>
        /// Fixups the zero character by replacing it to a secure character (Section 2.3 Insecure characters, CommonMark specs)
        /// </summary>
        /// <param name="text">The text to secure.</param>
        private static string FixupZero(string text)
        {
            return text.Replace('\0', CharHelper.ReplacementChar);
        }

        private static void ProcessBlocks(BlockProcessor blockProcessor, LineReader lineReader)
        {
            while (true)
            {
                // Get the precise position of the begining of the line
                var lineText = lineReader.ReadLine();

                // If this is the end of file and the last line is empty
                if (lineText.Text is null)
                {
                    break;
                }

                blockProcessor.ProcessLine(lineText);
            }
            blockProcessor.CloseAll(true);
        }

        private static void ProcessInlines(InlineProcessor inlineProcessor, MarkdownDocument document)
        {
            // "stackless" processor
            int blockCount = 1;
            var blocks = new ContainerItem[4];

            blocks[0] = new ContainerItem(document);
            document.OnProcessInlinesBegin(inlineProcessor);

            while (blockCount != 0)
            {
                process_new_block:
                ref ContainerItem item = ref blocks[blockCount - 1];
                var container = item.Container;

                for (; item.Index < container.Count; item.Index++)
                {
                    var block = container[item.Index];
                    if (block is LeafBlock leafBlock)
                    {
                        leafBlock.OnProcessInlinesBegin(inlineProcessor);
                        if (leafBlock.ProcessInlines)
                        {
                            inlineProcessor.ProcessInlineLeaf(leafBlock);
                            if (leafBlock.RemoveAfterProcessInlines)
                            {
                                container.RemoveAt(item.Index);
                                item.Index--;
                            }
                            else if (inlineProcessor.BlockNew != null)
                            {
                                container[item.Index] = inlineProcessor.BlockNew;
                            }
                        }
                        leafBlock.OnProcessInlinesEnd(inlineProcessor);
                    }
                    else if (block is ContainerBlock newContainer)
                    {
                        // If we need to remove it
                        if (newContainer.RemoveAfterProcessInlines)
                        {
                            container.RemoveAt(item.Index);
                        }
                        else
                        {
                            // Else we have processed it
                            item.Index++;
                        }

                        if (blockCount == blocks.Length)
                        {
                            Array.Resize(ref blocks, blockCount * 2);
                            ThrowHelper.CheckDepthLimit(blocks.Length);
                        }
                        blocks[blockCount++] = new ContainerItem(newContainer);
                        newContainer.OnProcessInlinesBegin(inlineProcessor);
                        goto process_new_block;
                    }
                }
                container.OnProcessInlinesEnd(inlineProcessor);
                blocks[--blockCount] = default;
            }
        }

        private struct ContainerItem
        {
            public ContainerItem(ContainerBlock container)
            {
                Container = container;
                Index = 0;
            }

            public readonly ContainerBlock Container;

            public int Index;
        }
    }
}