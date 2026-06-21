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
        private static readonly MarkdownPipeline InlinePipeline = MarkdownPipelineConfig.CreateInline();

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

            // Render the display label as inline-only HTML: parse it as Markdown, then strip the block
            // paragraph wrapper Markdig adds, so the label cannot introduce a block element that breaks
            // the badge onto multiple lines.
            string displayHtml = RenderInline(rule.DisplayText);
            string countHtml = rule.Enabled && rule.RemainingCount is { } remaining && remaining > 1
                ? $" <span class=\"yasn-reminder-count\">×{remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}</span>"
                : string.Empty;

            // Layout-critical properties (single inline row, no wrapping) are written inline so the badge
            // renders correctly even if the external stylesheet is missing or stale; the .yasn-reminder
            // classes carry only cosmetic styling (pill border, colour, size).
            renderer
                .Write("<span class=\"")
                .Write(cssClass)
                .Write("\" style=\"display:inline;white-space:nowrap\" title=\"")
                .Write(WebUtility.HtmlEncode(title))
                .Write("\"><span class=\"yasn-reminder-icon\">\U0001F514</span> ")
                .Write(displayHtml)
                .Write(countHtml)
                .Write("</span>");
        }

        private static string BuildTitle(NoteReminderRule rule)
        {
            string state = rule.Enabled
                ? rule.Schedule is null ? "invalid schedule" : "enabled"
                : "disabled";
            string cadence = rule.RemainingCount switch
            {
                null => string.Empty,
                1 => ", once",
                { } n => $", {n} times left"
            };
            string content = string.IsNullOrWhiteSpace(rule.Content) ? string.Empty : $" — {rule.Content}";
            return $"Reminder ({state}{cadence}): {rule.CronText}{content}";
        }

        /// <summary>
        /// Renders a short Markdown label as inline HTML, dropping the single block paragraph wrapper
        /// Markdig emits so the result can sit inline inside the badge. The label is always a single
        /// paragraph, so unwrapping it is exact rather than heuristic.
        /// </summary>
        private static string RenderInline(string markdown)
        {
            string html = global::Markdig.Markdown.ToHtml(markdown ?? string.Empty, InlinePipeline).Trim();
            if (html.StartsWith("<p>", StringComparison.Ordinal) &&
                html.EndsWith("</p>", StringComparison.Ordinal))
            {
                return html[3..^4].Trim();
            }

            return html;
        }
    }
}
