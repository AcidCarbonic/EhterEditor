using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using EtherEditorNative.Backend;
using Microsoft.Win32;

namespace EtherEditorNative.Views
{
    public partial class TranslateView : UserControl
    {
        public class TextMapRow
        {
            public string Id { get; set; }
            public string SourceText { get; set; }
            public string TargetText { get; set; }
        }

        public class TextMapItem
        {
            public string Id { get; set; }
            public string SourceText { get; set; }
            public string TargetText { get; set; }
            public string GameId { get; set; }
        }

        private readonly DatabaseService _dbService;
        private readonly LogicService _logicService;
        private readonly ProjectService _projectService;
        private List<TextMapRow> _currentRows;
        private string _currentFilePath = "";
        private string _currentGame = "hsr";
        private bool _isPreviewActive = false;

        public class TabItemData
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public Border TabBorder { get; set; }
            public System.Windows.Shapes.Path IconPath { get; set; }
            public TextBlock TitleBlock { get; set; }
            public System.Windows.Shapes.Path ClosePath { get; set; }
        }

        public class TabSessionItem
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public bool IsActive { get; set; }
        }

        public class TabSessionContainer
        {
            public int ActiveTabId { get; set; }
            public int TabCounter { get; set; }
            public List<TabSessionItem> Tabs { get; set; }

            public TabSessionContainer()
            {
                Tabs = new List<TabSessionItem>();
            }
        }

        private DispatcherTimer _healthStatsTimer;
        private TimeSpan _lastCpuTime;
        private DateTime _lastCpuCheckTime;
        private int _tabCounter = 1;
        private List<TabItemData> _tabList = new List<TabItemData>();
        private TabItemData _activeTab = null;

        public TranslateView()
        {
            InitializeComponent();
            string projectRoot = GetProjectRootDir();
            _dbService = new DatabaseService(projectRoot);
            _logicService = new LogicService(projectRoot);
            _projectService = new ProjectService(projectRoot);

            _currentRows = new List<TextMapRow>();
            LoadRealDataFromDatabase();
            InitDefaultWorkspaceSample();
        }

        private string GetProjectRootDir()
        {
            string dir = Path.GetDirectoryName(typeof(TranslateView).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;

            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "EtherEditorNative.csproj")) || 
                    File.Exists(Path.Combine(dir, "db", "bot_data.db")) ||
                    Directory.Exists(Path.Combine(dir, "Views")))
                {
                    return dir;
                }
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null)
            {
                TxtEditorContent.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TxtEditorContent_ScrollChanged));
                TxtEditorContent.SizeChanged += (s, ev) => RefreshLineNumbersAsync(TxtEditorContent, TxtLineNumbers);
                RefreshLineNumbersAsync(TxtEditorContent, TxtLineNumbers);
            }
            if (TxtSourceContent != null)
            {
                TxtSourceContent.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TxtSourceContent_ScrollChanged));
                TxtSourceContent.SizeChanged += (s, ev) => RefreshLineNumbersAsync(TxtSourceContent, TxtSourceLineNumbers);
                RefreshLineNumbersAsync(TxtSourceContent, TxtSourceLineNumbers);
            }
            if (TxtCompareContent != null)
            {
                TxtCompareContent.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(TxtCompareContent_ScrollChanged));
                TxtCompareContent.SizeChanged += (s, ev) => RefreshLineNumbersAsync(TxtCompareContent, TxtCompareLineNumbers);
                RefreshLineNumbersAsync(TxtCompareContent, TxtCompareLineNumbers);
            }

            StartHealthStatsMonitoring();
            InitTabManager();
        }

        private void TxtEditorContent_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (TxtLineNumbersSv != null)
            {
                TxtLineNumbersSv.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void TxtSourceContent_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (TxtSourceLineNumbersSv != null)
            {
                TxtSourceLineNumbersSv.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void TxtCompareContent_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (TxtCompareLineNumbersSv != null)
            {
                TxtCompareLineNumbersSv.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void RefreshLineNumbersAsync(WikitextRichTextBox editor, TextBlock lineNumbersBlock)
        {
            UpdateLineNumbers(editor, lineNumbersBlock);
            try
            {
                Dispatcher.BeginInvoke((Action)(() => UpdateLineNumbers(editor, lineNumbersBlock)), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
        }

        private void UpdateLineNumbers(WikitextRichTextBox editor, TextBlock lineNumbersBlock)
        {
            if (editor == null || lineNumbersBlock == null) return;

            int visualLineCount = editor.LineCount;
            if (visualLineCount <= 0) visualLineCount = 1;

            StringBuilder sb = new StringBuilder();
            for (int v = 1; v <= visualLineCount; v++)
            {
                sb.AppendLine(v.ToString());
            }

            lineNumbersBlock.Text = sb.ToString().TrimEnd('\r', '\n');
        }

        private void RefreshLineNumbersAsync(TextBox textBox, TextBlock lineNumbersBlock)
        {
            UpdateLineNumbers(textBox, lineNumbersBlock);
            try
            {
                Dispatcher.BeginInvoke((Action)(() => UpdateLineNumbers(textBox, lineNumbersBlock)), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch { }
        }

        private void UpdateLineNumbers(TextBox textBox, TextBlock lineNumbersBlock)
        {
            if (textBox == null || lineNumbersBlock == null) return;

            string text = textBox.Text ?? "";
            int visualLineCount = textBox.LineCount;

            if (visualLineCount <= 0)
            {
                string[] logicalLines = text.Split('\n');
                visualLineCount = logicalLines.Length;
            }
            if (visualLineCount <= 0) visualLineCount = 1;

            StringBuilder sb = new StringBuilder();
            int currentLogicalLine = 0;

            for (int v = 0; v < visualLineCount; v++)
            {
                int charIndex = -1;
                try
                {
                    charIndex = textBox.GetCharacterIndexFromLineIndex(v);
                }
                catch
                {
                    charIndex = -1;
                }

                bool isLogicalStart = false;
                if (v == 0)
                {
                    isLogicalStart = true;
                }
                else if (charIndex > 0 && charIndex <= text.Length)
                {
                    if (text[charIndex - 1] == '\n')
                    {
                        isLogicalStart = true;
                    }
                }

                if (isLogicalStart)
                {
                    currentLogicalLine++;
                    sb.AppendLine(currentLogicalLine.ToString());
                }
                else
                {
                    sb.AppendLine();
                }
            }

            lineNumbersBlock.Text = sb.ToString().TrimEnd('\r', '\n');
        }

        private void InitDefaultWorkspaceSample()
        {
            if (TxtEditorContent != null && string.IsNullOrEmpty(TxtEditorContent.Text))
            {
                TxtEditorContent.Text = "== Tổng quan ==\n" +
                    "{{NhânVật_Infobox\n" +
                    "|tên = March 7th\n" +
                    "|hình = March 7th.png\n" +
                    "|hiếm = 4\n" +
                    "|vận_mệnh = Bảo Vệ\n" +
                    "|thuộc_tính = Băng\n" +
                    "}}\n\n" +
                    "'''March 7th''' là một thiếu nữ hoạt bát, nhí nhảnh trong [[Honkai: Star Rail]].\n" +
                    "Cô mang theo một chiếc máy ảnh KTS và luôn tìm kiếm ký ức quá khứ của mình.";
            }

            if (TxtSourceContent != null && string.IsNullOrEmpty(TxtSourceContent.Text))
            {
                TxtSourceContent.Text = "== Overview ==\n" +
                    "{{Character_Infobox\n" +
                    "|name = March 7th\n" +
                    "|image = March 7th.png\n" +
                    "|rarity = 4\n" +
                    "|path = Preservation\n" +
                    "|element = Ice\n" +
                    "}}\n\n" +
                    "'''March 7th''' is a lively girl aboard the [[Honkai: Star Rail|Astral Express]].\n" +
                    "She carries a digital camera and is always looking for her past memories.";
            }
        }

        private void LoadRealDataFromDatabase(string query = "", string gameId = "hsr")
        {
            try
            {
                if (_dbService.IsDatabaseAvailable())
                {
                    var result = _dbService.SearchGameDataPaginated(gameId, query, "All", false, 1, 100);
                    _currentRows.Clear();

                    if (result != null && result.Items != null)
                    {
                        foreach (var item in result.Items)
                        {
                            _currentRows.Add(new TextMapRow
                            {
                                Id = item.ItemId,
                                SourceText = !string.IsNullOrEmpty(item.NameEn) ? item.NameEn : item.DescriptionEn,
                                TargetText = !string.IsNullOrEmpty(item.NameVi) ? item.NameVi : item.DescriptionVi
                            });
                        }
                    }

                    if (DgTextMap != null)
                    {
                        DgTextMap.ItemsSource = null;
                        DgTextMap.ItemsSource = _currentRows;
                    }
                    if (TxtStatus != null)
                    {
                        TxtStatus.Text = string.Format("Đã nạp {0}/{1} câu từ CSDL SQLite bot_data.db ({2})", 
                            _currentRows.Count, result != null ? result.TotalCount : 0, gameId.ToUpper());
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("TranslateView Data Load Error: " + ex.Message);
            }

            LoadSampleFallback();
        }

        private void LoadSampleFallback()
        {
            _currentRows = new List<TextMapRow>
            {
                new TextMapRow { Id = "30001001", SourceText = "Welcome to Astral Express!", TargetText = "Chào mừng bạn đến với Đội Tàu Astral!" },
                new TextMapRow { Id = "30001002", SourceText = "March 7th: Let's take a photo together!", TargetText = "March 7th: Hãy cùng chụp một bức ảnh nào!" },
                new TextMapRow { Id = "30001003", SourceText = "Dan Heng: Spear of the Cold Cloud.", TargetText = "Đan Hằng: Thương Của Mây Lạnh." },
                new TextMapRow { Id = "30001004", SourceText = "Kafka: Listen to me...", TargetText = "Kafka: Hãy nghe tôi nói..." },
                new TextMapRow { Id = "30001005", SourceText = "Silver Wolf: Game over!", TargetText = "Silver Wolf: Trò chơi kết thúc!" }
            };

            if (DgTextMap != null) DgTextMap.ItemsSource = _currentRows;
            if (TxtStatus != null) TxtStatus.Text = "";
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            PerformSearch();
        }

        private void TxtSearchInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformSearch();
            }
        }

        private void PerformSearch()
        {
            string query = "";
            string selectedGame = GetSelectedGameId();

            if (!string.IsNullOrEmpty(query))
            {
                if (DgTextMap != null) DgTextMap.Visibility = Visibility.Visible;
                if (GridEditPane != null) GridEditPane.Visibility = Visibility.Collapsed;
            }

            LoadRealDataFromDatabase(query, selectedGame);
        }

        private string GetSelectedGameId()
        {
            string selectedGame = "hsr";
            if (CmbGameSelect != null)
            {
                var item = CmbGameSelect.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                {
                    selectedGame = item.Tag.ToString();
                }
            }
            return selectedGame;
        }

        private bool _isSidebarCollapsed = false;

        private void CmbGameSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                PerformSearch();
            }
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = !_isSidebarCollapsed;
            if (_isSidebarCollapsed)
            {
                if (SidebarColumn != null) SidebarColumn.Width = new GridLength(34);
                if (TxtSidebarTitle != null) TxtSidebarTitle.Visibility = Visibility.Collapsed;
                if (SidebarScrollViewer != null) SidebarScrollViewer.Visibility = Visibility.Collapsed;
                if (BorderSidebarHeader != null) BorderSidebarHeader.Padding = new Thickness(4, 12, 4, 12);
                if (PathToggleSidebar != null)
                {
                    PathToggleSidebar.Data = Geometry.Parse("M 6 5 L 12 12 L 6 19 M 12 5 L 18 12 L 12 19");
                }
                if (BtnToggleSidebar != null)
                {
                    BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Center;
                    BtnToggleSidebar.Width = 26;
                    BtnToggleSidebar.Height = 24;
                    ToolTipService.SetToolTip(BtnToggleSidebar, "Mở rộng quy trình xuất bản");
                }
            }
            else
            {
                if (SidebarColumn != null) SidebarColumn.Width = new GridLength(280);
                if (TxtSidebarTitle != null) TxtSidebarTitle.Visibility = Visibility.Visible;
                if (SidebarScrollViewer != null) SidebarScrollViewer.Visibility = Visibility.Visible;
                if (BorderSidebarHeader != null) BorderSidebarHeader.Padding = new Thickness(14, 12, 12, 12);
                if (PathToggleSidebar != null)
                {
                    PathToggleSidebar.Data = Geometry.Parse("M 12 5 L 6 12 L 12 19 M 18 5 L 12 12 L 18 19");
                }
                if (BtnToggleSidebar != null)
                {
                    BtnToggleSidebar.HorizontalAlignment = HorizontalAlignment.Right;
                    BtnToggleSidebar.Width = 26;
                    BtnToggleSidebar.Height = 24;
                    ToolTipService.SetToolTip(BtnToggleSidebar, "Thu gọn quy trình xuất bản");
                }
            }
        }


        // --- EDITOR EVENT HANDLERS & LINE NUMBERS ---
        private void TxtEditorContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtEditorContent == null) return;

            string text = TxtEditorContent.Text ?? "";

            if (_activeTab != null)
            {
                _activeTab.Content = text;
            }

            // 1. Line numbers
            RefreshLineNumbersAsync(TxtEditorContent, TxtLineNumbers);

            // 2. Sync to compare mode text box if active
            if (TxtCompareContent != null && TxtCompareContent.Text != text)
            {
                TxtCompareContent.Text = text;
            }

            // 3. Character count
            if (TxtCharCount != null)
            {
                TxtCharCount.Text = string.Format("{0} ký tự", text.Length);
            }

            SaveTabSession();
        }

        private void TxtEditorContent_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || TxtCursorPos == null) return;

            try
            {
                int caretIndex = TxtEditorContent.CaretIndex;
                int lineIndex = TxtEditorContent.GetLineIndexFromCharacterIndex(caretIndex);
                int lineStartCharIndex = TxtEditorContent.GetCharacterIndexFromLineIndex(lineIndex);
                int colIndex = caretIndex - lineStartCharIndex + 1;

                TxtCursorPos.Text = string.Format("Ln {0}, Col {1}", lineIndex + 1, colIndex);
            }
            catch
            {
                TxtCursorPos.Text = "Ln 1, Col 1";
            }
        }

        private void TxtSourceContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSourceContent == null) return;
            RefreshLineNumbersAsync(TxtSourceContent, TxtSourceLineNumbers);
        }

        private void TxtCompareContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtCompareContent == null) return;

            string text = TxtCompareContent.Text ?? "";
            RefreshLineNumbersAsync(TxtCompareContent, TxtCompareLineNumbers);

            // Sync back to main editor
            if (TxtEditorContent != null && TxtEditorContent.Text != text)
            {
                TxtEditorContent.Text = text;
            }
        }

        private void TxtCompareContent_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (TxtCompareContent == null || TxtCursorPos == null) return;
            try
            {
                int caretIndex = TxtCompareContent.CaretIndex;
                int lineIndex = TxtCompareContent.GetLineIndexFromCharacterIndex(caretIndex);
                int lineStartCharIndex = TxtCompareContent.GetCharacterIndexFromLineIndex(lineIndex);
                int colIndex = caretIndex - lineStartCharIndex + 1;

                TxtCursorPos.Text = string.Format("Ln {0}, Col {1}", lineIndex + 1, colIndex);
            }
            catch { }
        }

        // --- MODE SWITCHER (EDIT, COMPARE, DATAGRID - MATCHING INDEX.JS LOGIC) ---
        private void CmbViewMode_Loaded(object sender, RoutedEventArgs e)
        {
            if (CmbViewMode != null)
            {
                var popup = CmbViewMode.Template.FindName("PART_Popup", CmbViewMode) as System.Windows.Controls.Primitives.Popup;
                if (popup != null)
                {
                    popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
                    {
                        return new System.Windows.Controls.Primitives.CustomPopupPlacement[]
                        {
                            new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0, -popupSize.Height - 4), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                        };
                    };
                }
            }
        }

        private void CmbViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbViewMode == null) return;

            var selectedItem = CmbViewMode.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Tag != null)
            {
                string mode = selectedItem.Tag.ToString();
                SwitchEditorMode(mode);
            }
        }

        private bool _isWikiLinkMode = true;

        private void BtnSyntaxModeToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isWikiLinkMode = !_isWikiLinkMode;
            if (_isWikiLinkMode)
            {
                if (PathSyntaxIcon != null) PathSyntaxIcon.Data = Geometry.Parse("M3.9,12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76,0-5,2.24-5,5s2.24,5 5,5h4v-1.9H7c-1.71,0-3.1-1.39-3.1-3.1z M8,13h8v-2H8v2z M17,7h-4v1.9h4c1.71,0 3.1,1.39 3.1,3.1s-1.39,3.1-3.1,3.1h-4V17h4c2.76,0 5-2.24 5-5s-2.24-5-5-5z");
                if (TxtSyntaxMode != null) TxtSyntaxMode.Text = "WikiLink";
                if (TxtEditorContent != null) TxtEditorContent.SyntaxMode = "WikiLink";
            }
            else
            {
                if (PathSyntaxIcon != null) PathSyntaxIcon.Data = Geometry.Parse("M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z");
                if (TxtSyntaxMode != null) TxtSyntaxMode.Text = "WikiText";
                if (TxtEditorContent != null) TxtEditorContent.SyntaxMode = "WikiText";
            }
        }

        private void SwitchEditorMode(string mode)
        {
            // Hide preview if active
            _isPreviewActive = false;
            if (GridPreviewPane != null) GridPreviewPane.Visibility = Visibility.Collapsed;
            if (TxtPreviewBtnLabel != null) TxtPreviewBtnLabel.Text = "Xem trước";

            if (mode == "compare")
            {
                if (GridEditPane != null) GridEditPane.Visibility = Visibility.Collapsed;
                if (DgTextMap != null) DgTextMap.Visibility = Visibility.Collapsed;
                if (GridComparePane != null) GridComparePane.Visibility = Visibility.Visible;

                // Sync text
                if (TxtCompareContent != null && TxtEditorContent != null)
                {
                    TxtCompareContent.Text = TxtEditorContent.Text;
                }
            }
            else if (mode == "datagrid")
            {
                if (GridEditPane != null) GridEditPane.Visibility = Visibility.Collapsed;
                if (GridComparePane != null) GridComparePane.Visibility = Visibility.Collapsed;
                if (DgTextMap != null) DgTextMap.Visibility = Visibility.Visible;
            }
            else // "edit" mode default
            {
                if (GridComparePane != null) GridComparePane.Visibility = Visibility.Collapsed;
                if (DgTextMap != null) DgTextMap.Visibility = Visibility.Collapsed;
                if (GridEditPane != null) GridEditPane.Visibility = Visibility.Visible;
            }
        }

        private void BtnPreviewToggle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isPreviewActive = !_isPreviewActive;
            if (_isPreviewActive)
            {
                if (GridEditPane != null) GridEditPane.Visibility = Visibility.Collapsed;
                if (GridComparePane != null) GridComparePane.Visibility = Visibility.Collapsed;
                if (DgTextMap != null) DgTextMap.Visibility = Visibility.Collapsed;
                if (GridPreviewPane != null) GridPreviewPane.Visibility = Visibility.Visible;

                if (TxtPreviewContent != null && TxtEditorContent != null)
                {
                    TxtPreviewContent.Text = TxtEditorContent.Text;
                }
                if (TxtPreviewBtnLabel != null) TxtPreviewBtnLabel.Text = "Soạn thảo";
            }
            else
            {
                if (TxtPreviewBtnLabel != null) TxtPreviewBtnLabel.Text = "Xem trước";
                if (CmbViewMode != null)
                {
                    var item = CmbViewMode.SelectedItem as ComboBoxItem;
                    string mode = item != null && item.Tag != null ? item.Tag.ToString() : "edit";
                    SwitchEditorMode(mode);
                }
            }
        }

        // --- RIBBON ACTIONS ---
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null && TxtEditorContent.CanUndo)
            {
                TxtEditorContent.Undo();
            }
        }

        private void BtnCut_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null)
            {
                TxtEditorContent.Cut();
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null)
            {
                TxtEditorContent.Copy();
            }
        }

        private void BtnPaste_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null)
            {
                TxtEditorContent.Paste();
            }
        }

        private void BtnToolLink_Click(object sender, RoutedEventArgs e)
        {
            InsertOrWrapText("[[", "]]", "Tên_Bài_Viết");
        }

        private void BtnToolPipe_Click(object sender, RoutedEventArgs e)
        {
            InsertOrWrapText("[[", "|Tên_Hiển_Thị]]", "Tên_Bài_Viết");
        }

        private void BtnToolBold_Click(object sender, RoutedEventArgs e)
        {
            InsertOrWrapText("'''", "'''", "Văn bản in đậm");
        }

        private void BtnToolItalic_Click(object sender, RoutedEventArgs e)
        {
            InsertOrWrapText("''", "''", "Văn bản in nghiêng");
        }

        // --- MASTER UNIFIED ASSISTANT CONTROLLER (7 TABS: WIKILINK, REF, INTERWIKI, INFOBOX, TEMPLATE, PROSE, AUTO) ---
        private string _currentAssistantTab = "wikilink";
        private List<string> _interwikiLanguagesList = new List<string>();

        public void ShowModalAssistant(string targetTab = "wikilink")
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalAssistant != null) BorderModalAssistant.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;

            UpdateAssistantDynamicTabs();
            SwitchAssistantTab(targetTab);
        }

        private void BtnAssistant_Click(object sender, RoutedEventArgs e)
        {
            ShowModalAssistant("wikilink");
        }

        private void BtnTabAssistant_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                SwitchAssistantTab(btn.Tag.ToString());
            }
        }

        private void BtnRefreshAssistant_Click(object sender, RoutedEventArgs e)
        {
            UpdateAssistantDynamicTabs();
            SwitchAssistantTab(_currentAssistantTab);
        }

        private void UpdateAssistantDynamicTabs()
        {
            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            int countWikilinks = Regex.Matches(text, @"\[\[([\s\S]*?)\]\]").Count;
            int countRefs = Regex.Matches(text, @"<ref(?:\s+[^>]*)?>.*?<\/ref>|<ref(?:\s+[^>]*)?\/>", RegexOptions.IgnoreCase).Count;
            int countInterwiki = Regex.Matches(text, @"\[\[[a-z]{2,3}(?:-[a-z]+)?:[^\]]+\]\]", RegexOptions.IgnoreCase).Count + Regex.Matches(text, @"\{\{(?:Otherlang|Other languages)", RegexOptions.IgnoreCase).Count;
            int countInfobox = Regex.Matches(text, @"^\s*\|\s*([^=\r\n]+)=", RegexOptions.Multiline).Count;
            int countTemplates = Regex.Matches(text, @"\{\{([^\}\r\n]+)\}\}").Count;

            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int countProse = 0;
            int countAuto = 0;
            foreach (var l in lines)
            {
                string tr = l.Trim();
                if (string.IsNullOrEmpty(tr)) continue;
                if (!Regex.IsMatch(tr, @"^[\{\}\!\|\=\#\*\:\;\<]")) countProse++;
                if ((tr.StartsWith("==") && tr.EndsWith("==")) || tr.Contains("'''") || tr.Contains("\"") || tr.Contains("“") || tr.Contains("{{Dialogue") || tr.Contains("{{Vo"))
                {
                    countAuto++;
                }
            }

            int visibleCount = 0;
            if (BtnTabWikilink != null) { BtnTabWikilink.Visibility = countWikilinks > 0 ? Visibility.Visible : Visibility.Collapsed; if (countWikilinks > 0) visibleCount++; }
            if (BtnTabRef != null) { BtnTabRef.Visibility = countRefs > 0 ? Visibility.Visible : Visibility.Collapsed; if (countRefs > 0) visibleCount++; }
            if (BtnTabInterwiki != null) { BtnTabInterwiki.Visibility = countInterwiki > 0 ? Visibility.Visible : Visibility.Collapsed; if (countInterwiki > 0) visibleCount++; }
            if (BtnTabInfobox != null) { BtnTabInfobox.Visibility = countInfobox > 0 ? Visibility.Visible : Visibility.Collapsed; if (countInfobox > 0) visibleCount++; }
            if (BtnTabTemplate != null) { BtnTabTemplate.Visibility = countTemplates > 0 ? Visibility.Visible : Visibility.Collapsed; if (countTemplates > 0) visibleCount++; }
            if (BtnTabProse != null) { BtnTabProse.Visibility = countProse > 0 ? Visibility.Visible : Visibility.Collapsed; if (countProse > 0) visibleCount++; }
            if (BtnTabAuto != null) { BtnTabAuto.Visibility = countAuto > 0 ? Visibility.Visible : Visibility.Collapsed; if (countAuto > 0) visibleCount++; }

            if (visibleCount == 0 && BtnTabWikilink != null)
            {
                BtnTabWikilink.Visibility = Visibility.Visible;
            }
        }

        private void SwitchAssistantTab(string tabName)
        {
            if (string.IsNullOrEmpty(tabName)) tabName = "wikilink";
            _currentAssistantTab = tabName;

            var tabs = new Dictionary<string, Button>
            {
                { "wikilink", BtnTabWikilink },
                { "ref", BtnTabRef },
                { "interwiki", BtnTabInterwiki },
                { "infobox", BtnTabInfobox },
                { "template", BtnTabTemplate },
                { "prose", BtnTabProse },
                { "auto", BtnTabAuto }
            };

            foreach (var kvp in tabs)
            {
                if (kvp.Value == null) continue;
                bool isActive = kvp.Key.Equals(tabName, StringComparison.OrdinalIgnoreCase);
                kvp.Value.BorderBrush = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007acc")) : Brushes.Transparent;
                kvp.Value.Foreground = isActive ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"));
                kvp.Value.Background = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18181c")) : Brushes.Transparent;
            }

            if (PaneWikilink != null) PaneWikilink.Visibility = tabName == "wikilink" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneRef != null) PaneRef.Visibility = tabName == "ref" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneInterwiki != null) PaneInterwiki.Visibility = tabName == "interwiki" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneInfobox != null) PaneInfobox.Visibility = tabName == "infobox" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneTemplate != null) PaneTemplate.Visibility = tabName == "template" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneProse != null) PaneProse.Visibility = tabName == "prose" ? Visibility.Visible : Visibility.Collapsed;
            if (PaneAuto != null) PaneAuto.Visibility = tabName == "auto" ? Visibility.Visible : Visibility.Collapsed;

            switch (tabName)
            {
                case "wikilink":
                    LoadAssistantWikilinks();
                    break;
                case "ref":
                    LoadAssistantRefs();
                    break;
                case "interwiki":
                    LoadAssistantInterwiki();
                    break;
                case "infobox":
                    LoadAssistantInfobox();
                    break;
                case "template":
                    LoadAssistantTemplate();
                    break;
                case "prose":
                    LoadAssistantProse();
                    break;
                case "auto":
                    LoadAssistantAuto();
                    break;
            }
        }

        // ==================== 1. WIKILINK TAB ====================
        private void LoadAssistantWikilinks()
        {
            if (StackWikilinkRows == null) return;
            StackWikilinkRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var matches = new List<Tuple<int, string, string, string>>(); // line, fullMatch, target, label
            var termsToLookup = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var lineMatches = Regex.Matches(lines[i], @"\[\[([\s\S]*?)\]\]");
                foreach (Match m in lineMatches)
                {
                    string inner = m.Groups[1].Value;
                    string target = inner;
                    string label = inner;
                    if (inner.Contains("|"))
                    {
                        var parts = inner.Split(new[] { '|' }, 2);
                        target = parts[0];
                        label = parts[1];
                    }
                    matches.Add(Tuple.Create(i + 1, m.Value, target, label));
                    if (!string.IsNullOrEmpty(target) && !termsToLookup.Contains(target))
                    {
                        termsToLookup.Add(target);
                    }
                }
            }

            var dbMap = _dbService != null ? _dbService.GetBulkTranslations(_currentGame ?? "hsr", termsToLookup, "en_to_vi") : new Dictionary<string, string>();

            if (TxtWikilinkStatus != null)
            {
                TxtWikilinkStatus.Text = string.Format("Hiển thị {0} Wikilink được trích xuất trong bài viết", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackWikilinkRows.Children.Add(CreateAssistantEmptyNotice("Không tìm thấy Wikilink nào trong bài viết."));
                return;
            }

            foreach (var item in matches)
            {
                int lineNum = item.Item1;
                string fullMatch = item.Item2;
                string target = item.Item3;
                string label = item.Item4;

                bool hasDbMatch = dbMap.ContainsKey(target) && !string.IsNullOrEmpty(dbMap[target]);
                string viTarget = hasDbMatch ? dbMap[target] : "";

                var row = CreateAssistantWikilinkRow(lineNum, fullMatch, target, label, viTarget, hasDbMatch, (newVal) =>
                {
                    if (TxtEditorContent != null && !string.IsNullOrEmpty(fullMatch))
                    {
                        TxtEditorContent.Text = TxtEditorContent.Text.Replace(fullMatch, newVal);
                        if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật Wikilink dòng {0} thành: {1}", lineNum, newVal);
                    }
                }, () =>
                {
                    var singleMatch = _dbService != null ? _dbService.GetExactMatchGameData(_currentGame ?? "hsr", target) : null;
                    if (singleMatch != null && !string.IsNullOrEmpty(singleMatch.NameVi))
                    {
                        return singleMatch.NameVi;
                    }
                    return "";
                });
                StackWikilinkRows.Children.Add(row);
            }
        }

        private void BtnApplyAllWikilinks_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || StackWikilinkRows == null) return;
            string curText = TxtEditorContent.Text;
            int count = 0;

            foreach (var child in StackWikilinkRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;

                var origTb = grid.Children[1] as TextBlock;
                var spVi = grid.Children[2] as StackPanel;
                var viBoxBorder = spVi != null && spVi.Children.Count > 0 ? spVi.Children[0] as Border : null;
                var viGrid = viBoxBorder != null ? viBoxBorder.Child as Grid : null;
                var viBox = viGrid != null && viGrid.Children.Count > 1 ? viGrid.Children[1] as TextBox : null;

                if (origTb != null && viBox != null)
                {
                    string orig = origTb.Text;
                    string typedVi = viBox.Text.Trim();
                    if (!string.IsNullOrEmpty(orig) && curText.Contains(orig))
                    {
                        string inner = orig.StartsWith("[[") && orig.EndsWith("]]") ? orig.Substring(2, orig.Length - 4) : orig;
                        string target = inner;
                        string label = inner;
                        if (inner.Contains("|"))
                        {
                            var parts = inner.Split(new[] { '|' }, 2);
                            target = parts[0];
                            label = parts[1];
                        }

                        string replVal = !string.IsNullOrEmpty(typedVi) ? typedVi : target;
                        string finalWikilink = target == label 
                            ? string.Format("[[{0}]]", replVal) 
                            : string.Format("[[{0}|{1}]]", replVal, label);

                        curText = curText.Replace(orig, finalWikilink);
                        count++;
                    }
                }
            }

            TxtEditorContent.Text = curText;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã áp dụng đồng bộ {0} Wikilink vào bài viết!", count);
            MessageBox.Show(string.Format("Đã áp dụng thành công {0} Wikilink vào bài viết!", count), "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 2. REF TAB ====================
        private void LoadAssistantRefs()
        {
            if (StackRefRows == null) return;
            StackRefRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var matches = new List<Tuple<int, string, string, string>>(); // line, fullTag, name, inner
            var termsToLookup = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var refMatches = Regex.Matches(lines[i], @"<ref(?:\s+([^>]*))?>(.*?)<\/ref>|<ref(?:\s+([^>]*))?\/>", RegexOptions.IgnoreCase);
                foreach (Match m in refMatches)
                {
                    string full = m.Value;
                    string attrs = m.Groups[1].Success ? m.Groups[1].Value : (m.Groups[3].Success ? m.Groups[3].Value : "");
                    string inner = m.Groups[2].Success ? m.Groups[2].Value : "";

                    string refName = "";
                    var nmMatch = Regex.Match(attrs, @"name\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s/>]+))", RegexOptions.IgnoreCase);
                    if (nmMatch.Success)
                    {
                        refName = nmMatch.Groups[1].Value != "" ? nmMatch.Groups[1].Value : (nmMatch.Groups[2].Value != "" ? nmMatch.Groups[2].Value : nmMatch.Groups[3].Value);
                    }

                    matches.Add(Tuple.Create(i + 1, full, refName, inner));
                    if (!string.IsNullOrEmpty(inner))
                    {
                        var terms = _logicService != null ? _logicService.ExtractTermsFromMarkup(inner) : new List<string>();
                        foreach (var t in terms) if (!termsToLookup.Contains(t)) termsToLookup.Add(t);
                    }
                }
            }

            var dbMap = _dbService != null ? _dbService.GetBulkTranslations(_currentGame ?? "hsr", termsToLookup, "en_to_vi") : new Dictionary<string, string>();

            if (TxtRefStatus != null)
            {
                TxtRefStatus.Text = string.Format("Hiển thị {0} thẻ Ref trong bài viết", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackRefRows.Children.Add(CreateAssistantEmptyNotice("Không tìm thấy thẻ <ref> nào trong bài viết."));
                return;
            }

            foreach (var item in matches)
            {
                int lineNum = item.Item1;
                string fullTag = item.Item2;
                string refName = item.Item3;
                string inner = item.Item4;

                string viInner = inner;
                foreach (var kv in dbMap)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    {
                        viInner = Regex.Replace(viInner, @"(?<!\w)" + Regex.Escape(kv.Key) + @"(?!\w)", kv.Value, RegexOptions.IgnoreCase);
                    }
                }

                string defaultViTag = fullTag;
                if (!string.IsNullOrEmpty(inner) && viInner != inner)
                {
                    defaultViTag = fullTag.Replace(inner, viInner);
                }

                var row = CreateAssistantTableRow(lineNum, fullTag, defaultViTag, (newVal) =>
                {
                    if (TxtEditorContent != null && !string.IsNullOrEmpty(fullTag))
                    {
                        TxtEditorContent.Text = TxtEditorContent.Text.Replace(fullTag, newVal);
                        if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật thẻ Ref dòng {0} thành công!", lineNum);
                    }
                });
                StackRefRows.Children.Add(row);
            }
        }

        private void BtnApplyAllRefs_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || StackRefRows == null) return;
            string curText = TxtEditorContent.Text;
            int count = 0;

            foreach (var child in StackRefRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;

                var origTb = grid.Children[1] as TextBlock;
                var viBoxBorder = grid.Children[2] as Border;
                var viBox = viBoxBorder != null ? viBoxBorder.Child as TextBox : null;

                if (origTb != null && viBox != null)
                {
                    string orig = origTb.Text;
                    string repl = viBox.Text;
                    if (!string.IsNullOrEmpty(orig) && curText.Contains(orig))
                    {
                        curText = curText.Replace(orig, repl);
                        count++;
                    }
                }
            }

            TxtEditorContent.Text = curText;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã áp dụng đồng bộ {0} thẻ Ref vào bài viết!", count);
            MessageBox.Show(string.Format("Đã áp dụng thành công {0} thẻ Ref vào bài viết!", count), "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 3. INTERWIKI TAB ====================
        private void LoadAssistantInterwiki()
        {
            if (StackInterwikiRows == null) return;
            StackInterwikiRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            var matches = Regex.Matches(text, @"\[\[([a-z]{2,3}(?:-[a-z]+)?):([^\]]+)\]\]", RegexOptions.IgnoreCase);

            _interwikiLanguagesList.Clear();
            var dictNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "de", "Tiếng Đức" }, { "en", "Tiếng Anh" }, { "es", "Tây Ban Nha" },
                { "fr", "Tiếng Pháp" }, { "id", "Indonesia" }, { "it", "Tiếng Ý" },
                { "ja", "Tiếng Nhật" }, { "ko", "Tiếng Hàn" }, { "pl", "Tiếng Ba Lan" },
                { "pt", "Bồ Đào Nha" }, { "ru", "Tiếng Nga" }, { "th", "Tiếng Thái" },
                { "tr", "Thổ Nhĩ Kỳ" }, { "zhs", "Tiếng Trung (Giản thể)" },
                { "zht", "Tiếng Trung (Phồn thể)" }, { "vi", "Tiếng Việt" }
            };

            foreach (Match m in matches)
            {
                string code = m.Groups[1].Value.ToLower();
                string link = m.Value;
                _interwikiLanguagesList.Add(link);
                string name = dictNames.ContainsKey(code) ? dictNames[code] : code.ToUpper();

                var row = CreateAssistantInterwikiRow(name, link);
                StackInterwikiRows.Children.Add(row);
            }

            if (TxtInterwikiStatus != null)
            {
                TxtInterwikiStatus.Text = string.Format("Hiển thị {0} liên kết Interwiki hiện có trong bài viết", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackInterwikiRows.Children.Add(CreateAssistantEmptyNotice("Chưa có liên kết Interwiki nào trong bài viết. Hãy chọn ngôn ngữ và bấm 'Thêm'."));
            }
        }

        private void BtnAddInterwikiLang_Click(object sender, RoutedEventArgs e)
        {
            if (CmbInterwikiLang == null || StackInterwikiRows == null) return;
            var item = CmbInterwikiLang.SelectedItem as ComboBoxItem;
            if (item == null) return;

            string tag = item.Tag != null ? item.Tag.ToString() : "de";
            string langName = item.Content != null ? item.Content.ToString() : tag;
            string articleTitle = TxtTitleEn != null && !string.IsNullOrEmpty(TxtTitleEn.Text) && TxtTitleEn.Text != "Tên bài Anh..." 
                ? TxtTitleEn.Text.Trim() 
                : "Tên_Bài_Viết";

            string newLink = string.Format("[[{0}:{1}]]", tag, articleTitle);
            _interwikiLanguagesList.Add(newLink);

            var row = CreateAssistantInterwikiRow(langName, newLink);
            StackInterwikiRows.Children.Add(row);

            if (TxtInterwikiStatus != null)
            {
                TxtInterwikiStatus.Text = string.Format("Hiển thị {0} liên kết Interwiki", _interwikiLanguagesList.Count);
            }
        }

        private void BtnApplyAllInterwiki_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null) return;
            string text = TxtEditorContent.Text;

            // Remove existing interwikis
            text = Regex.Replace(text, @"\[\[[a-z]{2,3}(?:-[a-z]+)?:[^\]]+\]\]\r?\n?", "");
            text = text.TrimEnd();

            var linksToAppend = new List<string>();
            foreach (var child in StackInterwikiRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;
                var linkTb = grid.Children[1] as TextBlock;
                if (linkTb != null && !string.IsNullOrEmpty(linkTb.Text))
                {
                    linksToAppend.Add(linkTb.Text.Trim());
                }
            }

            if (linksToAppend.Count > 0)
            {
                text += "\n\n" + string.Join("\n", linksToAppend.ToArray());
            }

            TxtEditorContent.Text = text;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật {0} liên kết Interwiki vào bài viết!", linksToAppend.Count);
            MessageBox.Show("Đã cập nhật liên kết Interwiki vào cuối bài viết thành công!", "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 4. INFOBOX TAB ====================
        private void LoadAssistantInfobox()
        {
            if (StackInfoboxRows == null) return;
            StackInfoboxRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var matches = new List<Tuple<int, string, string, string>>(); // line, fullLine, key, val
            var termsToLookup = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"^\s*\|\s*([^=\r\n]+)=(.*)$");
                if (match.Success)
                {
                    string key = match.Groups[1].Value.Trim();
                    string val = match.Groups[2].Value.Trim();
                    matches.Add(Tuple.Create(i + 1, lines[i], key, val));

                    if (!string.IsNullOrEmpty(val))
                    {
                        var terms = _logicService != null ? _logicService.ExtractTermsFromMarkup(val) : new List<string>();
                        foreach (var t in terms) if (!termsToLookup.Contains(t)) termsToLookup.Add(t);
                    }
                }
            }

            var dbMap = _dbService != null ? _dbService.GetBulkTranslations(_currentGame ?? "hsr", termsToLookup, "en_to_vi") : new Dictionary<string, string>();

            if (TxtInfoboxStatus != null)
            {
                TxtInfoboxStatus.Text = string.Format("Hiển thị {0} tham số Infobox được bóc tách", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackInfoboxRows.Children.Add(CreateAssistantEmptyNotice("Không tìm thấy tham số Infobox (| param = ...) nào trong bài viết."));
                return;
            }

            foreach (var item in matches)
            {
                int lineNum = item.Item1;
                string fullLine = item.Item2;
                string key = item.Item3;
                string val = item.Item4;

                string viVal = val;
                foreach (var kv in dbMap)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    {
                        viVal = Regex.Replace(viVal, @"(?<!\w)" + Regex.Escape(kv.Key) + @"(?!\w)", kv.Value, RegexOptions.IgnoreCase);
                    }
                }
                string viLine = string.Format("| {0} = {1}", key, viVal);

                var row = CreateAssistantTableRow(lineNum, fullLine, viLine, (newVal) =>
                {
                    if (TxtEditorContent != null && !string.IsNullOrEmpty(fullLine))
                    {
                        TxtEditorContent.Text = TxtEditorContent.Text.Replace(fullLine, newVal);
                        if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật tham số Infobox dòng {0} thành công!", lineNum);
                    }
                });
                StackInfoboxRows.Children.Add(row);
            }
        }

        private void BtnApplyAllInfobox_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || StackInfoboxRows == null) return;
            string curText = TxtEditorContent.Text;
            int count = 0;

            foreach (var child in StackInfoboxRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;

                var origTb = grid.Children[1] as TextBlock;
                var viBoxBorder = grid.Children[2] as Border;
                var viBox = viBoxBorder != null ? viBoxBorder.Child as TextBox : null;

                if (origTb != null && viBox != null)
                {
                    string orig = origTb.Text;
                    string repl = viBox.Text;
                    if (!string.IsNullOrEmpty(orig) && curText.Contains(orig))
                    {
                        curText = curText.Replace(orig, repl);
                        count++;
                    }
                }
            }

            TxtEditorContent.Text = curText;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã áp dụng {0} tham số Infobox vào bài viết!", count);
            MessageBox.Show(string.Format("Đã áp dụng thành công {0} tham số Infobox vào bài viết!", count), "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 5. TEMPLATE TAB ====================
        private void LoadAssistantTemplate()
        {
            if (StackTemplateRows == null) return;
            StackTemplateRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var matches = new List<Tuple<int, string, string>>(); // line, fullMatch, inner
            var termsToLookup = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var tMatches = Regex.Matches(lines[i], @"\{\{([^\}\r\n]+)\}\}");
                foreach (Match m in tMatches)
                {
                    string full = m.Value;
                    string inner = m.Groups[1].Value;
                    matches.Add(Tuple.Create(i + 1, full, inner));

                    var terms = _logicService != null ? _logicService.ExtractTermsFromMarkup(inner) : new List<string>();
                    foreach (var t in terms) if (!termsToLookup.Contains(t)) termsToLookup.Add(t);
                }
            }

            var dbMap = _dbService != null ? _dbService.GetBulkTranslations(_currentGame ?? "hsr", termsToLookup, "en_to_vi") : new Dictionary<string, string>();

            if (TxtTemplateStatus != null)
            {
                TxtTemplateStatus.Text = string.Format("Hiển thị {0} Bản mẫu được trích xuất", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackTemplateRows.Children.Add(CreateAssistantEmptyNotice("Không tìm thấy Bản mẫu {{...}} nào trong bài viết."));
                return;
            }

            foreach (var item in matches)
            {
                int lineNum = item.Item1;
                string full = item.Item2;
                string inner = item.Item3;

                string viInner = inner;
                foreach (var kv in dbMap)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    {
                        viInner = Regex.Replace(viInner, @"(?<!\w)" + Regex.Escape(kv.Key) + @"(?!\w)", kv.Value, RegexOptions.IgnoreCase);
                    }
                }
                string viFull = string.Format("{{{{{0}}}}}", viInner);

                var row = CreateAssistantTableRow(lineNum, full, viFull, (newVal) =>
                {
                    if (TxtEditorContent != null && !string.IsNullOrEmpty(full))
                    {
                        TxtEditorContent.Text = TxtEditorContent.Text.Replace(full, newVal);
                        if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật Bản mẫu dòng {0} thành công!", lineNum);
                    }
                });
                StackTemplateRows.Children.Add(row);
            }
        }

        private void BtnApplyAllTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || StackTemplateRows == null) return;
            string curText = TxtEditorContent.Text;
            int count = 0;

            foreach (var child in StackTemplateRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;

                var origTb = grid.Children[1] as TextBlock;
                var viBoxBorder = grid.Children[2] as Border;
                var viBox = viBoxBorder != null ? viBoxBorder.Child as TextBox : null;

                if (origTb != null && viBox != null)
                {
                    string orig = origTb.Text;
                    string repl = viBox.Text;
                    if (!string.IsNullOrEmpty(orig) && curText.Contains(orig))
                    {
                        curText = curText.Replace(orig, repl);
                        count++;
                    }
                }
            }

            TxtEditorContent.Text = curText;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã áp dụng {0} Bản mẫu vào bài viết!", count);
            MessageBox.Show(string.Format("Đã áp dụng thành công {0} Bản mẫu vào bài viết!", count), "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 6. PROSE TAB ====================
        private void LoadAssistantProse()
        {
            if (StackProseRows == null) return;
            StackProseRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var matches = new List<Tuple<int, string>>();
            var termsToLookup = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string tr = lines[i].Trim();
                if (string.IsNullOrEmpty(tr)) continue;
                if (!Regex.IsMatch(tr, @"^[\{\}\!\|\=\#\*\:\;\<]"))
                {
                    matches.Add(Tuple.Create(i + 1, lines[i]));
                    var terms = _logicService != null ? _logicService.ExtractTermsFromMarkup(lines[i]) : new List<string>();
                    foreach (var t in terms) if (!termsToLookup.Contains(t)) termsToLookup.Add(t);
                }
            }

            var dbMap = _dbService != null ? _dbService.GetBulkTranslations(_currentGame ?? "hsr", termsToLookup, "en_to_vi") : new Dictionary<string, string>();

            if (TxtProseStatus != null)
            {
                TxtProseStatus.Text = string.Format("Hiển thị {0} dòng văn bản thường (Prose)", matches.Count);
            }

            if (matches.Count == 0)
            {
                StackProseRows.Children.Add(CreateAssistantEmptyNotice("Không tìm thấy dòng văn bản thường nào trong bài viết."));
                return;
            }

            foreach (var item in matches)
            {
                int lineNum = item.Item1;
                string originalLine = item.Item2;

                string viLine = originalLine;
                foreach (var kv in dbMap)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    {
                        viLine = Regex.Replace(viLine, @"(?<!\w)" + Regex.Escape(kv.Key) + @"(?!\w)", kv.Value, RegexOptions.IgnoreCase);
                    }
                }

                var row = CreateAssistantTableRow(lineNum, originalLine, viLine, (newVal) =>
                {
                    if (TxtEditorContent != null && !string.IsNullOrEmpty(originalLine))
                    {
                        TxtEditorContent.Text = TxtEditorContent.Text.Replace(originalLine, newVal);
                        if (TxtStatus != null) TxtStatus.Text = string.Format("Đã cập nhật dòng văn bản thường {0} thành công!", lineNum);
                    }
                });
                StackProseRows.Children.Add(row);
            }
        }

        private void BtnApplyAllProse_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || StackProseRows == null) return;
            string curText = TxtEditorContent.Text;
            int count = 0;

            foreach (var child in StackProseRows.Children)
            {
                var border = child as Border;
                if (border == null) continue;
                var grid = border.Child as Grid;
                if (grid == null) continue;

                var origTb = grid.Children[1] as TextBlock;
                var viBoxBorder = grid.Children[2] as Border;
                var viBox = viBoxBorder != null ? viBoxBorder.Child as TextBox : null;

                if (origTb != null && viBox != null)
                {
                    string orig = origTb.Text;
                    string repl = viBox.Text;
                    if (!string.IsNullOrEmpty(orig) && curText.Contains(orig))
                    {
                        curText = curText.Replace(orig, repl);
                        count++;
                    }
                }
            }

            TxtEditorContent.Text = curText;
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã áp dụng {0} dòng văn bản thường vào bài viết!", count);
            MessageBox.Show(string.Format("Đã áp dụng thành công {0} dòng văn bản thường vào bài viết!", count), "Trợ lý Dịch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==================== 7. AUTO TAB ====================
        private void LoadAssistantAuto()
        {
            if (StackAutoRows == null) return;
            StackAutoRows.Children.Clear();

            string text = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int countDialogue = 0;
            int countHeader = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string tr = line.Trim();
                if (string.IsNullOrEmpty(tr)) continue;

                if (tr.StartsWith("==") && tr.EndsWith("=="))
                {
                    countHeader++;
                    var row = CreateAssistantAutoRow(i + 1, "📌 Tiêu đề", "#f472b6", line);
                    StackAutoRows.Children.Add(row);
                }
                else if (tr.Contains("'''") || tr.Contains("\"") || tr.Contains("“") || tr.Contains("{{Dialogue") || tr.Contains("{{Vo"))
                {
                    countDialogue++;
                    var row = CreateAssistantAutoRow(i + 1, "💬 Hội thoại", "#60a5fa", line);
                    StackAutoRows.Children.Add(row);
                }
            }

            if (TxtAutoStatus != null)
            {
                TxtAutoStatus.Text = string.Format("Phát hiện {0} dòng Hội thoại & {1} Tiêu đề sẵn sàng dịch tự động theo DB", countDialogue, countHeader);
            }

            if (countDialogue == 0 && countHeader == 0)
            {
                StackAutoRows.Children.Add(CreateAssistantEmptyNotice("Không phát hiện dòng Hội thoại hoặc Tiêu đề nào phù hợp để dịch tự động."));
            }
        }

        private void BtnExecuteAuto_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent == null || _logicService == null) return;
            string text = TxtEditorContent.Text;
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            bool doDialogue = ChkAutoDialogue != null && ChkAutoDialogue.IsChecked == true;
            bool doHeader = ChkAutoHeader != null && ChkAutoHeader.IsChecked == true;
            bool doWikilinks = ChkAutoWikilinks != null && ChkAutoWikilinks.IsChecked == true;

            int processedCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string tr = line.Trim();
                if (string.IsNullOrEmpty(tr)) continue;

                if (doHeader && tr.StartsWith("==") && tr.EndsWith("=="))
                {
                    lines[i] = _logicService.ProcessHeaderLine(line, _currentGame ?? "hsr", "en_to_vi");
                    processedCount++;
                }
                else if (doDialogue && (tr.Contains("'''") || tr.Contains("\"") || tr.Contains("“") || tr.Contains("{{Dialogue") || tr.Contains("{{Vo")))
                {
                    lines[i] = _logicService.ProcessDialogueLineOptimized(line, _currentGame ?? "hsr", "en_to_vi");
                    processedCount++;
                }
                else if (doWikilinks && line.Contains("[["))
                {
                    lines[i] = _logicService.TranslateWikilinksInString(line, _currentGame ?? "hsr", "en_to_vi");
                    processedCount++;
                }
            }

            TxtEditorContent.Text = string.Join("\n", lines);
            if (TxtStatus != null) TxtStatus.Text = string.Format("Đã thực hiện dịch tự động cho {0} dòng thành công theo CSDL DB!", processedCount);
            MessageBox.Show(string.Format("Đã thực hiện dịch tự động cho {0} dòng thành công theo CSDL TextMap DB!", processedCount), "Trợ lý Dịch Tự Động", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadAssistantAuto();
        }

        // ==================== ASSISTANT UI BUILDER HELPERS ====================
        private Border CreateAssistantWikilinkRow(int lineNum, string fullMatch, string target, string label, string initialViVal, bool hasDbMatch, Action<string> onReplace, Func<string> onTraDb)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 10, 20, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            // Col 0: DÒNG (Plain line number)
            var tbLine = new TextBlock
            {
                Text = lineNum.ToString(),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 4, 0, 0)
            };
            Grid.SetColumn(tbLine, 0);

            // Col 1: WIKILINK EN (Bold white text)
            var tbOrig = new TextBlock
            {
                Text = fullMatch,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 4, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(tbOrig, 1);

            // Col 2: WIKILINK VI (Input box with placeholder + status)
            var spVi = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 10, 0) };

            var inputBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3142")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Height = 28,
                Padding = new Thickness(8, 0, 8, 0)
            };

            var gridInput = new Grid();
            var tbPlaceholder = new TextBlock
            {
                Text = "Dịch Tên bài (Target)...",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b")),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Visibility = string.IsNullOrEmpty(initialViVal) ? Visibility.Visible : Visibility.Collapsed
            };

            var txtVi = new TextBox
            {
                Text = initialViVal,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007acc")),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            txtVi.TextChanged += (s, ev) =>
            {
                tbPlaceholder.Visibility = string.IsNullOrEmpty(txtVi.Text) ? Visibility.Visible : Visibility.Collapsed;
            };

            gridInput.Children.Add(tbPlaceholder);
            gridInput.Children.Add(txtVi);
            inputBorder.Child = gridInput;

            var spStatus = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var pathInfo = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hasDbMatch ? "#38bdf8" : "#858585")),
                Width = 11,
                Height = 11,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tbStatus = new TextBlock
            {
                Text = hasDbMatch ? "Đã khớp DB (100%)" : "Sẵn sàng chỉnh sửa",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hasDbMatch ? "#38bdf8" : "#858585")),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            spStatus.Children.Add(pathInfo);
            spStatus.Children.Add(tbStatus);

            spVi.Children.Add(inputBorder);
            spVi.Children.Add(spStatus);
            Grid.SetColumn(spVi, 2);

            // Col 3: THAO TÁC (Tra DB + Thay thế side-by-side)
            var spActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 0, 0)
            };

            var btnTraDb = new Button
            {
                Content = "Tra DB",
                Width = 64,
                Height = 26,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#132a57")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1d4ed8")),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60a5fa")),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btnTraDb.Click += (s, ev) =>
            {
                string dbRes = onTraDb();
                if (!string.IsNullOrEmpty(dbRes))
                {
                    txtVi.Text = dbRes;
                    tbStatus.Text = "Đã khớp DB (100%)";
                    tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                    pathInfo.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                }
                else
                {
                    tbStatus.Text = "Chưa tìm thấy trong DB";
                    tbStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                    pathInfo.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f59e0b"));
                }
            };

            var btnThayThe = new Button
            {
                Content = "Thay thế",
                Width = 64,
                Height = 26,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f3318")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16a34a")),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ade80")),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            btnThayThe.Click += (s, ev) =>
            {
                string replVal = txtVi.Text.Trim();
                if (string.IsNullOrEmpty(replVal))
                {
                    replVal = target;
                }
                string finalWikilink = target == label 
                    ? string.Format("[[{0}]]", replVal) 
                    : string.Format("[[{0}|{1}]]", replVal, label);
                onReplace(finalWikilink);
            };

            spActions.Children.Add(btnTraDb);
            spActions.Children.Add(btnThayThe);
            Grid.SetColumn(spActions, 3);

            grid.Children.Add(tbLine);
            grid.Children.Add(tbOrig);
            grid.Children.Add(spVi);
            grid.Children.Add(spActions);

            border.Child = grid;

            border.MouseEnter += (s, ev) =>
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e1e24"));
            };
            border.MouseLeave += (s, ev) =>
            {
                border.Background = Brushes.Transparent;
            };

            return border;
        }

        private Border CreateAssistantTableRow(int lineNum, string originalText, string viDefaultValue, Action<string> onApply)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            // Col 0: Line Badge
            var badgeBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3142")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = string.Format("#{0}", lineNum),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60a5fa")),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badgeBorder, 0);

            // Col 1: Original EN
            var tbOrig = new TextBlock
            {
                Text = originalText,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#fbbf24")),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tbOrig, 1);

            // Col 2: VI Editable Box
            var viBoxBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181a20")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3142")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Height = 32,
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(8, 0, 8, 0)
            };
            var txtVi = new TextBox
            {
                Text = viDefaultValue,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007acc")),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };
            viBoxBorder.Child = txtVi;
            Grid.SetColumn(viBoxBorder, 2);

            // Col 3: Apply Button
            var btnApply = new Button
            {
                Content = "Áp dụng",
                Height = 28,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = TryFindResource("VsLightPrimaryBtn") as Style
            };
            btnApply.Click += (s, ev) =>
            {
                onApply(txtVi.Text);
            };
            Grid.SetColumn(btnApply, 3);

            grid.Children.Add(badgeBorder);
            grid.Children.Add(tbOrig);
            grid.Children.Add(viBoxBorder);
            grid.Children.Add(btnApply);

            border.Child = grid;

            border.MouseEnter += (s, ev) =>
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222228"));
            };
            border.MouseLeave += (s, ev) =>
            {
                border.Background = Brushes.Transparent;
            };

            return border;
        }

        private Border CreateAssistantInterwikiRow(string langName, string linkText)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var tbLang = new TextBlock
            {
                Text = langName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60a5fa")),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tbLang, 0);

            var tbLink = new TextBlock
            {
                Text = linkText,
                Foreground = Brushes.White,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tbLink, 1);

            var btnDel = new Button
            {
                Content = "Xóa",
                Height = 26,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = TryFindResource("VsLightSecondaryBtn") as Style
            };
            btnDel.Click += (s, ev) =>
            {
                _interwikiLanguagesList.Remove(linkText);
                if (StackInterwikiRows != null) StackInterwikiRows.Children.Remove(border);
                if (TxtInterwikiStatus != null)
                {
                    TxtInterwikiStatus.Text = string.Format("Hiển thị {0} liên kết Interwiki", StackInterwikiRows.Children.Count);
                }
            };
            Grid.SetColumn(btnDel, 2);

            grid.Children.Add(tbLang);
            grid.Children.Add(tbLink);
            grid.Children.Add(btnDel);

            border.Child = grid;
            return border;
        }

        private Border CreateAssistantAutoRow(int lineNum, string typeName, string badgeHexColor, string lineText)
        {
            var border = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badgeLine = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3142")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = string.Format("#{0}", lineNum),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60a5fa")),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badgeLine, 0);

            var badgeType = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#25252a")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeHexColor)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = typeName,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeHexColor)),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold
                }
            };
            Grid.SetColumn(badgeType, 1);

            var tbText = new TextBlock
            {
                Text = lineText,
                Foreground = Brushes.White,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tbText, 2);

            grid.Children.Add(badgeLine);
            grid.Children.Add(badgeType);
            grid.Children.Add(tbText);

            border.Child = grid;
            return border;
        }

        private Border CreateAssistantEmptyNotice(string message)
        {
            return new Border
            {
                Padding = new Thickness(20, 40, 20, 40),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                    FontSize = 12.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };
        }

        private void InsertOrWrapText(string prefix, string suffix, string defaultText)
        {
            if (TxtEditorContent == null) return;

            string selected = TxtEditorContent.SelectedText;
            if (!string.IsNullOrEmpty(selected))
            {
                TxtEditorContent.SelectedText = prefix + selected + suffix;
            }
            else
            {
                int caretIndex = TxtEditorContent.CaretIndex;
                string insertText = prefix + defaultText + suffix;
                TxtEditorContent.Text = TxtEditorContent.Text.Insert(caretIndex, insertText);
                TxtEditorContent.CaretIndex = caretIndex + prefix.Length + defaultText.Length;
            }
            TxtEditorContent.Focus();
        }

        // --- SUB-MENU & FILE ATTRIBUTES MANAGEMENT ---
        private void BtnFileMenu_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu fileMenu = new ContextMenu();
            Style menuStyle = FindResource("VsContextMenuStyle") as Style;
            if (menuStyle != null) fileMenu.Style = menuStyle;

            MenuItem newFile = new MenuItem { Header = "📄 Tạo tệp dự án mới" };
            newFile.Click += (s, ev) => CreateNewProject();

            MenuItem openFile = new MenuItem { Header = "📂 Mở tệp dự án JSON / MediaWiki..." };
            openFile.Click += (s, ev) => OpenProjectDialog();

            MenuItem saveFile = new MenuItem { Header = "💾 Lưu dự án hiện tại" };
            saveFile.Click += (s, ev) => SaveCurrentProject();

            MenuItem saveAsFile = new MenuItem { Header = "💾 Lưu thành tệp mới (Save As)..." };
            saveAsFile.Click += (s, ev) => SaveProjectAsDialog();

            MenuItem exitApp = new MenuItem { Header = "🚪 Thoát ứng dụng" };
            exitApp.Click += (s, ev) => Application.Current.Shutdown();

            fileMenu.Items.Add(newFile);
            fileMenu.Items.Add(openFile);
            fileMenu.Items.Add(saveFile);
            fileMenu.Items.Add(saveAsFile);
            fileMenu.Items.Add(new Separator());
            fileMenu.Items.Add(exitApp);

            if (BtnFileMenu != null)
            {
                fileMenu.PlacementTarget = BtnFileMenu;
                fileMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                fileMenu.CustomPopupPlacementCallback = delegate(Size popupSize, Size targetSize, Point offset)
                {
                    return new System.Windows.Controls.Primitives.CustomPopupPlacement[]
                    {
                        new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0, targetSize.Height), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                    };
                };
                fileMenu.IsOpen = true;
            }
        }

        private void BtnSearchMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowModalLookup();
        }


        // --- MODAL DIALOGS CONTROLLER & HANDLERS ---
        public void ShowModalSettings()
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Visible;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;

            SwitchSettingsTab("display");
        }

        private void HeaderGroupSystem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SwitchSettingsTab("display");
        }

        private void HeaderGroupWiki_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SwitchSettingsTab("wiki");
        }

        private void HeaderGroupTranslate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SwitchSettingsTab("llm");
        }

        private void BtnTabSetting_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            string tabKey = btn.Tag.ToString();
            SwitchSettingsTab(tabKey);
        }

        private string _currentCategory = "";

        private void SwitchSettingsTab(string tabKey)
        {
            bool isSystemActive = (tabKey == "display" || tabKey == "update");
            bool isWikiActive = (tabKey == "wiki");
            bool isTranslateActive = (tabKey == "llm" || tabKey == "nmt" || tabKey == "translate");

            if (PaneSettingGroupSystem != null) PaneSettingGroupSystem.Visibility = isSystemActive ? Visibility.Visible : Visibility.Collapsed;
            if (PaneSettingWiki != null) PaneSettingWiki.Visibility = isWikiActive ? Visibility.Visible : Visibility.Collapsed;
            if (PaneSettingGroupTranslate != null) PaneSettingGroupTranslate.Visibility = isTranslateActive ? Visibility.Visible : Visibility.Collapsed;

            // Accordion tree expansion: expand only sub-items of active category
            if (TreeGroupSystem != null) TreeGroupSystem.Visibility = isSystemActive ? Visibility.Visible : Visibility.Collapsed;
            if (TreeGroupWiki != null) TreeGroupWiki.Visibility = isWikiActive ? Visibility.Visible : Visibility.Collapsed;
            if (TreeGroupTranslate != null) TreeGroupTranslate.Visibility = isTranslateActive ? Visibility.Visible : Visibility.Collapsed;

            SetCategoryHeaderActive(HeaderGroupSystem, IconHeaderGroupSystem, TxtHeaderGroupSystem, isSystemActive);
            SetCategoryHeaderActive(HeaderGroupWiki, IconHeaderGroupWiki, TxtHeaderGroupWiki, isWikiActive);
            SetCategoryHeaderActive(HeaderGroupTranslate, IconHeaderGroupTranslate, TxtHeaderGroupTranslate, isTranslateActive);

            Button activeBtn = null;
            FrameworkElement targetSection = null;
            ScrollViewer targetScrollViewer = null;

            if (tabKey == "display")
            {
                activeBtn = BtnTabSettingDisplay;
                targetSection = PaneSettingDisplay;
                targetScrollViewer = ScrollSystemSettings;
            }
            else if (tabKey == "update")
            {
                activeBtn = BtnTabSettingUpdate;
                targetSection = PaneSettingUpdate;
                targetScrollViewer = ScrollSystemSettings;
            }
            else if (tabKey == "wiki")
            {
                activeBtn = BtnTabSettingWiki;
            }
            else if (tabKey == "llm" || tabKey == "translate")
            {
                activeBtn = BtnTabSettingLlm;
                targetSection = PaneSettingLlm;
                targetScrollViewer = ScrollTranslateSettings;
            }
            else if (tabKey == "nmt")
            {
                activeBtn = BtnTabSettingNmt;
                targetSection = PaneSettingNmt;
                targetScrollViewer = ScrollTranslateSettings;
            }

            SetTabButtonActive(BtnTabSettingDisplay, IconTabSettingDisplay, tabKey == "display");
            SetTabButtonActive(BtnTabSettingUpdate, IconTabSettingUpdate, tabKey == "update");
            SetTabButtonActive(BtnTabSettingWiki, IconTabSettingWiki, tabKey == "wiki");
            SetTabButtonActive(BtnTabSettingLlm, IconTabSettingLlm, tabKey == "llm" || tabKey == "translate");
            SetTabButtonActive(BtnTabSettingNmt, IconTabSettingNmt, tabKey == "nmt");

            string newCategory = isSystemActive ? "system" : (isWikiActive ? "wiki" : "translate");
            bool categoryChanged = (_currentCategory != newCategory);
            _currentCategory = newCategory;

            if (activeBtn != null)
            {
                AnimateTabIndicatorTo(activeBtn, categoryChanged);
            }

            if (targetSection != null && targetScrollViewer != null)
            {
                ScrollToSectionInViewer(targetScrollViewer, targetSection);
            }
        }

        private void ScrollToSectionInViewer(ScrollViewer scrollViewer, FrameworkElement targetSection)
        {
            if (targetSection == null || scrollViewer == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    scrollViewer.UpdateLayout();
                    UIElement content = scrollViewer.Content as UIElement;
                    if (content != null)
                    {
                        GeneralTransform transform = targetSection.TransformToVisual(content);
                        Point pos = transform.Transform(new Point(0, 0));
                        scrollViewer.ScrollToVerticalOffset(pos.Y);
                    }
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        private void SetCategoryHeaderActive(Border headerBorder, System.Windows.Shapes.Path iconPath, TextBlock headerText, bool isActive)
        {
            if (headerBorder != null)
            {
                headerBorder.Background = Brushes.Transparent;
            }
            if (iconPath != null)
            {
                iconPath.Fill = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80848e"));
            }
            if (headerText != null)
            {
                headerText.Foreground = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80848e"));
            }
        }

        private void SetTabButtonActive(Button btn, System.Windows.Shapes.Path iconPath, bool isActive)
        {
            if (btn == null) return;
            btn.Foreground = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#949ba4"));
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            btn.Background = Brushes.Transparent;
            btn.BorderBrush = Brushes.Transparent;

            if (iconPath != null)
            {
                iconPath.Fill = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80848e"));
            }
        }

        private void AnimateTabIndicatorTo(Button targetBtn, bool snapImmediately = false)
        {
            if (targetBtn == null || ActiveTabIndicatorTransform == null || SidebarNavContainer == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SidebarNavContainer.UpdateLayout();

                    GeneralTransform transform = targetBtn.TransformToVisual(SidebarNavContainer);
                    Point pos = transform.Transform(new Point(0, 0));

                    double targetY = pos.Y;

                    if (snapImmediately)
                    {
                        ActiveTabIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, null);
                        ActiveTabIndicatorTransform.Y = targetY;
                    }
                    else
                    {
                        DoubleAnimation anim = new DoubleAnimation
                        {
                            To = targetY,
                            Duration = TimeSpan.FromMilliseconds(200),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };

                        ActiveTabIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, anim);
                    }
                }
                catch { }
            }), DispatcherPriority.Loaded);
        }

        private void BtnScanFonts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbFontFamily == null) return;

                ComboBoxItem selItem = CmbFontFamily.SelectedItem as ComboBoxItem;
                string currentSelected = (selItem != null && selItem.Content != null) ? selItem.Content.ToString() : "Consolas";

                // Scan all system installed font families via WPF SystemFontFamilies
                var installedFonts = System.Windows.Media.Fonts.SystemFontFamilies
                    .Select(f => f.Source)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .OrderBy(f => f)
                    .ToList();

                if (installedFonts.Count > 0)
                {
                    CmbFontFamily.Items.Clear();
                    int selectIndex = 0;

                    for (int i = 0; i < installedFonts.Count; i++)
                    {
                        string fontName = installedFonts[i];
                        var item = new ComboBoxItem { Content = fontName };

                        if (fontName.Equals(currentSelected, StringComparison.OrdinalIgnoreCase) ||
                            currentSelected.StartsWith(fontName, StringComparison.OrdinalIgnoreCase))
                        {
                            item.IsSelected = true;
                            selectIndex = i;
                        }

                        CmbFontFamily.Items.Add(item);
                    }

                    CmbFontFamily.SelectedIndex = selectIndex;
                    MessageBox.Show(string.Format("Đã quét và nạp thành công {0} phông chữ hệ thống vào danh sách!", installedFonts.Count), "Quét phông chữ thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể quét danh sách phông chữ hệ thống: " + ex.Message, "Lỗi Quét Font", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool _isSavingInstantly = false;

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            SaveSettingsInstantly();
        }

        private void SaveSettingsInstantly()
        {
            if (_isSavingInstantly || !IsLoaded) return;
            try
            {
                _isSavingInstantly = true;
                // Auto-save setting changes immediately
                Console.WriteLine("Ether Editor Settings: Settings auto-saved instantly.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Auto-save settings error: " + ex.Message);
            }
            finally
            {
                _isSavingInstantly = false;
            }
        }


        private struct GlossaryCatDef
        {
            public string Id;
            public string Name;
            public GlossaryCatDef(string id, string name) { Id = id; Name = name; }
        }

        private readonly List<GlossaryCatDef> _glossaryCategoryDefs = new List<GlossaryCatDef>
        {
            new GlossaryCatDef("global", "DÙNG CHUNG (CHUNG)"),
            new GlossaryCatDef("hsr", "HONKAI: STAR RAIL"),
            new GlossaryCatDef("genshin", "GENSHIN IMPACT"),
            new GlossaryCatDef("zzz", "ZENLESS ZONE ZERO")
        };

        private Dictionary<string, bool> _glossaryCollapsedState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private string _editingGlossaryCategory = null;
        private string _editingGlossaryKey = null;

        private void InitDefaultGlossaryCollapsedStates()
        {
            string currentGame = GetSelectedGameId().ToLower();
            if (_glossaryCollapsedState == null)
            {
                _glossaryCollapsedState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            _glossaryCollapsedState.Clear();
            foreach (var catDef in _glossaryCategoryDefs)
            {
                string id = catDef.Id.ToLower();
                if (id == "global" || id == currentGame)
                {
                    _glossaryCollapsedState[id] = false; // Mở sẵn
                }
                else
                {
                    _glossaryCollapsedState[id] = true;  // Thu gọn sẵn
                }
            }
        }

        public void ShowModalGlossary()
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;

            if (_glossaryCollapsedState == null || _glossaryCollapsedState.Count == 0)
            {
                InitDefaultGlossaryCollapsedStates();
            }

            RenderGlossaryAccordionUI();
        }

        public void ShowModalLookup()
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;

            PopulateInstalledGamesDropdown();

            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            if (!string.IsNullOrEmpty(q))
            {
                PerformLookupSearch(q, 1);
            }
            else
            {
                ResetLookupUI();
            }
        }

        private void PopulateInstalledGamesDropdown()
        {
            if (CmbLookupGame == null) return;

            string currentSelectedTag = null;
            ComboBoxItem curItem = CmbLookupGame.SelectedItem as ComboBoxItem;
            if (curItem != null && curItem.Tag != null)
            {
                currentSelectedTag = curItem.Tag.ToString();
            }

            var installedGames = _dbService.GetInstalledGames();

            CmbLookupGame.SelectionChanged -= OnLookupFilterChanged;
            CmbLookupGame.Items.Clear();

            if (installedGames != null && installedGames.Count > 0)
            {
                ComboBoxItem firstItem = null;
                ComboBoxItem matchedItem = null;

                foreach (var g in installedGames)
                {
                    var item = new ComboBoxItem
                    {
                        Content = g.FullName,
                        Tag = g.GameId
                    };
                    CmbLookupGame.Items.Add(item);
                    if (firstItem == null) firstItem = item;
                    if (currentSelectedTag != null && g.GameId.Equals(currentSelectedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedItem = item;
                    }
                }

                if (installedGames.Count > 1)
                {
                    var allItem = new ComboBoxItem
                    {
                        Content = "Tất cả Game",
                        Tag = "all"
                    };
                    CmbLookupGame.Items.Add(allItem);
                    if (currentSelectedTag != null && currentSelectedTag.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        matchedItem = allItem;
                    }
                }

                CmbLookupGame.SelectedItem = matchedItem ?? firstItem;
            }
            else
            {
                CmbLookupGame.Items.Add(new ComboBoxItem { Content = "Honkai: Star Rail", Tag = "hsr", IsSelected = true });
            }

            CmbLookupGame.SelectionChanged += OnLookupFilterChanged;
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Collapsed;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;
            if (BorderModalAssistant != null) BorderModalAssistant.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            BtnCloseModal_Click(null, null);
            MessageBox.Show("Đã lưu cấu hình Cài đặt hệ thống thành công!", "Ether Editor Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RenderGlossaryAccordionUI()
        {
            if (StackGlossaryCategories == null) return;
            StackGlossaryCategories.Children.Clear();

            var categorizedData = GlossaryService.Instance.GetCategorizedGlossary();
            int totalTermCount = 0;
            string currentGame = GetSelectedGameId().ToLower();

            foreach (var catDef in _glossaryCategoryDefs)
            {
                string catId = catDef.Id.ToLower();
                Dictionary<string, string> termDict;
                if (!categorizedData.TryGetValue(catId, out termDict))
                {
                    termDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                totalTermCount += termDict.Count;

                bool isCollapsed = false;
                if (!_glossaryCollapsedState.TryGetValue(catId, out isCollapsed))
                {
                    isCollapsed = (catId != "global" && catId != currentGame);
                    _glossaryCollapsedState[catId] = isCollapsed;
                }

                Border catBorder = new Border();
                catBorder.Margin = new Thickness(0, 0, 0, 10);
                catBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e1e24"));
                catBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d"));
                catBorder.BorderThickness = new Thickness(1);
                catBorder.CornerRadius = new CornerRadius(6);

                StackPanel catStack = new StackPanel();

                Grid headerGrid = new Grid();
                headerGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526"));
                headerGrid.MinHeight = 38;
                headerGrid.Cursor = Cursors.Hand;

                StackPanel headerTextStack = new StackPanel();
                headerTextStack.Orientation = Orientation.Horizontal;
                headerTextStack.VerticalAlignment = VerticalAlignment.Center;
                headerTextStack.Margin = new Thickness(14, 8, 14, 8);

                TextBlock txtArrow = new TextBlock();
                txtArrow.Text = isCollapsed ? "› " : "∨ ";
                txtArrow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                txtArrow.FontWeight = FontWeights.Bold;
                txtArrow.FontSize = 13;
                txtArrow.Margin = new Thickness(0, 0, 6, 0);

                TextBlock txtTitle = new TextBlock();
                txtTitle.Text = catDef.Name;
                txtTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                txtTitle.FontWeight = FontWeights.Bold;
                txtTitle.FontSize = 12.5;

                TextBlock txtCount = new TextBlock();
                txtCount.Text = string.Format(" ({0} từ)", termDict.Count);
                txtCount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80848e"));
                txtCount.FontSize = 11;
                txtCount.Margin = new Thickness(6, 0, 0, 0);

                headerTextStack.Children.Add(txtArrow);
                headerTextStack.Children.Add(txtTitle);
                headerTextStack.Children.Add(txtCount);
                headerGrid.Children.Add(headerTextStack);

                string capturedCatId = catId;
                headerGrid.MouseLeftButtonDown += (s, e) =>
                {
                    _glossaryCollapsedState[capturedCatId] = !_glossaryCollapsedState.ContainsKey(capturedCatId) || !_glossaryCollapsedState[capturedCatId];
                    RenderGlossaryAccordionUI();
                };

                catStack.Children.Add(headerGrid);

                if (!isCollapsed)
                {
                    StackPanel contentStack = new StackPanel();

                    if (termDict.Count == 0)
                    {
                        Border emptyBorder = new Border();
                        emptyBorder.Padding = new Thickness(16);
                        emptyBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18181c"));

                        TextBlock txtEmpty = new TextBlock();
                        txtEmpty.Text = "Chưa có thuật ngữ nào được cấu hình riêng trong mục này";
                        txtEmpty.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
                        txtEmpty.FontStyle = FontStyles.Italic;
                        txtEmpty.FontSize = 11.5;
                        txtEmpty.HorizontalAlignment = HorizontalAlignment.Center;

                        emptyBorder.Child = txtEmpty;
                        contentStack.Children.Add(emptyBorder);
                    }
                    else
                    {
                        var sortedKeys = new List<string>(termDict.Keys);
                        sortedKeys.Sort();

                        int rowIndex = 0;
                        foreach (string sourceKey in sortedKeys)
                        {
                            string targetVal = termDict[sourceKey];
                            bool isEditingThis = (_editingGlossaryCategory == capturedCatId && _editingGlossaryKey == sourceKey);

                            Border rowBorder = new Border();
                            rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(rowIndex % 2 == 0 ? "#18181c" : "#1e1e24"));
                            rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d"));
                            rowBorder.BorderThickness = new Thickness(0, 0, 0, 1);
                            rowBorder.Padding = new Thickness(16, 8, 16, 8);

                            Grid rowGrid = new Grid();
                            rowGrid.MinHeight = 36;
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                            if (isEditingThis)
                            {
                                TextBox txtEditEn = new TextBox();
                                txtEditEn.Text = sourceKey;
                                txtEditEn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141416"));
                                txtEditEn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                                txtEditEn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                                txtEditEn.BorderThickness = new Thickness(1);
                                txtEditEn.Padding = new Thickness(6, 4, 6, 4);
                                txtEditEn.FontSize = 12;
                                txtEditEn.Margin = new Thickness(0, 0, 10, 0);
                                Grid.SetColumn(txtEditEn, 0);

                                TextBox txtEditVi = new TextBox();
                                txtEditVi.Text = targetVal;
                                txtEditVi.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#141416"));
                                txtEditVi.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                                txtEditVi.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ce9178"));
                                txtEditVi.BorderThickness = new Thickness(1);
                                txtEditVi.Padding = new Thickness(6, 4, 6, 4);
                                txtEditVi.FontSize = 12;
                                txtEditVi.Margin = new Thickness(0, 0, 10, 0);
                                Grid.SetColumn(txtEditVi, 1);

                                StackPanel btnInlineStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

                                Button btnSaveInline = new Button();
                                btnSaveInline.Content = "✓ Lưu";
                                btnSaveInline.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ade80"));
                                btnSaveInline.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143823"));
                                btnSaveInline.BorderThickness = new Thickness(0);
                                btnSaveInline.Height = 26;
                                btnSaveInline.Width = 60;
                                btnSaveInline.Margin = new Thickness(0, 0, 6, 0);
                                btnSaveInline.Cursor = Cursors.Hand;
                                btnSaveInline.FontWeight = FontWeights.Bold;
                                btnSaveInline.FontSize = 11;

                                string origKey = sourceKey;
                                string currCat = capturedCatId;
                                btnSaveInline.Click += (s, e) =>
                                {
                                    string newEn = txtEditEn.Text.Trim();
                                    string newVi = txtEditVi.Text.Trim();
                                    if (!string.IsNullOrEmpty(newEn) && !string.IsNullOrEmpty(newVi))
                                    {
                                        if (!origKey.Equals(newEn, StringComparison.OrdinalIgnoreCase))
                                        {
                                            GlossaryService.Instance.RemoveTermFromCategory(currCat, origKey);
                                        }
                                        GlossaryService.Instance.AddTermToCategory(currCat, newEn, newVi);
                                        _editingGlossaryCategory = null;
                                        _editingGlossaryKey = null;
                                        RenderGlossaryAccordionUI();
                                    }
                                };

                                Button btnCancelInline = new Button();
                                btnCancelInline.Content = "✕ Hủy";
                                btnCancelInline.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"));
                                btnCancelInline.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d3139"));
                                btnCancelInline.BorderThickness = new Thickness(0);
                                btnCancelInline.Height = 26;
                                btnCancelInline.Width = 60;
                                btnCancelInline.Cursor = Cursors.Hand;
                                btnCancelInline.FontSize = 11;

                                btnCancelInline.Click += (s, e) =>
                                {
                                    _editingGlossaryCategory = null;
                                    _editingGlossaryKey = null;
                                    RenderGlossaryAccordionUI();
                                };

                                btnInlineStack.Children.Add(btnSaveInline);
                                btnInlineStack.Children.Add(btnCancelInline);
                                Grid.SetColumn(btnInlineStack, 2);

                                rowGrid.Children.Add(txtEditEn);
                                rowGrid.Children.Add(txtEditVi);
                                rowGrid.Children.Add(btnInlineStack);
                            }
                            else
                            {
                                TextBlock txtSource = new TextBlock();
                                txtSource.Text = sourceKey;
                                txtSource.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4fc1ff"));
                                txtSource.FontWeight = FontWeights.Bold;
                                txtSource.FontSize = 12.5;
                                txtSource.VerticalAlignment = VerticalAlignment.Center;
                                txtSource.Margin = new Thickness(0, 0, 10, 0);
                                Grid.SetColumn(txtSource, 0);

                                TextBlock txtTarget = new TextBlock();
                                txtTarget.Text = targetVal;
                                txtTarget.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ce9178"));
                                txtTarget.FontWeight = FontWeights.Bold;
                                txtTarget.FontSize = 12.5;
                                txtTarget.VerticalAlignment = VerticalAlignment.Center;
                                txtTarget.Margin = new Thickness(0, 0, 10, 0);
                                Grid.SetColumn(txtTarget, 1);

                                StackPanel btnActionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

                                Button btnEdit = new Button();
                                btnEdit.Content = "✏️ Sửa";
                                Style editStyle = TryFindResource("VsGlossaryEditBtn") as Style;
                                if (editStyle != null) btnEdit.Style = editStyle;
                                else
                                {
                                    btnEdit.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                                    btnEdit.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#102538"));
                                    btnEdit.BorderThickness = new Thickness(1);
                                    btnEdit.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e3a5f"));
                                    btnEdit.Width = 64;
                                    btnEdit.Height = 26;
                                    btnEdit.FontSize = 11.5;
                                    btnEdit.FontWeight = FontWeights.SemiBold;
                                }
                                btnEdit.Margin = new Thickness(0, 0, 8, 0);

                                string editCat = capturedCatId;
                                string editKey = sourceKey;
                                btnEdit.Click += (s, e) =>
                                {
                                    _editingGlossaryCategory = editCat;
                                    _editingGlossaryKey = editKey;
                                    RenderGlossaryAccordionUI();
                                };

                                Button btnDelete = new Button();
                                btnDelete.Content = "🗑️ Xóa";
                                Style delStyle = TryFindResource("VsGlossaryDeleteBtn") as Style;
                                if (delStyle != null) btnDelete.Style = delStyle;
                                else
                                {
                                    btnDelete.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f87171"));
                                    btnDelete.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33181b"));
                                    btnDelete.BorderThickness = new Thickness(1);
                                    btnDelete.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#522329"));
                                    btnDelete.Width = 64;
                                    btnDelete.Height = 26;
                                    btnDelete.FontSize = 11.5;
                                    btnDelete.FontWeight = FontWeights.SemiBold;
                                }

                                string delCat = capturedCatId;
                                string delKey = sourceKey;
                                btnDelete.Click += (s, e) =>
                                {
                                    GlossaryService.Instance.RemoveTermFromCategory(delCat, delKey);
                                    RenderGlossaryAccordionUI();
                                };

                                btnActionStack.Children.Add(btnEdit);
                                btnActionStack.Children.Add(btnDelete);
                                Grid.SetColumn(btnActionStack, 2);

                                rowGrid.Children.Add(txtSource);
                                rowGrid.Children.Add(txtTarget);
                                rowGrid.Children.Add(btnActionStack);
                            }

                            rowBorder.Child = rowGrid;
                            contentStack.Children.Add(rowBorder);
                            rowIndex++;
                        }
                    }

                    catStack.Children.Add(contentStack);
                }

                catBorder.Child = catStack;
                StackGlossaryCategories.Children.Add(catBorder);
            }

            if (TxtGlossarySummary != null)
            {
                TxtGlossarySummary.Text = string.Format("Hiển thị {0} thuật ngữ trong từ điển ưu tiên", totalTermCount);
            }
        }

        private void BtnAddGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            string en = TxtNewTermEn != null ? TxtNewTermEn.Text.Trim() : "";
            string vi = TxtNewTermVi != null ? TxtNewTermVi.Text.Trim() : "";

            string selectedCat = "global";
            ComboBoxItem catItem = CmbGlossaryCategory != null ? CmbGlossaryCategory.SelectedItem as ComboBoxItem : null;
            if (catItem != null && catItem.Tag != null)
            {
                selectedCat = catItem.Tag.ToString();
            }

            if (!string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(vi))
            {
                GlossaryService.Instance.AddTermToCategory(selectedCat, en, vi);
                if (TxtNewTermEn != null) TxtNewTermEn.Text = "";
                if (TxtNewTermVi != null) TxtNewTermVi.Text = "";
                RenderGlossaryAccordionUI();
            }
        }

        private void BtnDeleteGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            // Retained for backward compatibility
        }

        private void BtnSaveGlossary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GlossaryService.Instance.SaveCategorizedGlossary();
                MessageBox.Show("Đã lưu thay đổi từ điển ưu tiên thành công!", "Từ điển ưu tiên", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã xảy ra lỗi khi lưu từ điển: " + ex.Message, "Lỗi Lưu Từ điển", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private int _lookupCurrentPage = 1;
        private readonly int _lookupPageSize = 10;

        private void ResetLookupUI()
        {
            if (StackLookupResults != null)
            {
                StackLookupResults.Children.Clear();
                TextBlock txtInitial = new TextBlock
                {
                    Text = "Nhập từ khóa vào ô trên và nhấn '🔍 Tìm kiếm' (hoặc Enter) để bắt đầu tra cứu.",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                StackLookupResults.Children.Add(txtInitial);
            }

            if (StackLookupPagination != null)
            {
                StackLookupPagination.Children.Clear();
            }

            if (TxtLookupSummary != null)
            {
                TxtLookupSummary.Text = "Sẵn sàng tra cứu thuật ngữ & dữ liệu TextMap.";
            }
        }

        private async void PerformLookupSearch(string query, int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ResetLookupUI();
                return;
            }

            _lookupCurrentPage = page;
            if (StackLookupResults == null) return;
            StackLookupResults.Children.Clear();

            if (TxtLookupSummary != null)
            {
                TxtLookupSummary.Text = "Đang tìm kiếm dữ liệu...";
            }

            string selectedGame = "hsr";
            ComboBoxItem gameItem = CmbLookupGame != null ? CmbLookupGame.SelectedItem as ComboBoxItem : null;
            if (gameItem != null && gameItem.Tag != null)
            {
                selectedGame = gameItem.Tag.ToString();
            }

            string searchIn = "all";
            ComboBoxItem colItem = CmbLookupColumn != null ? CmbLookupColumn.SelectedItem as ComboBoxItem : null;
            if (colItem != null && colItem.Tag != null)
            {
                searchIn = colItem.Tag.ToString();
            }

            PaginatedSearchResult result = await Task.Run(() => 
                _dbService.SearchGameDataPaginated(selectedGame, query, searchIn, false, _lookupCurrentPage, _lookupPageSize)
            );

            // If DB returned 0 items or wasn't available, check Glossary as fallback
            if (result == null || result.Items == null || result.Items.Count == 0)
            {
                var glossaryResults = GlossaryService.Instance.SearchTerms(query);
                if (glossaryResults != null && glossaryResults.Count > 0)
                {
                    result = new PaginatedSearchResult();
                    result.TotalCount = glossaryResults.Count;
                    int startIndex = (_lookupCurrentPage - 1) * _lookupPageSize;
                    int count = 0;
                    foreach (var kvp in glossaryResults)
                    {
                        if (count >= startIndex && result.Items.Count < _lookupPageSize)
                        {
                            result.Items.Add(new GameDataRecord
                            {
                                GameId = selectedGame,
                                ItemId = "GLOSSARY",
                                NameEn = kvp.Key,
                                NameVi = kvp.Value
                            });
                        }
                        count++;
                    }
                }
            }

            int totalCount = (result != null) ? result.TotalCount : 0;
            var items = (result != null && result.Items != null) ? result.Items : new List<GameDataRecord>();

            if (items.Count == 0)
            {
                TextBlock txtEmpty = new TextBlock
                {
                    Text = "Không tìm thấy kết quả nào trùng khớp.",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                };
                StackLookupResults.Children.Add(txtEmpty);
            }
            else
            {
                int rowIndex = 0;
                foreach (var rec in items)
                {
                    string displayEn = !string.IsNullOrEmpty(rec.NameEn) ? rec.NameEn : rec.DescriptionEn;
                    string displayVi = !string.IsNullOrEmpty(rec.NameVi) ? rec.NameVi : rec.DescriptionVi;

                    Border rowBorder = new Border();
                    rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(rowIndex % 2 == 0 ? "#18181c" : "#1e1e24"));
                    rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d"));
                    rowBorder.BorderThickness = new Thickness(0, 0, 0, 1);
                    rowBorder.Padding = new Thickness(20, 12, 20, 12);

                    Grid rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

                    // Column 0: ID
                    TextBlock txtId = new TextBlock();
                    txtId.Text = rec.ItemId;
                    txtId.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                    txtId.FontSize = 12;
                    txtId.FontWeight = FontWeights.Normal;
                    txtId.TextWrapping = TextWrapping.Wrap;
                    txtId.TextAlignment = TextAlignment.Center;
                    txtId.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(txtId, 0);

                    // Column 1: Tiếng Anh (EN)
                    TextBlock txtEn = new TextBlock();
                    txtEn.Text = displayEn;
                    txtEn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                    txtEn.FontSize = 12.5;
                    txtEn.FontWeight = FontWeights.SemiBold;
                    txtEn.TextWrapping = TextWrapping.Wrap;
                    txtEn.Margin = new Thickness(12, 0, 16, 0);
                    txtEn.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(txtEn, 1);

                    // Column 2: Tiếng Việt (VI)
                    TextBlock txtVi = new TextBlock();
                    txtVi.Text = displayVi;
                    txtVi.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
                    txtVi.FontSize = 12.5;
                    txtVi.FontWeight = FontWeights.Normal;
                    txtVi.TextWrapping = TextWrapping.Wrap;
                    txtVi.Margin = new Thickness(0, 0, 16, 0);
                    txtVi.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(txtVi, 2);

                    // Column 3: Thao tác
                    StackPanel actionStack = new StackPanel();
                    actionStack.Orientation = Orientation.Vertical;
                    actionStack.HorizontalAlignment = HorizontalAlignment.Center;
                    actionStack.VerticalAlignment = VerticalAlignment.Top;

                    // Row 1 buttons: ->| EN and ->| VI
                    StackPanel btnRow1 = new StackPanel();
                    btnRow1.Orientation = Orientation.Horizontal;
                    btnRow1.HorizontalAlignment = HorizontalAlignment.Center;
                    btnRow1.Margin = new Thickness(0, 0, 0, 6);

                    Button btnEn = new Button();
                    btnEn.Content = "➔| EN";
                    btnEn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
                    btnEn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#102538"));
                    btnEn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e3a5f"));
                    btnEn.BorderThickness = new Thickness(1);
                    btnEn.Width = 56;
                    btnEn.Height = 24;
                    btnEn.FontSize = 11;
                    btnEn.FontWeight = FontWeights.Bold;
                    btnEn.Cursor = Cursors.Hand;
                    btnEn.Margin = new Thickness(0, 0, 6, 0);

                    string insEn = displayEn;
                    btnEn.Click += (s, e) =>
                    {
                        InsertTextIntoEditor(insEn);
                    };

                    Button btnVi = new Button();
                    btnVi.Content = "➔| VI";
                    btnVi.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f87171"));
                    btnVi.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33181b"));
                    btnVi.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#522329"));
                    btnVi.BorderThickness = new Thickness(1);
                    btnVi.Width = 56;
                    btnVi.Height = 24;
                    btnVi.FontSize = 11;
                    btnVi.FontWeight = FontWeights.Bold;
                    btnVi.Cursor = Cursors.Hand;

                    string insVi = displayVi;
                    btnVi.Click += (s, e) =>
                    {
                        InsertTextIntoEditor(insVi);
                    };

                    btnRow1.Children.Add(btnEn);
                    btnRow1.Children.Add(btnVi);

                    // Row 2 button: Copy ID
                    Button btnCopyId = new Button();
                    btnCopyId.Content = "Copy ID";
                    btnCopyId.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc"));
                    btnCopyId.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225"));
                    btnCopyId.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c3c3c"));
                    btnCopyId.BorderThickness = new Thickness(1);
                    btnCopyId.Width = 118;
                    btnCopyId.Height = 24;
                    btnCopyId.FontSize = 11;
                    btnCopyId.FontWeight = FontWeights.SemiBold;
                    btnCopyId.Cursor = Cursors.Hand;

                    string copyId = rec.ItemId;
                    btnCopyId.Click += (s, e) =>
                    {
                        try
                        {
                            Clipboard.SetText(copyId);
                            MessageBox.Show(string.Format("Đã sao chép ID: {0}", copyId), "Đã sao chép", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch { }
                    };

                    actionStack.Children.Add(btnRow1);
                    actionStack.Children.Add(btnCopyId);
                    Grid.SetColumn(actionStack, 3);

                    rowGrid.Children.Add(txtId);
                    rowGrid.Children.Add(txtEn);
                    rowGrid.Children.Add(txtVi);
                    rowGrid.Children.Add(actionStack);

                    rowBorder.Child = rowGrid;
                    StackLookupResults.Children.Add(rowBorder);
                    rowIndex++;
                }
            }

            RenderLookupPagination(_lookupCurrentPage, totalCount, query);
        }

        private void InsertTextIntoEditor(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (TxtEditorContent != null)
            {
                int caretIndex = TxtEditorContent.CaretIndex;
                TxtEditorContent.Text = TxtEditorContent.Text.Insert(caretIndex, text);
                TxtEditorContent.CaretIndex = caretIndex + text.Length;
                TxtEditorContent.Focus();
            }
            BtnCloseModal_Click(null, null);
        }

        private void RenderLookupPagination(int currentPage, int totalCount, string query)
        {
            if (StackLookupPagination == null) return;
            StackLookupPagination.Children.Clear();

            int totalPages = (int)Math.Ceiling((double)totalCount / _lookupPageSize);
            if (totalPages < 1) totalPages = 1;

            int startItem = totalCount > 0 ? (currentPage - 1) * _lookupPageSize + 1 : 0;
            int endItem = Math.Min(currentPage * _lookupPageSize, totalCount);

            if (TxtLookupSummary != null)
            {
                TxtLookupSummary.Text = string.Format("Hiển thị {0} - {1} trong tổng số {2} kết quả", startItem, endItem, totalCount);
            }

            // Previous Button <
            Button btnPrev = new Button();
            btnPrev.Content = "‹";
            btnPrev.FontSize = 14;
            btnPrev.FontWeight = FontWeights.Bold;
            btnPrev.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc"));
            btnPrev.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225"));
            btnPrev.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c3c3c"));
            btnPrev.BorderThickness = new Thickness(1);
            btnPrev.Width = 28;
            btnPrev.Height = 28;
            btnPrev.Margin = new Thickness(0, 0, 4, 0);
            btnPrev.IsEnabled = (currentPage > 1);
            btnPrev.Cursor = btnPrev.IsEnabled ? Cursors.Hand : Cursors.Arrow;
            btnPrev.Click += (s, e) =>
            {
                PerformLookupSearch(query, currentPage - 1);
            };
            StackLookupPagination.Children.Add(btnPrev);

            // Compute visible page numbers
            List<int> pagesToRender = new List<int>();
            if (totalPages <= 7)
            {
                for (int i = 1; i <= totalPages; i++) pagesToRender.Add(i);
            }
            else
            {
                pagesToRender.Add(1);
                if (currentPage > 3) pagesToRender.Add(-1);

                int pStart = Math.Max(2, currentPage - 1);
                int pEnd = Math.Min(totalPages - 1, currentPage + 1);

                if (currentPage <= 3) { pStart = 2; pEnd = 4; }
                if (currentPage >= totalPages - 2) { pStart = totalPages - 3; pEnd = totalPages - 1; }

                for (int i = pStart; i <= pEnd; i++) pagesToRender.Add(i);

                if (currentPage < totalPages - 2) pagesToRender.Add(-1);
                pagesToRender.Add(totalPages);
            }

            foreach (int pNum in pagesToRender)
            {
                if (pNum == -1)
                {
                    TextBlock txtDots = new TextBlock();
                    txtDots.Text = "...";
                    txtDots.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585"));
                    txtDots.VerticalAlignment = VerticalAlignment.Center;
                    txtDots.Margin = new Thickness(4, 0, 4, 0);
                    StackLookupPagination.Children.Add(txtDots);
                }
                else
                {
                    Button btnP = new Button();
                    btnP.Content = pNum.ToString();
                    btnP.FontSize = 11.5;
                    btnP.Width = 28;
                    btnP.Height = 28;
                    btnP.Margin = new Thickness(2, 0, 2, 0);
                    btnP.Cursor = Cursors.Hand;

                    if (pNum == currentPage)
                    {
                        btnP.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007acc"));
                        btnP.Foreground = Brushes.White;
                        btnP.FontWeight = FontWeights.Bold;
                        btnP.BorderThickness = new Thickness(0);
                    }
                    else
                    {
                        btnP.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225"));
                        btnP.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc"));
                        btnP.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c3c3c"));
                        btnP.BorderThickness = new Thickness(1);
                    }

                    int targetPage = pNum;
                    btnP.Click += (s, e) =>
                    {
                        PerformLookupSearch(query, targetPage);
                    };
                    StackLookupPagination.Children.Add(btnP);
                }
            }

            // Next Button >
            Button btnNext = new Button();
            btnNext.Content = "›";
            btnNext.FontSize = 14;
            btnNext.FontWeight = FontWeights.Bold;
            btnNext.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc"));
            btnNext.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225"));
            btnNext.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3c3c3c"));
            btnNext.BorderThickness = new Thickness(1);
            btnNext.Width = 28;
            btnNext.Height = 28;
            btnNext.Margin = new Thickness(4, 0, 0, 0);
            btnNext.IsEnabled = (currentPage < totalPages);
            btnNext.Cursor = btnNext.IsEnabled ? Cursors.Hand : Cursors.Arrow;
            btnNext.Click += (s, e) =>
            {
                PerformLookupSearch(query, currentPage + 1);
            };
            StackLookupPagination.Children.Add(btnNext);
        }

        private void BtnPerformLookup_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            PerformLookupSearch(q, 1);
        }

        private void OnLookupFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            PerformLookupSearch(q, 1);
        }

        private void BtnLookupPrev_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            if (_lookupCurrentPage > 1)
            {
                PerformLookupSearch(q, _lookupCurrentPage - 1);
            }
        }

        private void BtnLookupNext_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            PerformLookupSearch(q, _lookupCurrentPage + 1);
        }

        private void TxtLookupQuery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnPerformLookup_Click(null, null);
            }
        }

        private void BtnInsertLookupResult_Click(object sender, RoutedEventArgs e)
        {
            // Retained for backward compatibility
        }

        private void BtnGlossaryMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowModalGlossary();
        }

        private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowModalSettings();
        }


        private void PopupSavesFiles_Opened(object sender, EventArgs e)
        {
            RefreshSavesFileList();
        }

        private void RefreshSavesFileList(string filterQuery = "")
        {
            if (StackSavesFileList == null) return;
            StackSavesFileList.Children.Clear();

            var allFiles = _projectService.ListAllSaveFiles();
            if (!string.IsNullOrEmpty(filterQuery))
            {
                allFiles = allFiles.FindAll(f => f.FileName.IndexOf(filterQuery, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (TxtSavesFileCount != null)
            {
                TxtSavesFileCount.Text = string.Format("{0} tệp", allFiles.Count);
            }

            if (allFiles.Count == 0)
            {
                TextBlock emptyBlock = new TextBlock
                {
                    Text = string.IsNullOrEmpty(filterQuery) ? "Thư mục saves/ đang trống" : "Không tìm thấy tệp phù hợp",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20)
                };
                StackSavesFileList.Children.Add(emptyBlock);
                return;
            }

            foreach (var file in allFiles)
            {
                var itemBorder = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    Tag = file.FullPath
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconPath = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M6 2a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6H6zm7 1.5L18.5 9H13V3.5zM6 4h5v6h6v10H6V4z"),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(file.IconColor ?? "#38bdf8")),
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconPath, 0);

                var spText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                bool isCurrentActive = !string.IsNullOrEmpty(_currentFilePath) && 
                    string.Equals(Path.GetFullPath(_currentFilePath), Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase);

                var tbName = new TextBlock
                {
                    Text = file.FileName,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isCurrentActive ? "#38bdf8" : "#ffffff")),
                    FontSize = 12.5,
                    FontWeight = isCurrentActive ? FontWeights.Bold : FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var tbMeta = new TextBlock
                {
                    Text = string.Format("{0} • {1}", file.SizeFormatted, file.ModifiedTime),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#858585")),
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                spText.Children.Add(tbName);
                spText.Children.Add(tbMeta);
                Grid.SetColumn(spText, 1);

                grid.Children.Add(iconPath);
                grid.Children.Add(spText);

                if (isCurrentActive)
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17374d")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007acc")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "Đang mở",
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8")),
                            FontSize = 10,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    Grid.SetColumn(badge, 2);
                    grid.Children.Add(badge);
                }

                itemBorder.Child = grid;

                // Mouse hover events
                itemBorder.MouseEnter += (s, ev) =>
                {
                    itemBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#04395e"));
                };
                itemBorder.MouseLeave += (s, ev) =>
                {
                    itemBorder.Background = Brushes.Transparent;
                };

                // Click event to load file
                string filePathToLoad = file.FullPath;
                itemBorder.MouseLeftButtonDown += (s, ev) =>
                {
                    ev.Handled = true;
                    if (PopupSavesFiles != null) PopupSavesFiles.IsOpen = false;
                    LoadProjectFromFile(filePathToLoad);
                };

                StackSavesFileList.Children.Add(itemBorder);
            }
        }


        private void BtnFetchEn_Click(object sender, RoutedEventArgs e)
        {
            string titleEn = TxtTitleEn != null ? TxtTitleEn.Text.Trim() : "";
            if (string.IsNullOrEmpty(titleEn) || titleEn == "Tên bài Anh...")
            {
                MessageBox.Show("Vui lòng nhập Tiêu Đề Anh trước khi tải!", "Xuất bản", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (TxtEditorContent != null)
            {
                TxtEditorContent.Text = string.Format("== {0} ==\n\nBản dịch nội dung bài viết Wiki cho {0}.\nĐang tải từ API Fandom/MediaWiki...", titleEn);
                if (TxtStatus != null) TxtStatus.Text = string.Format("Đã tải dữ liệu EN cho bài '{0}'", titleEn);
            }
        }

        private void BtnPublishVi_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProject();
        }

        private void CreateNewProject()
        {
            _currentFilePath = "";
            if (TxtTitleEn != null) TxtTitleEn.Text = "";
            if (TxtTitleVi != null) TxtTitleVi.Text = "";
            if (TxtEditorContent != null) TxtEditorContent.Text = "";
            if (TxtDisplayFileName != null) TxtDisplayFileName.Text = "Chưa tiêu đề-1.json";
            if (TxtStatus != null) TxtStatus.Text = "Đã tạo bản thảo dự án mới";
        }

        private void OpenProjectDialog()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "EtherEditor Project Files (*.json)|*.json|MediaWiki Text (*.mediawiki;*.txt)|*.mediawiki;*.txt|All Files (*.*)|*.*";
            dlg.InitialDirectory = _projectService.GetSavesDirectory();

            if (dlg.ShowDialog() == true)
            {
                LoadProjectFromFile(dlg.FileName);
            }
        }

        private void LoadProjectFromFile(string filePath)
        {
            var res = _projectService.LoadFile(filePath);
            if (res != null && res.Status == "success")
            {
                _currentFilePath = res.Path ?? filePath;
                if (TxtDisplayFileName != null) TxtDisplayFileName.Text = Path.GetFileName(_currentFilePath);

                if (res.Type == "project")
                {
                    if (TxtTitleEn != null) TxtTitleEn.Text = res.Source ?? "";
                    if (TxtTitleVi != null) TxtTitleVi.Text = res.Target ?? "";
                    if (TxtEditorContent != null) TxtEditorContent.Text = res.Target ?? res.Source ?? "";
                    if (TxtSourceContent != null) TxtSourceContent.Text = res.Source ?? "";
                }
                else
                {
                    if (TxtEditorContent != null) TxtEditorContent.Text = res.Source ?? "";
                }

                if (TxtStatus != null) TxtStatus.Text = string.Format("Đã mở tệp: {0}", Path.GetFileName(_currentFilePath));
            }
            else
            {
                MessageBox.Show(res != null ? res.Message : "Lỗi đọc tệp!", "Lỗi Mở Tệp", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveCurrentProject()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                SaveProjectAsDialog();
            }
            else
            {
                SaveToPath(_currentFilePath);
            }
        }

        private void SaveProjectAsDialog()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "EtherEditor Project JSON (*.json)|*.json|MediaWiki Text (*.mediawiki)|*.mediawiki";
            dlg.InitialDirectory = _projectService.GetSavesDirectory();
            dlg.FileName = string.IsNullOrEmpty(_currentFilePath) ? "kết_quả.json" : Path.GetFileName(_currentFilePath);

            if (dlg.ShowDialog() == true)
            {
                SaveToPath(dlg.FileName);
            }
        }

        private void SaveToPath(string filePath)
        {
            string src = TxtTitleEn != null ? TxtTitleEn.Text : "";
            string tgt = TxtEditorContent != null ? TxtEditorContent.Text : "";
            string gameId = GetSelectedGameId();

            var res = _projectService.SaveWorkspace(filePath, src, tgt, gameId);
            if (res != null && res.Status == "success")
            {
                _currentFilePath = res.Path;
                if (TxtDisplayFileName != null) TxtDisplayFileName.Text = Path.GetFileName(_currentFilePath);
                if (TxtStatus != null) TxtStatus.Text = string.Format("Đã lưu dự án với đầy đủ thuộc tính vào: {0}", Path.GetFileName(_currentFilePath));
            }
            else
            {
                MessageBox.Show(res != null ? res.Message : "Lỗi khi lưu tệp!", "Lỗi Lưu Dự Án", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region SYSTEM HEALTH & TAB MONITORING LOGIC
        private void StartHealthStatsMonitoring()
        {
            try
            {
                Process proc = Process.GetCurrentProcess();
                _lastCpuTime = proc.TotalProcessorTime;
                _lastCpuCheckTime = DateTime.UtcNow;

                _healthStatsTimer = new DispatcherTimer();
                _healthStatsTimer.Interval = TimeSpan.FromSeconds(1);
                _healthStatsTimer.Tick += HealthStatsTimer_Tick;
                _healthStatsTimer.Start();

                UpdateSystemHealthStats();
            }
            catch { }
        }

        private void HealthStatsTimer_Tick(object sender, EventArgs e)
        {
            UpdateSystemHealthStats();
        }

        private void UpdateSystemHealthStats()
        {
            try
            {
                Process proc = Process.GetCurrentProcess();

                // 1. RAM Usage
                double ramMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                if (TxtRamVal != null)
                {
                    TxtRamVal.Text = string.Format("{0:F1} MB", ramMb);
                }

                // 2. CPU Usage Calculation
                DateTime now = DateTime.UtcNow;
                TimeSpan currentCpuTime = proc.TotalProcessorTime;

                double timeWindowSeconds = (now - _lastCpuCheckTime).TotalSeconds;
                double cpuUsedSeconds = (currentCpuTime - _lastCpuTime).TotalSeconds;

                _lastCpuCheckTime = now;
                _lastCpuTime = currentCpuTime;

                double cpuPercent = 0.0;
                if (timeWindowSeconds > 0)
                {
                    cpuPercent = (cpuUsedSeconds / timeWindowSeconds / Math.Max(1, Environment.ProcessorCount)) * 100.0;
                }

                if (cpuPercent < 0) cpuPercent = 0.0;
                if (cpuPercent > 100) cpuPercent = 100.0;

                if (TxtCpuVal != null)
                {
                    TxtCpuVal.Text = string.Format("{0:F1} %", cpuPercent);
                }

                // 3. Tab Count Logic
                int tabCount = GetOpenTabCount();
                if (TxtTabVal != null)
                {
                    TxtTabVal.Text = tabCount == 1 ? "1 Tab" : string.Format("{0} Tabs", tabCount);
                }
            }
            catch { }
        }

        private string GetSessionFilePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "tabs_session.json");
        }

        private void SaveTabSession()
        {
            try
            {
                if (_activeTab != null && TxtEditorContent != null)
                {
                    _activeTab.Content = TxtEditorContent.Text;
                }

                TabSessionContainer container = new TabSessionContainer();
                container.TabCounter = _tabCounter;
                container.ActiveTabId = _activeTab != null ? _activeTab.Id : 1;

                foreach (var tab in _tabList)
                {
                    container.Tabs.Add(new TabSessionItem
                    {
                        Id = tab.Id,
                        Title = tab.Title,
                        Content = tab.Content,
                        IsActive = (tab == _activeTab)
                    });
                }

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(container);
                string sessionFile = GetSessionFilePath();
                File.WriteAllText(sessionFile, json, Encoding.UTF8);
            }
            catch { }
        }

        private string GetNextTabTitle(out int newId)
        {
            int n = 1;
            while (true)
            {
                string candidate = string.Format("Chưa tiêu đề-{0} *", n);
                bool exists = false;
                foreach (var tab in _tabList)
                {
                    if (tab != null && (tab.Title == candidate || tab.Id == n))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    newId = n;
                    return candidate;
                }
                n++;
            }
        }

        private void InitTabManager()
        {
            if (_tabList.Count > 0) return;

            string sessionFile = GetSessionFilePath();
            if (File.Exists(sessionFile))
            {
                try
                {
                    string json = File.ReadAllText(sessionFile, Encoding.UTF8);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    TabSessionContainer container = serializer.Deserialize<TabSessionContainer>(json);

                    if (container != null && container.Tabs != null && container.Tabs.Count > 0)
                    {
                        _tabCounter = Math.Max(1, container.TabCounter);
                        _tabList.Clear();

                        if (TabContainer != null)
                        {
                            TabContainer.Children.Clear();
                        }

                        TabItemData targetActiveTab = null;

                        foreach (var item in container.Tabs)
                        {
                            Border tabBorder = new Border
                            {
                                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#1e1e1e" : "#2d2d2d")),
                                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#007acc" : "#252526")),
                                BorderThickness = item.IsActive ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 1, 0),
                                Padding = new Thickness(14, 0, 12, 0),
                                VerticalAlignment = VerticalAlignment.Stretch,
                                Cursor = Cursors.Hand
                            };

                            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                            System.Windows.Shapes.Path iconPath = new System.Windows.Shapes.Path
                            {
                                Data = Geometry.Parse("M6 2a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6H6zm7 1.5L18.5 9H13V3.5zM6 4h5v6h6v10H6V4z"),
                                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#007acc" : "#6e6e6e")),
                                Width = 12,
                                Height = 12,
                                Stretch = Stretch.Uniform,
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            TextBlock titleBlock = new TextBlock { Text = item.Title, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#ffffff" : "#808080")), FontSize = 12, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
                            System.Windows.Shapes.Path closePath = new System.Windows.Shapes.Path
                            {
                                Data = Geometry.Parse("M18.3 5.71a1 1 0 0 0-1.41 0L12 10.59 7.11 5.7A1 1 0 0 0 5.7 7.11L10.59 12 5.7 16.89a1 1 0 1 0 1.41 1.41L12 13.41l4.89 4.89a1 1 0 0 0 1.41-1.41L13.41 12l4.89-4.89a1 1 0 0 0 0-1.4z"),
                                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#999999" : "#666666")),
                                Width = 9,
                                Height = 9,
                                Stretch = Stretch.Uniform
                            };
                            Border closeBorder = new Border
                            {
                                Margin = new Thickness(8, 0, 0, 0),
                                Padding = new Thickness(2),
                                Cursor = Cursors.Hand,
                                Child = closePath
                            };

                            sp.Children.Add(iconPath);
                            sp.Children.Add(titleBlock);
                            sp.Children.Add(closeBorder);
                            tabBorder.Child = sp;

                            TabItemData tabData = new TabItemData
                            {
                                Id = item.Id,
                                Title = item.Title,
                                Content = item.Content,
                                TabBorder = tabBorder,
                                IconPath = iconPath,
                                TitleBlock = titleBlock,
                                ClosePath = closePath
                            };

                            tabBorder.MouseLeftButtonDown += (s, args) => SelectTab(tabData);
                            closeBorder.MouseLeftButtonDown += (s, args) =>
                            {
                                args.Handled = true;
                                CloseTab(tabData);
                            };

                            _tabList.Add(tabData);
                            if (TabContainer != null)
                            {
                                TabContainer.Children.Add(tabBorder);
                            }

                            if (item.IsActive || item.Id == container.ActiveTabId)
                            {
                                targetActiveTab = tabData;
                            }
                        }

                        if (BtnAddTab != null && TabContainer != null)
                        {
                            TabContainer.Children.Add(BtnAddTab);
                        }

                        if (targetActiveTab == null && _tabList.Count > 0)
                        {
                            targetActiveTab = _tabList[0];
                        }

                        if (targetActiveTab != null)
                        {
                            SelectTab(targetActiveTab);
                            return;
                        }
                    }
                }
                catch { }
            }

            TabItemData defaultTab = new TabItemData
            {
                Id = 1,
                Title = "Chưa tiêu đề-1 *",
                Content = TxtEditorContent != null ? TxtEditorContent.Text : "",
                TabBorder = FirstTabBorder,
                IconPath = FirstTabIcon,
                TitleBlock = FirstTabText,
                ClosePath = FirstTabClose
            };

            if (FirstTabBorder != null)
            {
                FirstTabBorder.MouseLeftButtonDown += (s, e) => SelectTab(defaultTab);
            }

            _tabList.Add(defaultTab);
            _activeTab = defaultTab;
            SelectTab(defaultTab);
        }

        private void SelectTab(TabItemData targetTab)
        {
            if (targetTab == null) return;

            if (_activeTab != null && TxtEditorContent != null && _activeTab != targetTab)
            {
                _activeTab.Content = TxtEditorContent.Text;
            }

            _activeTab = targetTab;

            if (TxtEditorContent != null)
            {
                TxtEditorContent.Text = targetTab.Content ?? "";
            }

            foreach (var tab in _tabList)
            {
                bool isActive = (tab == targetTab);
                if (tab.TabBorder != null)
                {
                    tab.TabBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#1e1e1e" : "#2d2d2d"));
                    tab.TabBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#007acc" : "#252526"));
                    tab.TabBorder.BorderThickness = isActive ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 1, 0);
                }
                if (tab.IconPath != null)
                {
                    tab.IconPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#007acc" : "#6e6e6e"));
                }
                if (tab.TitleBlock != null)
                {
                    tab.TitleBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#ffffff" : "#808080"));
                    tab.TitleBlock.FontWeight = FontWeights.Normal;
                }
                if (tab.ClosePath != null)
                {
                    tab.ClosePath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#999999" : "#666666"));
                }
            }

            UpdateSystemHealthStats();
            SaveTabSession();
        }

        private void CloseTab(TabItemData tabToClose)
        {
            if (tabToClose == null || !_tabList.Contains(tabToClose)) return;

            int index = _tabList.IndexOf(tabToClose);
            bool isClosingActive = (_activeTab == tabToClose);

            _tabList.Remove(tabToClose);

            if (tabToClose.TabBorder != null && TabContainer != null)
            {
                TabContainer.Children.Remove(tabToClose.TabBorder);
            }

            if (isClosingActive)
            {
                if (_tabList.Count > 0)
                {
                    int targetIndex = (index > 0) ? index - 1 : 0;
                    SelectTab(_tabList[targetIndex]);
                }
                else
                {
                    _activeTab = null;
                    BtnAddTab_Click(null, null);
                }
            }
            else
            {
                UpdateSystemHealthStats();
                SaveTabSession();
            }
        }

        private int GetOpenTabCount()
        {
            return Math.Max(1, _tabList.Count);
        }

        private void BtnAddTab_Click(object sender, RoutedEventArgs e)
        {
            if (TabContainer == null) return;

            if (_tabList.Count == 0)
            {
                InitTabManager();
            }

            int tabId;
            string tabTitle = GetNextTabTitle(out tabId);
            _tabCounter = Math.Max(_tabCounter, tabId);

            Border tabBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2d2d2d")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526")),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(14, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.Hand
            };

            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Shapes.Path iconPath = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6 2a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6H6zm7 1.5L18.5 9H13V3.5zM6 4h5v6h6v10H6V4z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6e6e6e")),
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock titleBlock = new TextBlock { Text = tabTitle, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")), FontSize = 12, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Shapes.Path closePath = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M18.3 5.71a1 1 0 0 0-1.41 0L12 10.59 7.11 5.7A1 1 0 0 0 5.7 7.11L10.59 12 5.7 16.89a1 1 0 1 0 1.41 1.41L12 13.41l4.89 4.89a1 1 0 0 0 1.41-1.41L13.41 12l4.89-4.89a1 1 0 0 0 0-1.4z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
                Width = 9,
                Height = 9,
                Stretch = Stretch.Uniform
            };
            Border closeBorder = new Border
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = closePath
            };

            sp.Children.Add(iconPath);
            sp.Children.Add(titleBlock);
            sp.Children.Add(closeBorder);
            tabBorder.Child = sp;

            TabItemData newTab = new TabItemData
            {
                Id = tabId,
                Title = tabTitle,
                Content = string.Format("-- Document {0} --\n\n-- Nội dung nháp {0} --\n", tabId),
                TabBorder = tabBorder,
                IconPath = iconPath,
                TitleBlock = titleBlock,
                ClosePath = closePath
            };

            tabBorder.MouseLeftButtonDown += (s, args) => SelectTab(newTab);

            closeBorder.MouseLeftButtonDown += (s, args) =>
            {
                args.Handled = true;
                CloseTab(newTab);
            };

            _tabList.Add(newTab);

            int insertIndex = TabContainer.Children.Count - 1;
            if (insertIndex < 0) insertIndex = 0;
            TabContainer.Children.Insert(insertIndex, tabBorder);

            SelectTab(newTab);
        }

        private void BtnCloseFirstTab_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_tabList.Count > 0)
            {
                CloseTab(_tabList[0]);
            }
        }
        #endregion
    }
}
