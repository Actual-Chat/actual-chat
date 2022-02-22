// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using System.IO;
using System.Reflection;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Markdig
{
    /// <summary>
    /// Provides methods for parsing a Markdown string to a syntax tree and converting it to other formats.
    /// </summary>
    public static partial class Markdown
    {
        public static readonly string Version = ((AssemblyFileVersionAttribute) typeof(Markdown).Assembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)[0]).Version;

        public static readonly MarkdownPipeline DefaultPipeline = new MarkdownPipelineBuilder().Build();
        private static readonly MarkdownPipeline _defaultTrackTriviaPipeline = new MarkdownPipelineBuilder().EnableTrackTrivia().Build();

        /// <summary>
        /// Parses the specified markdown into an AST <see cref="MarkdownDocument"/>
        /// </summary>
        /// <param name="markdown">The markdown text.</param>
        /// <param name="trackTrivia">Whether to parse trivia such as whitespace, extra heading characters and unescaped string values.</param>
        /// <returns>An AST Markdown document</returns>
        /// <exception cref="ArgumentNullException">if markdown variable is null</exception>
        public static MarkdownDocument Parse(string markdown, bool trackTrivia = false)
        {
            if (markdown is null) ThrowHelper.ArgumentNullException_markdown();

            MarkdownPipeline? pipeline = trackTrivia ? _defaultTrackTriviaPipeline : null;

            return Parse(markdown, pipeline);
        }

        /// <summary>
        /// Parses the specified markdown into an AST <see cref="MarkdownDocument"/>
        /// </summary>
        /// <param name="markdown">The markdown text.</param>
        /// <param name="pipeline">The pipeline used for the parsing.</param>
        /// <param name="context">A parser context used for the parsing.</param>
        /// <returns>An AST Markdown document</returns>
        /// <exception cref="ArgumentNullException">if markdown variable is null</exception>
        public static MarkdownDocument Parse(string markdown, MarkdownPipeline? pipeline, MarkdownParserContext? context = null)
        {
            if (markdown is null) ThrowHelper.ArgumentNullException_markdown();

            pipeline ??= DefaultPipeline;

            return MarkdownParser.Parse(markdown, pipeline, context);
        }
    }
}
