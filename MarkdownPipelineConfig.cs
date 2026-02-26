using Markdig;

namespace YASN
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
