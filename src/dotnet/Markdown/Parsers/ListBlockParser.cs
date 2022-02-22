// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Collections.Generic;
using Markdig.Helpers;
using Markdig.Syntax;

namespace Markdig.Parsers
{
    /// <summary>
    /// A parser for a list block and list item block.
    /// </summary>
    /// <seealso cref="BlockParser" />
    public class ListBlockParser : BlockParser
    {
        private CharacterMap<ListItemParser>? mapItemParsers;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListBlockParser"/> class.
        /// </summary>
        public ListBlockParser()
        {
            ItemParsers = new OrderedList<ListItemParser>()
            {
                new UnorderedListItemParser(),
                new NumberedListItemParser()
            };
        }

        /// <summary>
        /// Gets the parsers for items.
        /// </summary>
        public OrderedList<ListItemParser> ItemParsers { get; }

        public override void Initialize()
        {
            var tempMap = new Dictionary<char, ListItemParser>();

            foreach (var itemParser in ItemParsers)
            {
                if (itemParser.OpeningCharacters is null)
                {
                    ThrowHelper.InvalidOperationException($"The list item parser of type [{itemParser.GetType()}] cannot have OpeningCharacters to null. It must define a list of valid opening characters");
                }

                foreach (var openingCharacter in itemParser.OpeningCharacters)
                {
                    if (tempMap.ContainsKey(openingCharacter))
                    {
                        ThrowHelper.InvalidOperationException(
                            $"A list item parser with the same opening character `{openingCharacter}` is already registered");
                    }
                    tempMap.Add(openingCharacter, itemParser);
                }
            }
            mapItemParsers = new CharacterMap<ListItemParser>(tempMap);
        }

        public override BlockState TryOpen(BlockProcessor processor)
        {
            // When both a thematic break and a list item are possible
            // interpretations of a line, the thematic break takes precedence
            var thematicParser = ThematicBreakParser.Default;
            if (thematicParser.HasOpeningCharacter(processor.CurrentChar))
            {
                var result = thematicParser.TryOpen(processor);
                if (result.IsBreak())
                {
                    return result;
                }
            }

            return TryParseListItem(processor, null);
        }

        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            if (block is ListBlock && processor.NextContinue is ListItemBlock)
            {
                // We try to match only on item block if the ListBlock
                return BlockState.Skip;
            }

            // When both a thematic break and a list item are possible
            // interpretations of a line, the thematic break takes precedence
            BlockState result;
            var thematicParser = ThematicBreakParser.Default;
            if (!(processor.LastBlock is FencedCodeBlock) && thematicParser.HasOpeningCharacter(processor.CurrentChar))
            {
                result = thematicParser.TryOpen(processor);
                if (result.IsBreak())
                {
                    // TODO: We remove the thematic break, as it will be created later, but this is inefficient, try to find another way
                    var thematicBreak = processor.NewBlocks.Pop();
                    var linesBefore = thematicBreak.LinesBefore;
                    processor.LinesBefore = linesBefore;
                    return BlockState.None;
                }
            }

            // 5.2 List items
            // TODO: Check with specs, it is not clear that list marker or bullet marker must be followed by at least 1 space

            // If we have already a ListItemBlock, we are going to try to append to it
            result = BlockState.None;
            if (block is ListItemBlock listItem)
            {
                result = TryContinueListItem(processor, listItem);
            }

            if (result == BlockState.None)
            {
                result = TryParseListItem(processor, block);
            }

            return result;
        }

        private BlockState TryContinueListItem(BlockProcessor state, ListItemBlock listItem)
        {
            var list = (ListBlock)listItem.Parent!;

            // Allow all blanks lines if the last block is a fenced code block
            // Allow 1 blank line inside a list
            // If > 1 blank line, terminate this list
            if (state.IsBlankLine)
            {
                if (state.CurrentBlock != null && state.CurrentBlock.IsBreakable)
                {
                    if (!(state.NextContinue is ListBlock))
                    {
                        list.CountAllBlankLines++;
                        if (!state.TrackTrivia)
                        {
                            listItem.Add(new BlankLineBlock());
                        }
                    }
                    list.CountBlankLinesReset++;
                }

                if (list.CountBlankLinesReset == 1 && listItem.ColumnWidth < 0)
                {
                    state.Close(listItem);

                    // Leave the list open
                    list.IsOpen = true;
                    return BlockState.Continue;
                }

                // Update list-item source end position
                listItem.UpdateSpanEnd(state.Line.End);

                return BlockState.Continue;
            }

            list.CountBlankLinesReset = 0;

            int columnWidth = listItem.ColumnWidth;
            if (columnWidth < 0)
            {
                columnWidth = -columnWidth;
            }

            if (state.Indent >= columnWidth)
            {
                if (state.Indent > columnWidth && state.IsCodeIndent)
                {
                    state.GoToColumn(state.ColumnBeforeIndent + columnWidth);
                }

                // Update list-item source end position
                listItem.UpdateSpanEnd(state.Line.End);
                listItem.NewLine = state.Line.NewLine;

                return BlockState.Continue;
            }

            return BlockState.None;
        }

        private BlockState TryParseListItem(BlockProcessor state, Block? block)
        {
            var currentListItem = block as ListItemBlock;
            var currentParent = block as ListBlock ?? (ListBlock)currentListItem?.Parent!;

            // We can early exit if we have a code indent and we are either (1) not in a ListItem, (2) preceded by a blank line, (3) in an unordered list
            if (state.IsCodeIndent && (currentListItem is null || currentListItem.LastChild is BlankLineBlock || !currentParent.IsOrdered))
            {
                return BlockState.None;
            }

            var initColumnBeforeIndent = state.ColumnBeforeIndent;
            var initColumn = state.Column;
            var sourcePosition = state.Start;
            var sourceEndPosition = state.Line.End;

            var c = state.CurrentChar;
            var itemParser = mapItemParsers![c];
            if (itemParser is null)
            {
                return BlockState.None;
            }

            // Try to parse the list item
            if (!itemParser.TryParse(state, currentParent?.BulletType ?? '\0', out ListInfo listInfo))
            {
                // Reset to an a start position
                state.GoToColumn(initColumn);
                return BlockState.None;
            }
            var savedTriviaStart = state.TriviaStart;
            var triviaBefore = state.UseTrivia(sourcePosition - 1);

            // set trivia to the mandatory whitespace after the bullet
            state.TriviaStart = state.Start;

            bool isOrdered = itemParser is OrderedListItemParser;

            // Gets the current character after a successful parsing of the list information
            c = state.CurrentChar;

            // Item starting with a blank line
            int columnWidth;

            // Do we have a blank line right after the bullet?
            if (c == '\0')
            {
                // Use a negative number to store the number of expected chars
                columnWidth = -(state.Column - initColumnBeforeIndent + 1);
            }
            else
            {
                if (!c.IsSpaceOrTab())
                {
                    state.GoToColumn(initColumn);
                    state.TriviaStart = savedTriviaStart; // restore changed TriviaStart state
                    return BlockState.None;
                }

                // Parse the following indent
                state.RestartIndent();
                var columnBeforeIndent = state.Column;
                state.ParseIndent();

                // We expect at most 4 columns after
                // If we have more, we reset the position
                if (state.Indent > 4)
                {
                    state.GoToColumn(columnBeforeIndent + 1);
                }

                // Number of spaces required for the following content to be part of this list item
                // If the list item starts with a blank line, the number of spaces
                // following the list marker doesn't change the required indentation
                columnWidth = (state.IsBlankLine ? columnBeforeIndent : state.Column) - initColumnBeforeIndent;
            }

            // Starts/continue the list unless:
            // - an empty list item follows a paragraph
            // - an ordered list is not starting by '1'
            if ((block ?? state.LastBlock) is ParagraphBlock previousParagraph)
            {
                if (state.IsBlankLine ||
                    state.IsOpen(previousParagraph) && listInfo.BulletType == '1' && listInfo.OrderedStart is not "1")
                {
                    state.GoToColumn(initColumn);
                    state.TriviaStart = savedTriviaStart; // restore changed TriviaStart state
                    return BlockState.None;
                }
            }

            int.TryParse(listInfo.OrderedStart, out int order);
            var newListItem = new ListItemBlock(this)
            {
                Column = initColumn,
                ColumnWidth = columnWidth,
                Order = order,
                SourceBullet = listInfo.SourceBullet,
                TriviaBefore = triviaBefore,
                Span = new SourceSpan(sourcePosition, sourceEndPosition),
                LinesBefore = state.UseLinesBefore(),
                NewLine = state.Line.NewLine,
            };
            state.NewBlocks.Push(newListItem);

            if (currentParent != null)
            {
                // If we have a new list item, close the previous one
                if (currentListItem != null)
                {
                    state.Close(currentListItem);
                }

                // Reset the list if it is a new list or a new type of bullet
                if (currentParent.IsOrdered != isOrdered ||
                    currentParent.OrderedDelimiter != listInfo.OrderedDelimiter ||
                    currentParent.BulletType != listInfo.BulletType)
                {
                    state.Close(currentParent);
                    currentParent = null;
                }
            }

            if (currentParent is null)
            {
                var newList = new ListBlock(this)
                {
                    Column = initColumn,
                    Span = new SourceSpan(sourcePosition, sourceEndPosition),
                    IsOrdered = isOrdered,
                    BulletType = listInfo.BulletType,
                    OrderedDelimiter = listInfo.OrderedDelimiter,
                    DefaultOrderedStart = listInfo.DefaultOrderedStart,
                    OrderedStart = listInfo.OrderedStart,
                    LinesBefore = state.UseLinesBefore(),
                };
                state.NewBlocks.Push(newList);
            }
            return BlockState.Continue;
        }

        public override bool Close(BlockProcessor processor, Block blockToClose)
        {
            if (processor.TrackTrivia)
            {
                return true;
            }

            // Process only if we have blank lines
            if (blockToClose is ListBlock listBlock && listBlock.CountAllBlankLines > 0)
            {
                if (listBlock.Parent is ListItemBlock parentListItemBlock &&
                    listBlock.LastChild is ListItemBlock lastListItem &&
                    lastListItem.LastChild is BlankLineBlock)
                {
                    // Inform the outer list that we have a blank line
                    var parentList = (ListBlock)parentListItemBlock.Parent!;

                    parentList.CountAllBlankLines++;
                    parentListItemBlock.Add(new BlankLineBlock());
                }

                for (int listIndex = listBlock.Count - 1; listIndex >= 0; listIndex--)
                {
                    var listItem = (ListItemBlock)listBlock[listIndex];

                    for (int i = listItem.Count - 1; i >= 0; i--)
                    {
                        if (listItem[i] is BlankLineBlock)
                        {
                            if (i == listItem.Count - 1 ? listIndex < listBlock.Count - 1 : i > 0)
                            {
                                listBlock.IsLoose = true;
                            }

                            listItem.RemoveAt(i);

                            //If we have removed all blank lines, we can exit
                            listBlock.CountAllBlankLines--;
                            if (listBlock.CountAllBlankLines == 0)
                            {
                                goto done;
                            }
                        }
                    }
                }
            }

            done:
            return true;
        }
    }
}