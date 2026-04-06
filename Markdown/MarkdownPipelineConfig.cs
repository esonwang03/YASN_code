using Markdig;
using YASN.Markdown.Extensions;

namespace YASN.Markdown
{
    internal static class MarkdownPipelineConfig
    {
        internal static MarkdownPipeline Create()
        {
            return new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseHexColorText()
                .Build();
        }
    }
}
