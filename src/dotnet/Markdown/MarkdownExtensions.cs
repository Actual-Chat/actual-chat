// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using Markdig.Extensions.AutoLinks;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Helpers;

namespace Markdig
{
    /// <summary>
    /// Provides extension methods for <see cref="MarkdownPipelineBuilder"/> to enable several Markdown extensions.
    /// </summary>
    public static class MarkdownExtensions
    {
        /// <summary>
        /// Adds the specified extension to the extensions collection.
        /// </summary>
        /// <typeparam name="TExtension">The type of the extension.</typeparam>
        /// <returns>The instance of <see cref="MarkdownPipelineBuilder" /></returns>
        public static MarkdownPipelineBuilder Use<TExtension>(this MarkdownPipelineBuilder pipeline) where TExtension : class, IMarkdownExtension, new()
        {
            pipeline.Extensions.AddIfNotAlready<TExtension>();
            return pipeline;
        }

        /// <summary>
        /// Adds the specified extension instance to the extensions collection.
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <param name="extension">The instance of the extension to be added.</param>
        /// <typeparam name="TExtension">The type of the extension.</typeparam>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder Use<TExtension>(this MarkdownPipelineBuilder pipeline, TExtension extension) where TExtension : class, IMarkdownExtension
        {
            pipeline.Extensions.AddIfNotAlready(extension);
            return pipeline;
        }

        /// <summary>
        /// Uses this extension to enable autolinks from text `http://`, `https://`, `ftp://`, `mailto:`, `www.xxx.yyy`
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <param name="options">The options.</param>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder UseAutoLinks(this MarkdownPipelineBuilder pipeline, AutoLinkOptions? options = null)
        {
            pipeline.Extensions.ReplaceOrAdd<AutoLinkExtension>(new AutoLinkExtension(options));
            return pipeline;
        }

        /// <summary>
        /// Uses precise source code location (useful for syntax highlighting).
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder UsePreciseSourceLocation(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.PreciseSourceLocation = true;
            return pipeline;
        }

        /// <summary>
        /// This will disable the HTML support in the markdown processor (for constraint/safe parsing).
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder DisableHtml(this MarkdownPipelineBuilder pipeline)
        {
            var parser = pipeline.BlockParsers.Find<HtmlBlockParser>();
            if (parser != null)
            {
                pipeline.BlockParsers.Remove(parser);
            }

            var inlineParser = pipeline.InlineParsers.Find<AutolinkInlineParser>();
            if (inlineParser != null)
            {
                inlineParser.EnableHtmlParsing = false;
            }
            return pipeline;
        }

        /// <summary>
        /// Configures the pipeline using a string that defines the extensions to activate.
        /// </summary>
        /// <param name="pipeline">The pipeline (e.g: advanced for <see cref="UseAdvancedExtensions"/>, pipetables+gridtables for <see cref="UsePipeTables"/> and <see cref="UseGridTables"/></param>
        /// <param name="extensions">The extensions to activate as a string</param>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder Configure(this MarkdownPipelineBuilder pipeline, string? extensions)
        {
            if (extensions is null)
            {
                return pipeline;
            }

            // TODO: the extension string should come from the extension itself instead of this hardcoded switch case.

            foreach (var extension in extensions.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (extension.ToLowerInvariant())
                {
                    case "common":
                        break;
                    case "nohtml":
                        pipeline.DisableHtml();
                        break;
                    default:
                        throw new ArgumentException($"Invalid extension `{extension}` from `{extensions}`", nameof(extensions));
                }
            }
            return pipeline;
        }

        /// <summary>
        /// Disables parsing of ATX and Setex headings
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>The modified pipeline</returns>
        public static MarkdownPipelineBuilder DisableHeadings(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.BlockParsers.TryRemove<HeadingBlockParser>();
            if (pipeline.BlockParsers.TryFind<ParagraphBlockParser>(out var parser))
            {
                parser.ParseSetexHeadings = false;
            }
            return pipeline;
        }

        /// <summary>
        /// Enables parsing and tracking of trivia characters
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>he modified pipeline</returns>
        public static MarkdownPipelineBuilder EnableTrackTrivia(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.TrackTrivia = true;
            if (pipeline.BlockParsers.TryFind<FencedCodeBlockParser>(out var parser))
            {
                parser.InfoParser = FencedCodeBlockParser.RoundtripInfoParser;
            }
            return pipeline;
        }
    }
}
