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

        /// <summary>
        /// Builds a pipeline for rendering short Markdown fragments that must sit inline (badge labels,
        /// coloured spans). Identical to <see cref="Create"/> but without <c>UseSourceLines</c>: a fragment
        /// has no meaningful block source line, and the extra <c>data-source-line</c> attribute would defeat
        /// the bare-<c>&lt;p&gt;</c> unwrap callers rely on to keep the fragment on one line.
        /// </summary>
        internal static MarkdownPipeline CreateInline()
        {
            return new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UsePreciseSourceLocation()
                .UseHexColorText()
                .UseNoteReminders()
                .Build();
        }
    }
}
