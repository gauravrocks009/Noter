using System;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;

namespace AppleNotesWpf;

public static class MarkdownHelper
{
    private const string UNCHECKED = "☐ ";
    private const string CHECKED = "☑ ";

    public static string FlowDocumentToMarkdown(FlowDocument doc)
    {
        StringBuilder sb = new StringBuilder();
        bool isFirst = true;
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph para)
            {
                string paraMarkdown = ConvertParagraphToMarkdown(para, isFirst);
                if (isFirst && !string.IsNullOrWhiteSpace(paraMarkdown))
                {
                    isFirst = false;
                }
                sb.AppendLine(paraMarkdown);
            }
        }
        return sb.ToString();
    }

    private static string ConvertParagraphToMarkdown(Paragraph para, bool isFirstParagraph)
    {
        StringBuilder sb = new StringBuilder();
        
        string prefix = "";
        bool isChecklist = false;
        
        string fullText = new TextRange(para.ContentStart, para.ContentEnd).Text;
        if (fullText.StartsWith("☐"))
        {
            prefix = "- [ ] ";
            isChecklist = true;
        }
        else if (fullText.StartsWith("☑"))
        {
            prefix = "- [x] ";
            isChecklist = true;
        }
        else if (isFirstParagraph)
        {
            prefix = "# ";
        }
        
        sb.Append(prefix);
        
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
            {
                string runText = run.Text;
                if (isChecklist)
                {
                    if (runText.StartsWith("☐ ") || runText.StartsWith("☑ "))
                    {
                        runText = runText.Substring(2);
                    }
                    else if (runText.StartsWith("☐") || runText.StartsWith("☑"))
                    {
                        runText = runText.Substring(1);
                    }
                    isChecklist = false;
                }
                
                if (string.IsNullOrEmpty(runText)) continue;
                
                bool isBold = run.FontWeight == FontWeights.Bold;
                bool isItalic = run.FontStyle == FontStyles.Italic;
                
                if (isBold && isItalic)
                {
                    sb.Append($"***{runText}***");
                }
                else if (isBold)
                {
                    sb.Append($"**{runText}**");
                }
                else if (isItalic)
                {
                    sb.Append($"*{runText}*");
                }
                else
                {
                    sb.Append(runText);
                }
            }
            else if (inline is LineBreak)
            {
                sb.AppendLine();
            }
            else if (inline is Span span)
            {
                string spanText = new TextRange(span.ContentStart, span.ContentEnd).Text;
                if (isChecklist)
                {
                    if (spanText.StartsWith("☐ ") || spanText.StartsWith("☑ "))
                    {
                        spanText = spanText.Substring(2);
                    }
                    else if (spanText.StartsWith("☐") || spanText.StartsWith("☑"))
                    {
                        spanText = spanText.Substring(1);
                    }
                    isChecklist = false;
                }
                if (string.IsNullOrEmpty(spanText)) continue;
                
                bool isBold = span.FontWeight == FontWeights.Bold;
                bool isItalic = span.FontStyle == FontStyles.Italic;
                
                if (isBold && isItalic)
                {
                    sb.Append($"***{spanText}***");
                }
                else if (isBold)
                {
                    sb.Append($"**{spanText}**");
                }
                else if (isItalic)
                {
                    sb.Append($"*{spanText}*");
                }
                else
                {
                    sb.Append(spanText);
                }
            }
        }
        
        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static void MarkdownToFlowDocument(string markdown, FlowDocument doc)
    {
        doc.Blocks.Clear();
        
        string[] lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        for (int idx = 0; idx < lines.Length; idx++)
        {
            string line = lines[idx];
            
            if (idx == lines.Length - 1 && string.IsNullOrEmpty(line))
            {
                break;
            }
            
            var para = new Paragraph();
            
            if (line.StartsWith("# "))
            {
                string titleText = line.Substring(2);
                AddParsedInlines(para, titleText);
            }
            else if (line.StartsWith("- [ ] "))
            {
                string itemText = line.Substring(6);
                para.Inlines.Add(new Run(UNCHECKED));
                AddParsedInlines(para, itemText);
            }
            else if (line.StartsWith("- [x] ") || line.StartsWith("- [X] "))
            {
                string itemText = line.Substring(6);
                para.Inlines.Add(new Run(CHECKED));
                AddParsedInlines(para, itemText);
                
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)));
            }
            else
            {
                AddParsedInlines(para, line);
            }
            
            doc.Blocks.Add(para);
        }
        
        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new Paragraph());
        }
    }

    private static void AddParsedInlines(Paragraph para, string lineText)
    {
        int i = 0;
        while (i < lineText.Length)
        {
            if (i + 2 < lineText.Length && (lineText.Substring(i, 3) == "***" || lineText.Substring(i, 3) == "___"))
            {
                string marker = lineText.Substring(i, 3);
                int endIdx = lineText.IndexOf(marker, i + 3);
                if (endIdx != -1)
                {
                    string content = lineText.Substring(i + 3, endIdx - (i + 3));
                    var run = new Run(content) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
                    para.Inlines.Add(run);
                    i = endIdx + 3;
                    continue;
                }
            }
            if (i + 1 < lineText.Length && (lineText.Substring(i, 2) == "**" || lineText.Substring(i, 2) == "__"))
            {
                string marker = lineText.Substring(i, 2);
                int endIdx = lineText.IndexOf(marker, i + 2);
                if (endIdx != -1)
                {
                    string content = lineText.Substring(i + 2, endIdx - (i + 2));
                    var run = new Run(content) { FontWeight = FontWeights.Bold };
                    para.Inlines.Add(run);
                    i = endIdx + 2;
                    continue;
                }
            }
            if (lineText[i] == '*' || lineText[i] == '_')
            {
                char marker = lineText[i];
                int endIdx = lineText.IndexOf(marker, i + 1);
                if (endIdx != -1)
                {
                    string content = lineText.Substring(i + 1, endIdx - (i + 1));
                    var run = new Run(content) { FontStyle = FontStyles.Italic };
                    para.Inlines.Add(run);
                    i = endIdx + 1;
                    continue;
                }
            }

            int nextSpecial = -1;
            for (int j = i; j < lineText.Length; j++)
            {
                if (lineText[j] == '*' || lineText[j] == '_')
                {
                    nextSpecial = j;
                    break;
                }
            }

            if (nextSpecial == -1)
            {
                para.Inlines.Add(new Run(lineText.Substring(i)));
                break;
            }
            else
            {
                para.Inlines.Add(new Run(lineText.Substring(i, nextSpecial - i)));
                i = nextSpecial;
            }
        }
    }
}
