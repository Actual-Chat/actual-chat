// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Markdig.Helpers;
using Markdig.Syntax;

namespace Markdig.Renderers.Html
{
    /// <summary>
    /// Attached HTML attributes to a <see cref="MarkdownObject"/>.
    /// </summary>
    public class HtmlAttributes : MarkdownObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HtmlAttributes"/> class.
        /// </summary>
        public HtmlAttributes()
        {
        }

        /// <summary>
        /// Gets or sets the HTML id/identifier. May be null.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the CSS classes attached. May be null.
        /// </summary>
        public List<string>? Classes { get; set; }

        /// <summary>
        /// Gets or sets the additional properties. May be null.
        /// </summary>
        public List<KeyValuePair<string, string?>>? Properties { get; set; }

        /// <summary>
        /// Adds a CSS class.
        /// </summary>
        /// <param name="name">The css class name.</param>
        public void AddClass(string name)
        {
            if (name is null) ThrowHelper.ArgumentNullException_name();

            Classes ??= new (2);// Use half list compare to default capacity (4), as we don't expect lots of classes

 #pragma warning disable MA0002
            if (!Classes.Contains(name))
 #pragma warning restore MA0002
            {
                Classes.Add(name);
            }
        }

        /// <summary>
        /// Adds a property.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value.</param>
        public void AddProperty(string name, string value)
        {
            if (name is null) ThrowHelper.ArgumentNullException_name();

            Properties ??= new (2); // Use half list compare to default capacity (4), as we don't expect lots of classes

            Properties.Add(new KeyValuePair<string, string?>(name, value));
        }

        /// <summary>
        /// Adds the specified property only if it does not already exist.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value.</param>
        public void AddPropertyIfNotExist(string name, object? value)
        {
            if (name is null) ThrowHelper.ArgumentNullException_name();
            if (Properties is null)
            {
                Properties = new (4);
            }
            else
            {
                for (int i = 0; i < Properties.Count; i++)
                {
                    if (Properties[i].Key.Equals(name, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            Properties.Add(new KeyValuePair<string, string?>(name, value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Copies/merge the values from this instance to the specified <see cref="HtmlAttributes"/> instance.
        /// </summary>
        /// <param name="htmlAttributes">The HTML attributes.</param>
        /// <param name="mergeIdAndProperties">If set to <c>true</c> it will merge properties to the target htmlAttributes. Default is <c>false</c></param>
        /// <param name="shared">If set to <c>true</c> it will try to share Classes and Properties if destination don't have them, otherwise it will make a copy. Default is <c>true</c></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void CopyTo(HtmlAttributes htmlAttributes, bool mergeIdAndProperties = false, bool shared = true)
        {
            if (htmlAttributes is null) ThrowHelper.ArgumentNullException(nameof(htmlAttributes));
            // Add html htmlAttributes to the object
            if (!mergeIdAndProperties || Id != null)
            {
                htmlAttributes.Id = Id;
            }
            if (htmlAttributes.Classes is null)
            {
                htmlAttributes.Classes = shared ? Classes : Classes != null ? new (Classes) : null;
            }
            else if (Classes != null)
            {
                htmlAttributes.Classes.AddRange(Classes);
            }

            if (htmlAttributes.Properties is null)
            {
                htmlAttributes.Properties = shared ? Properties : Properties != null ? new (Properties) : null;
            }
            else if (Properties != null)
            {
                if (mergeIdAndProperties)
                {
                    foreach (var prop in Properties)
                    {
                        htmlAttributes.AddPropertyIfNotExist(prop.Key, prop.Value);
                    }
                }
                else
                {
                    htmlAttributes.Properties.AddRange(Properties);
                }
            }
        }
    }

    /// <summary>
    /// Extensions for a <see cref="MarkdownObject"/> to allow accessing <see cref="HtmlAttributes"/>
    /// </summary>
    public static class HtmlAttributesExtensions
    {
        private static readonly object Key = typeof (HtmlAttributes);

        /// <summary>
        /// Tries the get <see cref="HtmlAttributes"/> stored on a <see cref="MarkdownObject"/>.
        /// </summary>
        /// <param name="obj">The markdown object.</param>
        /// <returns>The attached html attributes or null if not found</returns>
        public static HtmlAttributes? TryGetAttributes(this IMarkdownObject obj)
        {
            return obj.GetData(Key) as HtmlAttributes;
        }

        /// <summary>
        /// Gets or creates the <see cref="HtmlAttributes"/> stored on a <see cref="MarkdownObject"/>
        /// </summary>
        /// <param name="obj">The markdown object.</param>
        /// <returns>The attached html attributes</returns>
        public static HtmlAttributes GetAttributes(this IMarkdownObject obj)
        {
            var attributes = obj.GetData(Key) as HtmlAttributes;
            if (attributes is null)
            {
                attributes = new HtmlAttributes();
                obj.SetAttributes(attributes);
            }
            return attributes;
        }

        /// <summary>
        /// Sets <see cref="HtmlAttributes" /> to the <see cref="MarkdownObject" />
        /// </summary>
        /// <param name="obj">The markdown object.</param>
        /// <param name="attributes">The attributes to attach.</param>
        public static void SetAttributes(this IMarkdownObject obj, HtmlAttributes attributes)
        {
            obj.SetData(Key, attributes);
        }
    }
}
