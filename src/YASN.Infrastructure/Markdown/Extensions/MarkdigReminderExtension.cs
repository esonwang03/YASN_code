using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using YASN.Infrastructure.Reminders;

namespace YASN.Infrastructure.Markdown.Extensions
{
    /// <summary>
    /// Renders the inline reminder syntax <c>[!display][control]{cron}{content}</c> as a styled badge
    /// in the Markdown preview. Scheduling is handled separately by the reminder scheduler; this
    /// extension is purely presentational.
    /// </summary>
    internal sealed class ReminderExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            // Insert before the link parser so "[!...]" is claimed as a reminder, not a link/image.
            pipeline.InlineParsers.Insert(0, new ReminderInlineParser());
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is HtmlRenderer htmlRenderer)
            {
                htmlRenderer.ObjectRenderers.Insert(0, new ReminderInlineRenderer());
            }
        }
    }

    internal static class ReminderExtensionBuilderExtensions
    {
        internal static MarkdownPipelineBuilder UseNoteReminders(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.Extensions.AddIfNotAlready<ReminderExtension>();
            return pipeline;
        }
    }

    /// <summary>
    /// A matched reminder token carried through the Markdig AST to the renderer.
    /// </summary>
    internal sealed class ReminderInline : LeafInline
    {
        internal ReminderInline(NoteReminderRule rule)
        {
            Rule = rule;
        }

        internal NoteReminderRule Rule { get; }
    }

    internal sealed class ReminderInlineParser : InlineParser
    {
        public ReminderInlineParser()
        {
            OpeningCharacters = ['['];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            string text = slice.Text;
            if (!ReminderSyntax.TryMatch(text, slice.Start, out ReminderTokenMatch match))
            {
                return false;
            }

            CronExpression.TryParse(match.CronText, out CronExpression? schedule);
            NoteReminderRule rule = new NoteReminderRule
            {
                DisplayText = match.DisplayText,
                Enabled = match.Enabled,
                RemainingCount = match.RemainingCount,
                CronText = match.CronText,
                Schedule = schedule,
                Content = match.Content,
                SourceStart = match.Start,
                SourceLength = match.Length
            };

            processor.Inline = new ReminderInline(rule)
            {
                Span = new SourceSpan(match.Start, match.Start + match.Length - 1)
            };

            slice.Start = match.Start + match.Length;
            return true;
        }
    }
}
