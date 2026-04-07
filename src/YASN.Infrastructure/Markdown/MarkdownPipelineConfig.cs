using Markdig;
using YASN.Infrastructure.Markdown.Extensions;

namespace YASN.Infrastructure.Markdown
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
