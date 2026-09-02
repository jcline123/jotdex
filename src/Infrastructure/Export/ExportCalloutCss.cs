namespace Jotdex.Infrastructure.Export;

/// <summary>
/// Styles Markdig GitHub-alert HTML plus older <c>&lt;blockquote data-callout&gt;</c> callouts
/// in Share HTML and static vault export.
/// </summary>
internal static class ExportCalloutCss
{
    public const string Rules = """
        .markdown-alert, blockquote[data-callout], blockquote.callout {
          margin: 1rem 0; padding: .65rem 1rem; border-left: 4px solid #667085;
          border-radius: 0 8px 8px 0; background: rgba(102,112,133,.1); color: inherit;
        }
        .markdown-alert > :first-child, blockquote[data-callout] > :first-child, blockquote.callout > :first-child { margin-top: 0; }
        .markdown-alert > :last-child, blockquote[data-callout] > :last-child, blockquote.callout > :last-child { margin-bottom: 0; }
        .markdown-alert-title { font-weight: 650; margin: 0 0 .35rem; }
        .markdown-alert-note, blockquote[data-callout="note"], blockquote.callout-note { border-left-color: #667085; background: rgba(102,112,133,.1); }
        .markdown-alert-tip, blockquote[data-callout="tip"], blockquote.callout-tip { border-left-color: #027a48; background: rgba(2,122,72,.1); }
        .markdown-alert-info, .markdown-alert-important, blockquote[data-callout="info"], blockquote.callout-info { border-left-color: #175cd3; background: rgba(23,92,211,.1); }
        .markdown-alert-warning, blockquote[data-callout="warning"], blockquote.callout-warning { border-left-color: #b54708; background: rgba(181,71,8,.12); }
        .markdown-alert-danger, .markdown-alert-caution, blockquote[data-callout="danger"], blockquote.callout-danger { border-left-color: #b42318; background: rgba(180,35,24,.12); }
        mark { background: #f5d565; padding: 0 .12em; }
        .jotdex-details { border: 1px solid #d0d5dd; border-radius: 8px; padding: .5rem .85rem; margin: 1rem 0; }
        .jotdex-details > summary { font-weight: 650; cursor: pointer; }
        .jotdex-align-center { text-align: center; }
        .jotdex-align-right { text-align: right; }
        .jotdex-align-justify { text-align: justify; }
        .jotdex-figure { margin: 1rem 0; }
        .jotdex-figure img { max-width: 100%; height: auto; }
        .jotdex-figure figcaption { font-size: .9rem; color: #667085; }
        .jotdex-math { font-family: Cambria, 'Times New Roman', serif; }
        .jotdex-link-card { border: 1px solid #d0d5dd; border-radius: 8px; padding: .65rem .85rem; margin: .75rem 0; }
        """;
}
