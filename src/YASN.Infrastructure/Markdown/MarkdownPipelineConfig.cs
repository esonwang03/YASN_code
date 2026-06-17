using Markdig;

namespace YASN.Infrastructure.Markdown
{
    internal static class MarkdownPipelineConfig
    {
        internal static MarkdownPipeline Create()
        {
            return new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UsePreciseSourceLocation()
                .UseHexColorText()
                .UseNoteReminders()
                .UseSourceLines()
                .Build();
        }
    }
}
