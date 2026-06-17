using System.Globalization;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace YASN.Infrastructure.Markdown.Extensions
{
    /// <summary>
    /// Annotates every rendered block with a <c>data-source-line</c> attribute carrying its 0-based
    /// source line. The preview uses these anchors to scroll to the editor's caret line. Purely
    /// additive: the rendered HTML is unchanged except for the extra attribute, which the default
    /// <see cref="Markdig.Renderers.HtmlRenderer"/> emits from each block's attached attributes.
    /// </summary>
    internal sealed class SourceLineExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            // Run after parsing completes so every block has its final source Line. Annotating here
            // (rather than in a renderer) keeps the change renderer-agnostic and order-independent.
            pipeline.DocumentProcessed += AnnotateBlocks;
        }

        public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
        {
            // No renderer customization: HtmlRenderer already writes attached HtmlAttributes.
        }

        private static void AnnotateBlocks(MarkdownDocument document)
        {
            foreach (Block block in document.Descendants<Block>())
            {
                if (block.Line < 0)
                {
                    continue;
                }

                block.GetAttributes().AddPropertyIfNotExist(
                    "data-source-line",
                    block.Line.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    internal static class SourceLineExtensionBuilderExtensions
    {
        internal static MarkdownPipelineBuilder UseSourceLines(this MarkdownPipelineBuilder pipeline)
        {
            pipeline.Extensions.AddIfNotAlready<SourceLineExtension>();
            return pipeline;
        }
    }
}
