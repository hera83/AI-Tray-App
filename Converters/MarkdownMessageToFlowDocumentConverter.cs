using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TrayApp.Models;
using ChatMessage = TrayApp.Models.Message;
using MdBlock = Markdig.Syntax.Block;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBlock = System.Windows.Documents.Block;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace TrayApp.Converters
{
    public class MarkdownMessageToFlowDocumentConverter : IValueConverter, IMultiValueConverter
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .Build();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ChatMessage message)
                return CreateDocument(message.Content, message.Role);

            if (value is string markdown)
                return CreateDocument(markdown, MessageRole.Assistant);

            return CreateDocument(string.Empty, MessageRole.Assistant);
        }

        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is { Length: > 0 })
            {
                var content = values[0] as string;
                var role = values.Length > 1 && values[1] is MessageRole messageRole
                    ? messageRole
                    : MessageRole.Assistant;

                return CreateDocument(content, role);
            }

            return CreateDocument(string.Empty, MessageRole.Assistant);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return WpfBinding.DoNothing;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            return Array.Empty<object>();
        }

        private static FlowDocument CreateDocument(string? markdown, MessageRole role)
        {
            var foreground = ResolveRoleForeground(role);
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                FontSize = ResolveDouble("FontSize.Body", 16),
                LineHeight = 22,
                Foreground = foreground,
                Background = WpfBrushes.Transparent
            };

            if (string.IsNullOrWhiteSpace(markdown))
                return doc;

            try
            {
                var parsed = Markdig.Markdown.Parse(markdown, Pipeline);
                AppendBlocks(doc.Blocks, parsed, role);
            }
            catch
            {
                doc.Blocks.Add(CreateFallbackParagraph(markdown, role));
            }

            if (doc.Blocks.Count == 0)
                doc.Blocks.Add(CreateFallbackParagraph(markdown, role));

            return doc;
        }

        private static void AppendBlocks(BlockCollection target, ContainerBlock container, MessageRole role)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case HeadingBlock heading:
                        target.Add(CreateHeadingParagraph(heading, role));
                        break;
                    case ParagraphBlock paragraph:
                        target.Add(CreateParagraph(paragraph.Inline, role));
                        break;
                    case QuoteBlock quote:
                        target.Add(CreateQuoteSection(quote, role));
                        break;
                    case ListBlock listBlock:
                        target.Add(CreateListBlock(listBlock, role));
                        break;
                    case FencedCodeBlock fencedCode:
                        target.Add(CreateCodeParagraph(GetBlockText(fencedCode), role));
                        break;
                    case CodeBlock codeBlock:
                        target.Add(CreateCodeParagraph(GetBlockText(codeBlock), role));
                        break;
                    case ThematicBreakBlock:
                        target.Add(CreateRuleBlock());
                        break;
                    case ContainerBlock nested:
                        AppendBlocks(target, nested, role);
                        break;
                    default:
                    {
                        var fallback = GetBlockText(block);
                        if (!string.IsNullOrWhiteSpace(fallback))
                            target.Add(CreateFallbackParagraph(fallback, role));
                        break;
                    }
                }
            }
        }

        private static Paragraph CreateParagraph(ContainerInline? inline, MessageRole role)
        {
            var paragraph = CreateBaseParagraph(role);
            AppendInlines(paragraph.Inlines, inline, role);
            return paragraph;
        }

        private static Paragraph CreateHeadingParagraph(HeadingBlock heading, MessageRole role)
        {
            var paragraph = CreateBaseParagraph(role);
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.FontSize = heading.Level switch
            {
                1 => 22,
                2 => 20,
                3 => 18,
                _ => 16
            };
            paragraph.Margin = heading.Level switch
            {
                1 => new Thickness(0, 10, 0, 8),
                2 => new Thickness(0, 8, 0, 6),
                _ => new Thickness(0, 6, 0, 4)
            };

            AppendInlines(paragraph.Inlines, heading.Inline, role);
            return paragraph;
        }

        private static Section CreateQuoteSection(QuoteBlock quote, MessageRole role)
        {
            var section = new Section
            {
                Margin = new Thickness(10, 4, 0, 8),
                Padding = new Thickness(8, 0, 0, 0),
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = ResolveBrush("Brush.Theme.Border", WpfBrushes.Gray),
                Foreground = ResolveRoleForeground(role)
            };

            AppendBlocks(section.Blocks, quote, role);
            if (section.Blocks.Count == 0)
                section.Blocks.Add(CreateFallbackParagraph(string.Empty, role));

            return section;
        }

        private static System.Windows.Documents.List CreateListBlock(ListBlock listBlock, MessageRole role)
        {
            var list = new System.Windows.Documents.List
            {
                MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 4, 0, 8),
                Padding = new Thickness(20, 0, 0, 0)
            };

            foreach (var itemBlock in listBlock)
            {
                if (itemBlock is not ListItemBlock listItemBlock)
                    continue;

                var item = new ListItem();
                AppendBlocks(item.Blocks, listItemBlock, role);

                if (item.Blocks.Count == 0)
                    item.Blocks.Add(CreateFallbackParagraph(string.Empty, role));

                list.ListItems.Add(item);
            }

            return list;
        }

        private static Paragraph CreateCodeParagraph(string code, MessageRole role)
        {
            var trimmedCode = (code ?? string.Empty).TrimEnd('\r', '\n');
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 4, 0, 8),
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = ResolveBrush("Brush.Theme.Border", WpfBrushes.Gray),
                Background = ResolveBrush("Brush.Theme.SurfaceBackground", WpfBrushes.Transparent),
                FontFamily = new WpfFontFamily("Consolas"),
                Foreground = ResolveRoleForeground(role)
            };

            paragraph.Inlines.Add(new Run(trimmedCode));
            return paragraph;
        }

        private static WpfBlock CreateRuleBlock()
        {
            return new BlockUIContainer
            {
                Child = new System.Windows.Controls.Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 6, 0, 6),
                    Background = ResolveBrush("Brush.Theme.Border", WpfBrushes.Gray)
                }
            };
        }

        private static Paragraph CreateFallbackParagraph(string text, MessageRole role)
        {
            var paragraph = CreateBaseParagraph(role);
            paragraph.Inlines.Add(new Run(text ?? string.Empty));
            return paragraph;
        }

        private static Paragraph CreateBaseParagraph(MessageRole role)
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = ResolveRoleForeground(role)
            };
        }

        private static void AppendInlines(InlineCollection target, ContainerInline? container, MessageRole role)
        {
            if (container == null)
                return;

            var current = container.FirstChild;
            while (current != null)
            {
                switch (current)
                {
                    case LiteralInline literal:
                    {
                        var text = literal.Content.ToString();
                        if (!string.IsNullOrEmpty(text))
                            target.Add(new Run(text));
                        break;
                    }
                    case LineBreakInline:
                        target.Add(new LineBreak());
                        break;
                    case CodeInline codeInline:
                    {
                        var inline = new Span(new Run(codeInline.Content))
                        {
                            FontFamily = new WpfFontFamily("Consolas"),
                            Background = ResolveBrush("Brush.Theme.SurfaceBackground", WpfBrushes.Transparent)
                        };
                        target.Add(inline);
                        break;
                    }
                    case EmphasisInline emphasis:
                    {
                        Span span = emphasis.DelimiterCount >= 2
                            ? new Bold()
                            : new Italic();
                        AppendInlines(span.Inlines, emphasis, role);
                        target.Add(span);
                        break;
                    }
                    case LinkInline link:
                        AppendLink(target, link, role);
                        break;
                    case ContainerInline nested:
                    {
                        var span = new Span();
                        AppendInlines(span.Inlines, nested, role);
                        target.Add(span);
                        break;
                    }
                    default:
                    {
                        var fallback = current.ToString();
                        if (!string.IsNullOrWhiteSpace(fallback))
                            target.Add(new Run(fallback));
                        break;
                    }
                }

                current = current.NextSibling;
            }
        }

        private static void AppendLink(InlineCollection target, LinkInline link, MessageRole role)
        {
            if (link.IsImage)
            {
                var altText = ExtractInlineText(link);
                if (!string.IsNullOrWhiteSpace(altText))
                    target.Add(new Run(altText));
                return;
            }

            var text = ExtractInlineText(link);
            if (string.IsNullOrWhiteSpace(text))
                text = string.IsNullOrWhiteSpace(link.Url) ? string.Empty : link.Url;

            var accent = ResolveBrush("Brush.Theme.Accent", WpfBrushes.DodgerBlue);
            var url = link.GetDynamicUrl != null
                ? link.GetDynamicUrl()
                : link.Url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var hyperlink = new Hyperlink(new Run(text))
                {
                    NavigateUri = uri,
                    Foreground = accent,
                    TextDecorations = TextDecorations.Underline
                };
                hyperlink.Click += (_, __) => OpenUri(uri);
                target.Add(hyperlink);
                return;
            }

            target.Add(new Run(text)
            {
                Foreground = accent,
                TextDecorations = TextDecorations.Underline
            });
        }

        private static string ExtractInlineText(ContainerInline container)
        {
            var sb = new StringBuilder();
            AppendInlineText(sb, container);
            return sb.ToString();
        }

        private static void AppendInlineText(StringBuilder sb, ContainerInline? container)
        {
            if (container == null)
                return;

            var current = container.FirstChild;
            while (current != null)
            {
                switch (current)
                {
                    case LiteralInline literal:
                        sb.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        sb.Append(code.Content);
                        break;
                    case LineBreakInline:
                        sb.AppendLine();
                        break;
                    case ContainerInline nested:
                        AppendInlineText(sb, nested);
                        break;
                }

                current = current.NextSibling;
            }
        }

        private static string GetBlockText(MdBlock block)
        {
            return block switch
            {
                LeafBlock leaf => leaf.Lines.ToString(),
                _ => string.Empty
            };
        }

        private static WpfBrush ResolveRoleForeground(MessageRole role)
        {
            var key = role == MessageRole.User
                ? "Brush.Theme.Chat.Message.UserForeground"
                : "Brush.Theme.Chat.Message.AssistantForeground";

            return ResolveBrush(key, WpfBrushes.Black);
        }

        private static WpfBrush ResolveBrush(string resourceKey, WpfBrush fallback)
        {
            if (WpfApplication.Current?.TryFindResource(resourceKey) is WpfBrush brush)
                return brush;

            return fallback;
        }

        private static double ResolveDouble(string resourceKey, double fallback)
        {
            if (WpfApplication.Current?.TryFindResource(resourceKey) is double value)
                return value;

            return fallback;
        }

        private static void OpenUri(Uri uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }
}
