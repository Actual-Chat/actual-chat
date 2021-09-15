using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users;

namespace ActualChat.Chat.Markup
{
    public interface IMarkupParser
    {
        public Task<Markup> Parse(string text, CancellationToken cancellationToken);
    }

    public class MarkupParser : IMarkupParser
    {
        protected IUserNameService UserNames { get; init; }
        protected IUserInfoService UserInfos { get; init; }

        public MarkupParser(IUserNameService userNames, IUserInfoService userInfos)
        {
            UserNames = userNames;
            UserInfos = userInfos;
        }

        public virtual async Task<Markup> Parse(string text, CancellationToken cancellationToken)
        {
            List<MarkupParts.Part> fragments = new();
            var start = 0;

            void AddRawText(int length) {
                fragments.Add(new MarkupParts.RawText(start, length));
                start += length;
            }

            void AddSkippedRawText(int length) {
                start -= length;
                AddRawText(length);
            }

            bool HasPrefix(string prefix)
                => text.AsSpan(start).StartsWith(prefix);

            // Parses "{directive}[{out value}]"
            bool TryParseDirective(string directive, out string value) {
                value = "";
                if (!HasPrefix(directive))
                    return false;
                if (start + directive.Length + 2 >= text.Length || text[start + directive.Length] != '[') {
                    AddRawText(directive.Length);
                    return false;
                }
                var valueStartIndex = start + directive.Length + 1;
                var rightBracketIndex = text.IndexOf(']', valueStartIndex);
                if (rightBracketIndex < 0) {
                    AddRawText(directive.Length);
                    return false;
                }
                value = text.Substring(valueStartIndex, rightBracketIndex - valueStartIndex);
                start = rightBracketIndex + 1;
                return true;
            }

            while (start < text.Length) {
                if (text[start] != '@') {
                    var endIndex = text.IndexOf('@', start);
                    if (endIndex < 0)
                        endIndex = text.Length;
                    AddRawText(endIndex - start);
                    continue;
                }
                if (HasPrefix("@@")) {
                    fragments.Add(new MarkupParts.EscapedSymbol(start, 2));
                    start += 2;
                    continue;
                }

                var directiveStart = start;
                if (TryParseDirective("@user", out var userId)) {
                    var directiveLength = start - directiveStart;
                    var user = await UserInfos.TryGet(userId, cancellationToken);
                    if (user == null) {
                        AddSkippedRawText(directiveLength);
                        continue;
                    }
                    fragments.Add(new MarkupParts.UserMention(directiveStart, directiveLength, user));
                    continue;
                }
                if (HasPrefix("@")) {
                    var name = UserNames.ParseName(text.AsSpan(start + 1)).ToString();
                    if (name.Length == 0) {
                        AddRawText(1);
                        continue;
                    }
                    var user = await UserInfos.TryGetByName(name, cancellationToken);
                    if (user == null) {
                        AddRawText(name.Length + 1);
                        continue;
                    }
                    fragments.Add(new MarkupParts.UserMention(start, name.Length + 1, user));
                    start += name.Length + 1;
                    continue;
                }
            }

            return new Markup(text, fragments.ToArray());
        }
    }
}
