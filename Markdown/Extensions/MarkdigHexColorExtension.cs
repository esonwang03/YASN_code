using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace YASN.Markdown.Extensions
{
    internal sealed class HexColorExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            pipeline.InlineParsers.Insert(0, new HexColorInlineParser());
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is HtmlRenderer htmlRenderer)
            {
                htmlRenderer.ObjectRenderers.Insert(0, new HexColorInlineRenderer());
            }
        }
    }

    internal static class HexColorExtensionBuilderExtensions
    {
        internal static MarkdownPipelineBuilder UseHexColorText(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.Extensions.AddIfNotAlready<HexColorExtension>();
            return pipeline;
        }
    }

    internal sealed class HexColorInline : LeafInline
    {
        internal HexColorInline(string colorHex, string markdownText)
        {
            ColorHex = colorHex;
            MarkdownText = markdownText;
        }

        internal string ColorHex { get; }
        internal string MarkdownText { get; }
    }

    internal sealed class HexColorInlineParser : InlineParser
    {
        private static readonly Dictionary<string, string> ColorAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["b"] = "#0000FF",
            ["blue"] = "#0000FF",
            ["r"] = "#FF0000",
            ["red"] = "#FF0000",
            ["g"] = "#00AA00",
            ["green"] = "#00AA00",
            ["y"] = "#FFD700",
            ["yellow"] = "#FFD700",
            ["o"] = "#FFA500",
            ["orange"] = "#FFA500",
            ["p"] = "#800080",
            ["purple"] = "#800080",
            ["c"] = "#00CED1",
            ["cyan"] = "#00CED1",
            ["k"] = "#000000",
            ["black"] = "#000000",
            ["w"] = "#FFFFFF",
            ["white"] = "#FFFFFF"
        };

        public HexColorInlineParser()
        {
            OpeningCharacters = ['{'];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var text = slice.Text;
            var start = slice.Start;
            var end = slice.End;

            if (start + 5 > end || text[start] != '{' || text[start + 1] != '#')
            {
                return false;
            }

            var i = start + 2;
            var colorTokenStart = i;
            while (i <= end && text[i] != '|')
            {
                i++;
            }

            var colorTokenLength = i - colorTokenStart;
            if (i > end || text[i] != '|' || colorTokenLength <= 0)
            {
                return false;
            }

            var colorToken = text.Substring(colorTokenStart, colorTokenLength);
            if (!TryResolveColorHex(colorToken, out var colorHex))
            {
                return false;
            }

            i++;
            var contentStart = i;
            while (i <= end && text[i] != '}')
            {
                i++;
            }

            if (i > end || i == contentStart)
            {
                return false;
            }

            var content = text.Substring(contentStart, i - contentStart);
            var inline = new HexColorInline(colorHex, content)
            {
                Span = new SourceSpan(start, i)
            };

            processor.Inline = inline;
            slice.Start = i + 1;
            return true;
        }

        private static bool IsHex(char c)
        {
            return c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
        }

        private static bool TryResolveColorHex(string token, out string colorHex)
        {
            if ((token.Length == 6 || token.Length == 8) && token.All(IsHex))
            {
                colorHex = "#" + token;
                return true;
            }

            if (ColorAliases.TryGetValue(token, out var aliasedHex))
            {
                colorHex = aliasedHex;
                return true;
            }

            colorHex = string.Empty;
            return false;
        }
    }

    internal sealed class HexColorInlineRenderer : HtmlObjectRenderer<HexColorInline>
    {
        private static readonly MarkdownPipeline InlinePipeline = MarkdownPipelineConfig.Create();

        protected override void Write(HtmlRenderer renderer, HexColorInline obj)
        {
            var inlineHtml = StripParagraphWrapper(global::Markdig.Markdown.ToHtml(obj.MarkdownText, InlinePipeline));

            renderer
                .Write("<span style=\"color:")
                .Write(obj.ColorHex)
                .Write("\">")
                .Write(inlineHtml)
                .Write("</span>");
        }

        private static string StripParagraphWrapper(string html)
        {
            var trimmed = html.Trim();
            if (trimmed.StartsWith("<p>", StringComparison.Ordinal) &&
                trimmed.EndsWith("</p>", StringComparison.Ordinal))
            {
                return trimmed[3..^4];
            }

            return trimmed;
        }
    }
}
