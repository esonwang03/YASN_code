using System.Net;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using YASN.Infrastructure.Reminders;

namespace YASN.Infrastructure.Markdown.Extensions
{
    /// <summary>
    /// Writes a <see cref="ReminderInline"/> as a <c>&lt;span class="yasn-reminder…"&gt;</c> badge with
    /// a bell glyph, the display text (rendered as Markdown), and a tooltip describing the schedule.
    /// </summary>
    internal sealed class ReminderInlineRenderer : HtmlObjectRenderer<ReminderInline>
    {
        private static readonly MarkdownPipeline InlinePipeline = MarkdownPipelineConfig.Create();

        protected override void Write(HtmlRenderer renderer, ReminderInline obj)
        {
            NoteReminderRule rule = obj.Rule;
            string cssClass = !rule.Enabled
                ? "yasn-reminder yasn-reminder-disabled"
                : rule.Schedule is null
                    ? "yasn-reminder yasn-reminder-invalid"
                    : "yasn-reminder";

            if (rule.Once)
            {
                cssClass += " yasn-reminder-once";
            }

            string title = BuildTitle(rule);
            string displayHtml = StripParagraphWrapper(
                global::Markdig.Markdown.ToHtml(rule.DisplayText, InlinePipeline));

            renderer
                .Write("<span class=\"")
                .Write(cssClass)
                .Write("\" title=\"")
                .Write(WebUtility.HtmlEncode(title))
                .Write("\"><span class=\"yasn-reminder-icon\">\U0001F514</span>")
                .Write(displayHtml)
                .Write("</span>");
        }

        private static string BuildTitle(NoteReminderRule rule)
        {
            string state = rule.Enabled
                ? rule.Schedule is null ? "invalid schedule" : "enabled"
                : "disabled";
            string cadence = rule.Once ? ", once" : string.Empty;
            string content = string.IsNullOrWhiteSpace(rule.Content) ? string.Empty : $" — {rule.Content}";
            return $"Reminder ({state}{cadence}): {rule.CronText}{content}";
        }

        private static string StripParagraphWrapper(string html)
        {
            string trimmed = html.Trim();
            if (trimmed.StartsWith("<p>", StringComparison.Ordinal) &&
                trimmed.EndsWith("</p>", StringComparison.Ordinal))
            {
                return trimmed[3..^4];
            }

            return trimmed;
        }
    }
}
