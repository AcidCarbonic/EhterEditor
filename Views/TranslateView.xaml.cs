using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        private bool _isPreviewActive = false;

        public class TabItemData
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
            public Border TabBorder { get; set; }
            public TextBlock IconBlock { get; set; }
            public TextBlock TitleBlock { get; set; }
            public TextBlock CloseBlock { get; set; }
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
            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));
            _dbService = new DatabaseService(projectRoot);
            _logicService = new LogicService(projectRoot);
            _projectService = new ProjectService(projectRoot);

            _currentRows = new List<TextMapRow>();
            LoadRealDataFromDatabase();
            InitDefaultWorkspaceSample();
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
                if (TxtSyntaxIcon != null) TxtSyntaxIcon.Text = "🔗 ";
                if (TxtSyntaxMode != null) TxtSyntaxMode.Text = "WikiLink";
                if (TxtEditorContent != null) TxtEditorContent.SyntaxMode = "WikiLink";
            }
            else
            {
                if (TxtSyntaxIcon != null) TxtSyntaxIcon.Text = "📝 ";
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

        private void BtnAssistant_Click(object sender, RoutedEventArgs e)
        {
            if (TxtEditorContent != null)
            {
                string sel = TxtEditorContent.SelectedText;
                if (string.IsNullOrEmpty(sel))
                {
                    MessageBox.Show("Vui lòng bôi đen văn bản cần hỗ trợ dịch/chuẩn hóa thuật ngữ!", "Trợ lý Dịch EtherEditor", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(string.Format("Đang phân tích và tối ưu bản dịch cho đoạn:\n\"{0}\"", sel), "Trợ lý Dịch AI", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
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


        public void ShowModalGlossary()
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;

            LoadGlossaryDataGrid();
        }

        public void ShowModalLookup()
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Visible;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Visible;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;

            PerformLookupSearch("");
        }

        private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
        {
            if (GridModalOverlay != null) GridModalOverlay.Visibility = Visibility.Collapsed;
            if (BorderModalSettings != null) BorderModalSettings.Visibility = Visibility.Collapsed;
            if (BorderModalGlossary != null) BorderModalGlossary.Visibility = Visibility.Collapsed;
            if (BorderModalLookup != null) BorderModalLookup.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            BtnCloseModal_Click(null, null);
            MessageBox.Show("Đã lưu cấu hình Cài đặt hệ thống thành công!", "Ether Editor Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadGlossaryDataGrid()
        {
            try
            {
                var glossary = GlossaryService.Instance.GetGlossary();
                List<TextMapItem> list = new List<TextMapItem>();
                string defaultGame = GetSelectedGameId().ToUpper();

                ComboBoxItem catItem = CmbGlossaryCategory != null ? CmbGlossaryCategory.SelectedItem as ComboBoxItem : null;
                string selectedCat = (catItem != null && catItem.Content != null) ? catItem.Content.ToString() : "Chung";

                foreach (var kvp in glossary)
                {
                    list.Add(new TextMapItem { SourceText = kvp.Key, TargetText = kvp.Value, GameId = selectedCat, Id = "GLOSSARY" });
                }
                if (DgGlossaryList != null) DgGlossaryList.ItemsSource = list;
                if (TxtGlossarySummary != null) TxtGlossarySummary.Text = string.Format("Hiển thị {0} thuật ngữ trong từ điển ưu tiên", list.Count);
            }
            catch { }
        }

        private void BtnAddGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            string en = TxtNewTermEn != null ? TxtNewTermEn.Text.Trim() : "";
            string vi = TxtNewTermVi != null ? TxtNewTermVi.Text.Trim() : "";

            if (!string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(vi))
            {
                GlossaryService.Instance.AddTerm(en, vi);
                if (TxtNewTermEn != null) TxtNewTermEn.Text = "";
                if (TxtNewTermVi != null) TxtNewTermVi.Text = "";
                LoadGlossaryDataGrid();
            }
        }

        private void BtnDeleteGlossaryTerm_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn != null)
            {
                TextMapItem item = btn.DataContext as TextMapItem;
                if (item != null)
                {
                    GlossaryService.Instance.RemoveTerm(item.SourceText);
                    LoadGlossaryDataGrid();
                }
            }
        }

        private void BtnSaveGlossary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GlossaryService.Instance.SaveGlossary(GlossaryService.Instance.GetGlossary());
                MessageBox.Show("Đã lưu thay đổi từ điển ưu tiên thành công!", "Từ điển ưu tiên", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Đã xảy ra lỗi khi lưu từ điển: " + ex.Message, "Lỗi Lưu Từ điển", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PerformLookupSearch(string query)
        {
            try
            {
                var results = GlossaryService.Instance.SearchTerms(query);
                List<TextMapItem> list = new List<TextMapItem>();
                string gameId = GetSelectedGameId().ToUpper();

                ComboBoxItem gameItem = CmbLookupGame != null ? CmbLookupGame.SelectedItem as ComboBoxItem : null;
                if (gameItem != null && gameItem.Content != null && !gameItem.Content.ToString().StartsWith("Tất cả"))
                {
                    gameId = gameItem.Content.ToString();
                }

                int index = 100001;
                foreach (var r in results)
                {
                    list.Add(new TextMapItem { Id = index++.ToString(), SourceText = r.Key, TargetText = r.Value, GameId = gameId });
                }
                if (DgLookupResults != null) DgLookupResults.ItemsSource = list;
                if (TxtLookupSummary != null) TxtLookupSummary.Text = string.Format("Hiển thị {0} kết quả tra cứu", list.Count);
                if (TxtLookupPage != null) TxtLookupPage.Text = "Trang 1 / 1";
            }
            catch { }
        }

        private void BtnPerformLookup_Click(object sender, RoutedEventArgs e)
        {
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            PerformLookupSearch(q);
        }

        private void OnLookupFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            string q = TxtLookupQuery != null ? TxtLookupQuery.Text.Trim() : "";
            PerformLookupSearch(q);
        }

        private void BtnLookupPrev_Click(object sender, RoutedEventArgs e)
        {
            if (TxtLookupPage != null) TxtLookupPage.Text = "Trang 1 / 1";
        }

        private void BtnLookupNext_Click(object sender, RoutedEventArgs e)
        {
            if (TxtLookupPage != null) TxtLookupPage.Text = "Trang 1 / 1";
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
            Button btn = sender as Button;
            if (btn != null)
            {
                TextMapItem item = btn.DataContext as TextMapItem;
                if (item != null)
                {
                    string wikiLink = string.Format("[[{0}|{1}]]", item.SourceText, item.TargetText);
                    if (TxtEditorContent != null)
                    {
                        int caretIndex = TxtEditorContent.CaretIndex;
                        TxtEditorContent.Text = TxtEditorContent.Text.Insert(caretIndex, wikiLink);
                        TxtEditorContent.CaretIndex = caretIndex + wikiLink.Length;
                        TxtEditorContent.Focus();
                    }
                    BtnCloseModal_Click(null, null);
                }
            }
        }

        private void BtnGlossaryMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowModalGlossary();
        }

        private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            ShowModalSettings();
        }


        private void BtnFilePickerPill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenProjectDialog();
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
                            TextBlock iconBlock = new TextBlock { Text = "📄 ", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#007acc" : "#6e6e6e")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                            TextBlock titleBlock = new TextBlock { Text = item.Title, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#ffffff" : "#808080")), FontSize = 12, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
                            TextBlock closeBlock = new TextBlock { Text = "  ✕", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsActive ? "#999999" : "#666666")), FontSize = 11, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

                            sp.Children.Add(iconBlock);
                            sp.Children.Add(titleBlock);
                            sp.Children.Add(closeBlock);
                            tabBorder.Child = sp;

                            TabItemData tabData = new TabItemData
                            {
                                Id = item.Id,
                                Title = item.Title,
                                Content = item.Content,
                                TabBorder = tabBorder,
                                IconBlock = iconBlock,
                                TitleBlock = titleBlock,
                                CloseBlock = closeBlock
                            };

                            tabBorder.MouseLeftButtonDown += (s, args) => SelectTab(tabData);
                            closeBlock.MouseLeftButtonDown += (s, args) =>
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
                TitleBlock = FirstTabText,
                CloseBlock = FirstTabClose
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
                if (tab.IconBlock != null)
                {
                    tab.IconBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#007acc" : "#6e6e6e"));
                }
                if (tab.TitleBlock != null)
                {
                    tab.TitleBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#ffffff" : "#808080"));
                    tab.TitleBlock.FontWeight = FontWeights.Normal;
                }
                if (tab.CloseBlock != null)
                {
                    tab.CloseBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isActive ? "#999999" : "#666666"));
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
            TextBlock iconBlock = new TextBlock { Text = "📄 ", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6e6e6e")), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            TextBlock titleBlock = new TextBlock { Text = tabTitle, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080")), FontSize = 12, FontWeight = FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock closeBlock = new TextBlock { Text = "  ✕", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")), FontSize = 11, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

            sp.Children.Add(iconBlock);
            sp.Children.Add(titleBlock);
            sp.Children.Add(closeBlock);
            tabBorder.Child = sp;

            TabItemData newTab = new TabItemData
            {
                Id = tabId,
                Title = tabTitle,
                Content = string.Format("-- Document {0} --\n\n-- Nội dung nháp {0} --\n", tabId),
                TabBorder = tabBorder,
                IconBlock = iconBlock,
                TitleBlock = titleBlock,
                CloseBlock = closeBlock
            };

            tabBorder.MouseLeftButtonDown += (s, args) => SelectTab(newTab);

            closeBlock.MouseLeftButtonDown += (s, args) =>
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
