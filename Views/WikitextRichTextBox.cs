using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EtherEditorNative.Views
{
    public class HighlightToken
    {
        public string Text { get; set; }
        public Brush Foreground { get; set; }
        public FontWeight Weight { get; set; }
        public FontStyle Style { get; set; }

        public HighlightToken()
        {
            Weight = FontWeights.Normal;
            Style = FontStyles.Normal;
        }
    }

    public class WikitextRichTextBox : RichTextBox
    {
        private bool _isInternalChange = false;
        private string _syntaxMode = "WikiLink";

        public string SyntaxMode
        {
            get { return _syntaxMode; }
            set
            {
                if (_syntaxMode != value)
                {
                    _syntaxMode = value;
                    ApplyHighlighting();
                }
            }
        }

        public WikitextRichTextBox()
        {
            // Styling defaults matching dark theme editor
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
            Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4));
            CaretBrush = Brushes.White;
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x4f, 0x78));
            BorderThickness = new Thickness(0);
            Padding = new Thickness(16, 10, 16, 10);
            FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New");
            FontSize = 15;

            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                PageWidth = 5000
            };

            TextChanged += OnWikitextRichTextBoxTextChanged;
        }

        private void OnWikitextRichTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInternalChange) return;
            ApplyHighlighting();
        }

        public string Text
        {
            get { return GetTextFromDocument(Document); }
            set
            {
                _isInternalChange = true;
                try
                {
                    SetTextInternal(value);
                }
                finally
                {
                    _isInternalChange = false;
                }
            }
        }

        public int CaretIndex
        {
            get { return GetCaretOffset(); }
            set { SetCaretOffset(value); }
        }

        public string SelectedText
        {
            get
            {
                if (Selection != null) return Selection.Text;
                return "";
            }
            set
            {
                if (Selection != null)
                {
                    Selection.Text = value != null ? value : "";
                }
            }
        }

        public int LineCount
        {
            get
            {
                string text = Text;
                if (string.IsNullOrEmpty(text)) return 1;
                return text.Split('\n').Length;
            }
        }

        public int GetLineIndexFromCharacterIndex(int charIndex)
        {
            string text = Text;
            if (charIndex <= 0 || string.IsNullOrEmpty(text)) return 0;
            if (charIndex > text.Length) charIndex = text.Length;

            int lineIndex = 0;
            for (int i = 0; i < charIndex; i++)
            {
                if (text[i] == '\n') lineIndex++;
            }
            return lineIndex;
        }

        public int GetCharacterIndexFromLineIndex(int lineIndex)
        {
            string text = Text;
            if (lineIndex <= 0 || string.IsNullOrEmpty(text)) return 0;

            int currentLine = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (currentLine == lineIndex) return i;
                if (text[i] == '\n') currentLine++;
            }

            return text.Length;
        }

        private void SetTextInternal(string text)
        {
            text = text != null ? text : "";
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            Document.Blocks.Clear();

            for (int i = 0; i < lines.Length; i++)
            {
                Paragraph p = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };
                List<HighlightToken> tokens = _syntaxMode == "WikiText"
                    ? TokenizeWikiTextLine(lines[i])
                    : TokenizeWikiLinkLine(lines[i]);

                if (tokens.Count == 0 && lines[i].Length == 0)
                {
                    p.Inlines.Add(new Run(""));
                }
                else
                {
                    foreach (var tok in tokens)
                    {
                        p.Inlines.Add(new Run(tok.Text)
                        {
                            Foreground = tok.Foreground,
                            FontWeight = tok.Weight,
                            FontStyle = tok.Style
                        });
                    }
                }

                Document.Blocks.Add(p);
            }
        }

        public void ApplyHighlighting()
        {
            if (_isInternalChange) return;
            _isInternalChange = true;

            try
            {
                int caretOffset = GetCaretOffset();
                string fullText = GetTextFromDocument(Document);

                SetTextInternal(fullText);
                SetCaretOffset(caretOffset);
            }
            finally
            {
                _isInternalChange = false;
            }
        }

        private string GetTextFromDocument(FlowDocument doc)
        {
            if (doc == null) return "";
            StringBuilder sb = new StringBuilder();
            bool first = true;

            foreach (Block b in doc.Blocks)
            {
                Paragraph p = b as Paragraph;
                if (p != null)
                {
                    if (!first) sb.Append("\n");
                    first = false;

                    TextRange range = new TextRange(p.ContentStart, p.ContentEnd);
                    string text = range.Text;
                    if (text.EndsWith("\r\n")) text = text.Substring(0, text.Length - 2);
                    else if (text.EndsWith("\n") || text.EndsWith("\r")) text = text.Substring(0, text.Length - 1);

                    sb.Append(text);
                }
            }

            return sb.ToString();
        }

        private int GetCaretOffset()
        {
            TextPointer caret = CaretPosition;
            if (caret == null || Document == null) return 0;

            int totalOffset = 0;
            foreach (Block b in Document.Blocks)
            {
                Paragraph p = b as Paragraph;
                if (p != null)
                {
                    if (caret.CompareTo(p.ContentStart) >= 0 && caret.CompareTo(p.ContentEnd) <= 0)
                    {
                        TextRange range = new TextRange(p.ContentStart, caret);
                        totalOffset += range.Text.Length;
                        return totalOffset;
                    }
                    else
                    {
                        TextRange pRange = new TextRange(p.ContentStart, p.ContentEnd);
                        totalOffset += pRange.Text.Length + 1; // +1 for newline
                    }
                }
            }

            return totalOffset;
        }

        private void SetCaretOffset(int targetOffset)
        {
            if (Document == null) return;
            if (targetOffset < 0) targetOffset = 0;

            int currentAcc = 0;
            foreach (Block b in Document.Blocks)
            {
                Paragraph p = b as Paragraph;
                if (p != null)
                {
                    TextRange pRange = new TextRange(p.ContentStart, p.ContentEnd);
                    int pLen = pRange.Text.Length;

                    if (targetOffset <= currentAcc + pLen)
                    {
                        int offsetInPara = targetOffset - currentAcc;
                        int inlineAcc = 0;

                        foreach (Inline inline in p.Inlines)
                        {
                            Run r = inline as Run;
                            if (r != null)
                            {
                                int rLen = r.Text.Length;
                                if (offsetInPara <= inlineAcc + rLen)
                                {
                                    int offsetInRun = offsetInPara - inlineAcc;
                                    TextPointer tp = r.ContentStart.GetPositionAtOffset(offsetInRun, LogicalDirection.Forward);
                                    if (tp != null)
                                    {
                                        CaretPosition = tp;
                                        return;
                                    }
                                }
                                inlineAcc += rLen;
                            }
                        }

                        CaretPosition = p.ContentEnd;
                        return;
                    }

                    currentAcc += pLen + 1;
                }
            }

            CaretPosition = Document.ContentEnd;
        }

        // --- TOKENIZERS ---
        public static List<HighlightToken> TokenizeWikiLinkLine(string line)
        {
            var tokens = new List<HighlightToken>();
            if (string.IsNullOrEmpty(line)) return tokens;

            int i = 0;
            int len = line.Length;

            Brush normalBrush = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4));
            Brush bracketBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x79, 0xc6)); // Pink
            Brush targetBrush = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0));  // Cyan
            Brush pipeBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));    // Gray
            Brush displayBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79)); // Green

            StringBuilder plain = new StringBuilder();

            while (i < len)
            {
                if (i + 1 < len && line[i] == '[' && line[i + 1] == '[')
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int end = line.IndexOf("]]", i + 2);
                    if (end != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "[[", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        string content = line.Substring(i + 2, end - (i + 2));
                        int pipeIndex = content.IndexOf('|');

                        if (pipeIndex != -1)
                        {
                            string target = content.Substring(0, pipeIndex);
                            string display = content.Substring(pipeIndex + 1);

                            if (target.Length > 0)
                                tokens.Add(new HighlightToken { Text = target, Foreground = targetBrush });

                            tokens.Add(new HighlightToken { Text = "|", Foreground = pipeBrush });

                            if (display.Length > 0)
                                tokens.Add(new HighlightToken { Text = display, Foreground = displayBrush });
                        }
                        else
                        {
                            tokens.Add(new HighlightToken { Text = content, Foreground = targetBrush });
                        }

                        tokens.Add(new HighlightToken { Text = "]]", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        i = end + 2;
                        continue;
                    }
                }

                plain.Append(line[i]);
                i++;
            }

            if (plain.Length > 0)
            {
                tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
            }

            return tokens;
        }

        public static List<HighlightToken> TokenizeWikiTextLine(string line)
        {
            var tokens = new List<HighlightToken>();
            if (string.IsNullOrEmpty(line)) return tokens;

            Brush normalBrush = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4));
            Brush headingMarkBrush = new SolidColorBrush(Color.FromRgb(0x56, 0x9c, 0xd6)); // Blue
            Brush headingTitleBrush = new SolidColorBrush(Color.FromRgb(0xdc, 0xdc, 0xaa)); // Yellow
            Brush bracketBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x79, 0xc6)); // Pink
            Brush targetBrush = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0));  // Cyan
            Brush pipeBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));    // Gray
            Brush displayBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79)); // Green
            Brush templateBracketBrush = new SolidColorBrush(Color.FromRgb(0xc5, 0x86, 0xc0)); // Purple
            Brush templateNameBrush = new SolidColorBrush(Color.FromRgb(0xce, 0x91, 0x78)); // Orange
            Brush infoboxHeaderBrush = new SolidColorBrush(Color.FromRgb(0xe5, 0xc0, 0x7b)); // Gold
            Brush paramKeyBrush = new SolidColorBrush(Color.FromRgb(0x9c, 0xdc, 0xfe)); // Light Blue
            Brush paramValBrush = new SolidColorBrush(Color.FromRgb(0xdc, 0xdc, 0xaa)); // Light Yellow
            Brush commentBrush = new SolidColorBrush(Color.FromRgb(0x6a, 0x99, 0x55)); // Dark Green
            Brush tagBrush = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0)); // Teal
            Brush boldBrush = new SolidColorBrush(Color.FromRgb(0xce, 0x91, 0x78)); // Orange
            Brush linkUrlBrush = new SolidColorBrush(Color.FromRgb(0x37, 0x94, 0xff)); // Bright Blue

            string trimmed = line.Trim();

            // 1. Heading == Title ==
            if (trimmed.StartsWith("=") && trimmed.EndsWith("=") && trimmed.Length >= 4)
            {
                int startEqualsCount = 0;
                while (startEqualsCount < line.Length && line[startEqualsCount] == '=') startEqualsCount++;

                int endEqualsCount = 0;
                while (endEqualsCount < line.Length && line[line.Length - 1 - endEqualsCount] == '=') endEqualsCount++;

                int equalsCount = Math.Min(startEqualsCount, endEqualsCount);
                string startMarks = line.Substring(0, equalsCount);
                string middle = line.Substring(equalsCount, line.Length - 2 * equalsCount);
                string endMarks = line.Substring(line.Length - equalsCount);

                tokens.Add(new HighlightToken { Text = startMarks, Foreground = headingMarkBrush, Weight = FontWeights.Bold });
                tokens.Add(new HighlightToken { Text = middle, Foreground = headingTitleBrush, Weight = FontWeights.Bold });
                tokens.Add(new HighlightToken { Text = endMarks, Foreground = headingMarkBrush, Weight = FontWeights.Bold });
                return tokens;
            }

            // 2. Infobox / Template Parameter Line: "| key = value"
            if (trimmed.StartsWith("|") && trimmed.Contains("="))
            {
                int pipePos = line.IndexOf('|');
                if (pipePos > 0)
                {
                    tokens.Add(new HighlightToken { Text = line.Substring(0, pipePos), Foreground = normalBrush });
                }
                tokens.Add(new HighlightToken { Text = "|", Foreground = templateBracketBrush, Weight = FontWeights.Bold });

                string rest = line.Substring(pipePos + 1);
                int eqPos = rest.IndexOf('=');

                string keyPart = rest.Substring(0, eqPos);
                string valPart = rest.Substring(eqPos + 1);

                tokens.Add(new HighlightToken { Text = keyPart, Foreground = paramKeyBrush, Weight = FontWeights.SemiBold });
                tokens.Add(new HighlightToken { Text = "=", Foreground = pipeBrush });

                if (!string.IsNullOrEmpty(valPart))
                {
                    List<HighlightToken> valTokens = TokenizeSubContent(valPart, paramValBrush);
                    foreach (var vt in valTokens) tokens.Add(vt);
                }
                return tokens;
            }

            int i = 0;
            int len = line.Length;
            StringBuilder plain = new StringBuilder();

            while (i < len)
            {
                // Comment <!-- ... -->
                if (i + 3 < len && line.Substring(i, 4) == "<!--")
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endComment = line.IndexOf("-->", i + 4);
                    if (endComment != -1)
                    {
                        string commentText = line.Substring(i, endComment + 3 - i);
                        tokens.Add(new HighlightToken { Text = commentText, Foreground = commentBrush, Style = FontStyles.Italic });
                        i = endComment + 3;
                        continue;
                    }
                }

                // Template {{ ... }} or Multiline Template Header {{Infobox ...
                if (i + 1 < len && line[i] == '{' && line[i + 1] == '{')
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    tokens.Add(new HighlightToken { Text = "{{", Foreground = templateBracketBrush, Weight = FontWeights.Bold });
                    i += 2;

                    int endTpl = line.IndexOf("}}", i);
                    if (endTpl != -1)
                    {
                        string tplContent = line.Substring(i, endTpl - i);
                        int firstPipe = tplContent.IndexOf('|');

                        if (firstPipe != -1)
                        {
                            string tplName = tplContent.Substring(0, firstPipe);
                            string tplArgs = tplContent.Substring(firstPipe);

                            Brush nameBrush = tplName.Trim().StartsWith("Infobox", StringComparison.OrdinalIgnoreCase)
                                ? infoboxHeaderBrush
                                : templateNameBrush;

                            tokens.Add(new HighlightToken { Text = tplName, Foreground = nameBrush, Weight = FontWeights.Bold });

                            List<HighlightToken> argTokens = TokenizeTemplateArgs(tplArgs);
                            foreach (var at in argTokens) tokens.Add(at);
                        }
                        else
                        {
                            Brush nameBrush = tplContent.Trim().StartsWith("Infobox", StringComparison.OrdinalIgnoreCase)
                                ? infoboxHeaderBrush
                                : templateNameBrush;
                            tokens.Add(new HighlightToken { Text = tplContent, Foreground = nameBrush, Weight = FontWeights.Bold });
                        }

                        tokens.Add(new HighlightToken { Text = "}}", Foreground = templateBracketBrush, Weight = FontWeights.Bold });
                        i = endTpl + 2;
                        continue;
                    }
                    else
                    {
                        string restLine = line.Substring(i);
                        int firstPipe = restLine.IndexOf('|');
                        if (firstPipe != -1)
                        {
                            string tplName = restLine.Substring(0, firstPipe);
                            string tplArgs = restLine.Substring(firstPipe);

                            Brush nameBrush = tplName.Trim().StartsWith("Infobox", StringComparison.OrdinalIgnoreCase)
                                ? infoboxHeaderBrush
                                : templateNameBrush;

                            tokens.Add(new HighlightToken { Text = tplName, Foreground = nameBrush, Weight = FontWeights.Bold });

                            List<HighlightToken> argTokens = TokenizeTemplateArgs(tplArgs);
                            foreach (var at in argTokens) tokens.Add(at);
                        }
                        else
                        {
                            Brush nameBrush = restLine.Trim().StartsWith("Infobox", StringComparison.OrdinalIgnoreCase)
                                ? infoboxHeaderBrush
                                : templateNameBrush;
                            tokens.Add(new HighlightToken { Text = restLine, Foreground = nameBrush, Weight = FontWeights.Bold });
                        }
                        i = len;
                        continue;
                    }
                }

                // Template End }}
                if (i + 1 < len && line[i] == '}' && line[i + 1] == '}')
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    tokens.Add(new HighlightToken { Text = "}}", Foreground = templateBracketBrush, Weight = FontWeights.Bold });
                    i += 2;
                    continue;
                }

                // WikiLink [[ ... ]]
                if (i + 1 < len && line[i] == '[' && line[i + 1] == '[')
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endLink = line.IndexOf("]]", i + 2);
                    if (endLink != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "[[", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        string content = line.Substring(i + 2, endLink - (i + 2));
                        int pipeIndex = content.IndexOf('|');

                        if (pipeIndex != -1)
                        {
                            string target = content.Substring(0, pipeIndex);
                            string display = content.Substring(pipeIndex + 1);
                            if (target.Length > 0) tokens.Add(new HighlightToken { Text = target, Foreground = targetBrush });
                            tokens.Add(new HighlightToken { Text = "|", Foreground = pipeBrush });
                            if (display.Length > 0) tokens.Add(new HighlightToken { Text = display, Foreground = displayBrush });
                        }
                        else
                        {
                            tokens.Add(new HighlightToken { Text = content, Foreground = targetBrush });
                        }

                        tokens.Add(new HighlightToken { Text = "]]", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        i = endLink + 2;
                        continue;
                    }
                }

                // Bold ''' ... '''
                if (i + 2 < len && line.Substring(i, 3) == "'''")
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endBold = line.IndexOf("'''", i + 3);
                    if (endBold != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "'''", Foreground = pipeBrush });
                        string boldText = line.Substring(i + 3, endBold - (i + 3));
                        tokens.Add(new HighlightToken { Text = boldText, Foreground = boldBrush, Weight = FontWeights.Bold });
                        tokens.Add(new HighlightToken { Text = "'''", Foreground = pipeBrush });
                        i = endBold + 3;
                        continue;
                    }
                }

                // Italic '' ... ''
                if (i + 1 < len && line.Substring(i, 2) == "''")
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endItalic = line.IndexOf("''", i + 2);
                    if (endItalic != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "''", Foreground = pipeBrush });
                        string italicText = line.Substring(i + 2, endItalic - (i + 2));
                        tokens.Add(new HighlightToken { Text = italicText, Foreground = normalBrush, Style = FontStyles.Italic });
                        tokens.Add(new HighlightToken { Text = "''", Foreground = pipeBrush });
                        i = endItalic + 2;
                        continue;
                    }
                }

                // XML/HTML Tag <tag> or </tag>
                if (line[i] == '<' && i + 1 < len && (char.IsLetter(line[i + 1]) || line[i + 1] == '/'))
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endTag = line.IndexOf('>', i + 1);
                    if (endTag != -1)
                    {
                        string tagContent = line.Substring(i, endTag - i + 1);
                        tokens.Add(new HighlightToken { Text = tagContent, Foreground = tagBrush });
                        i = endTag + 1;
                        continue;
                    }
                }

                // External Link [http...]
                if (line[i] == '[' && (i + 7 < len) && (line.Substring(i + 1, 7) == "http://" || line.Substring(i + 1, 8) == "https://"))
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
                        plain.Clear();
                    }

                    int endExt = line.IndexOf(']', i + 1);
                    if (endExt != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "[", Foreground = pipeBrush });
                        string extContent = line.Substring(i + 1, endExt - (i + 1));
                        tokens.Add(new HighlightToken { Text = extContent, Foreground = linkUrlBrush });
                        tokens.Add(new HighlightToken { Text = "]", Foreground = pipeBrush });
                        i = endExt + 1;
                        continue;
                    }
                }

                plain.Append(line[i]);
                i++;
            }

            if (plain.Length > 0)
            {
                tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = normalBrush });
            }

            return tokens;
        }

        private static List<HighlightToken> TokenizeSubContent(string content, Brush defaultBrush)
        {
            var tokens = new List<HighlightToken>();
            if (string.IsNullOrEmpty(content)) return tokens;

            Brush bracketBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x79, 0xc6)); // Pink
            Brush targetBrush = new SolidColorBrush(Color.FromRgb(0x4e, 0xc9, 0xb0));  // Cyan
            Brush pipeBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));    // Gray
            Brush displayBrush = new SolidColorBrush(Color.FromRgb(0x98, 0xc3, 0x79)); // Green

            int i = 0;
            int len = content.Length;
            StringBuilder plain = new StringBuilder();

            while (i < len)
            {
                if (i + 1 < len && content[i] == '[' && content[i + 1] == '[')
                {
                    if (plain.Length > 0)
                    {
                        tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = defaultBrush });
                        plain.Clear();
                    }

                    int endLink = content.IndexOf("]]", i + 2);
                    if (endLink != -1)
                    {
                        tokens.Add(new HighlightToken { Text = "[[", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        string inner = content.Substring(i + 2, endLink - (i + 2));
                        int pipeIndex = inner.IndexOf('|');

                        if (pipeIndex != -1)
                        {
                            string target = inner.Substring(0, pipeIndex);
                            string display = inner.Substring(pipeIndex + 1);
                            if (target.Length > 0) tokens.Add(new HighlightToken { Text = target, Foreground = targetBrush });
                            tokens.Add(new HighlightToken { Text = "|", Foreground = pipeBrush });
                            if (display.Length > 0) tokens.Add(new HighlightToken { Text = display, Foreground = displayBrush });
                        }
                        else
                        {
                            tokens.Add(new HighlightToken { Text = inner, Foreground = targetBrush });
                        }

                        tokens.Add(new HighlightToken { Text = "]]", Foreground = bracketBrush, Weight = FontWeights.Bold });
                        i = endLink + 2;
                        continue;
                    }
                }

                plain.Append(content[i]);
                i++;
            }

            if (plain.Length > 0)
            {
                tokens.Add(new HighlightToken { Text = plain.ToString(), Foreground = defaultBrush });
            }

            return tokens;
        }

        private static List<HighlightToken> TokenizeTemplateArgs(string tplArgs)
        {
            var tokens = new List<HighlightToken>();
            if (string.IsNullOrEmpty(tplArgs)) return tokens;

            Brush pipeBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            Brush paramKeyBrush = new SolidColorBrush(Color.FromRgb(0x9c, 0xdc, 0xfe));
            Brush normalBrush = new SolidColorBrush(Color.FromRgb(0xd4, 0xd4, 0xd4));

            int argPos = 0;
            StringBuilder argBuf = new StringBuilder();

            while (argPos < tplArgs.Length)
            {
                if (tplArgs[argPos] == '|')
                {
                    if (argBuf.Length > 0)
                    {
                        string argText = argBuf.ToString();
                        int eq = argText.IndexOf('=');
                        if (eq != -1)
                        {
                            tokens.Add(new HighlightToken { Text = argText.Substring(0, eq), Foreground = paramKeyBrush, Weight = FontWeights.SemiBold });
                            tokens.Add(new HighlightToken { Text = "=", Foreground = pipeBrush });
                            tokens.Add(new HighlightToken { Text = argText.Substring(eq + 1), Foreground = normalBrush });
                        }
                        else
                        {
                            tokens.Add(new HighlightToken { Text = argText, Foreground = normalBrush });
                        }
                        argBuf.Clear();
                    }
                    tokens.Add(new HighlightToken { Text = "|", Foreground = pipeBrush });
                }
                else
                {
                    argBuf.Append(tplArgs[argPos]);
                }
                argPos++;
            }

            if (argBuf.Length > 0)
            {
                string argText = argBuf.ToString();
                int eq = argText.IndexOf('=');
                if (eq != -1)
                {
                    tokens.Add(new HighlightToken { Text = argText.Substring(0, eq), Foreground = paramKeyBrush, Weight = FontWeights.SemiBold });
                    tokens.Add(new HighlightToken { Text = "=", Foreground = pipeBrush });
                    tokens.Add(new HighlightToken { Text = argText.Substring(eq + 1), Foreground = normalBrush });
                }
                else
                {
                    tokens.Add(new HighlightToken { Text = argText, Foreground = normalBrush });
                }
            }

            return tokens;
        }
    }
}
