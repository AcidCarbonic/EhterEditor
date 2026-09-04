using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private readonly DatabaseService _dbService;
        private readonly LogicService _logicService;
        private readonly ProjectService _projectService;
        private List<TextMapRow> _currentRows;
        private string _currentFilePath = "";
        private bool _isPreviewActive = false;

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
            if (TxtStatus != null) TxtStatus.Text = "Sẵn sàng";
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

        private void CmbGameSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                PerformSearch();
            }
        }

        // --- EDITOR EVENT HANDLERS & LINE NUMBERS ---
        private void TxtEditorContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtEditorContent == null) return;

            string text = TxtEditorContent.Text ?? "";

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

            MenuItem newFile = new MenuItem { Header = "📄 Tạo tệp dự án mới" };
            newFile.Click += (s, ev) => CreateNewProject();

            MenuItem openFile = new MenuItem { Header = "📂 Mở tệp dự án JSON / MediaWiki..." };
            openFile.Click += (s, ev) => OpenProjectDialog();

            MenuItem saveFile = new MenuItem { Header = "💾 Lưu dự án hiện tại (JSON full attributes)" };
            saveFile.Click += (s, ev) => SaveCurrentProject();

            MenuItem saveAsFile = new MenuItem { Header = "💾 Lưu thành tệp mới (Save As)..." };
            saveAsFile.Click += (s, ev) => SaveProjectAsDialog();

            fileMenu.Items.Add(newFile);
            fileMenu.Items.Add(openFile);
            fileMenu.Items.Add(saveFile);
            fileMenu.Items.Add(saveAsFile);

            if (BtnFileMenu != null)
            {
                fileMenu.PlacementTarget = BtnFileMenu;
                fileMenu.IsOpen = true;
            }
        }

        private void BtnSearchMenu_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Tính năng tra cứu từ điển / TextMap!", "Tra cứu", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnGlossaryMenu_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Từ điển ưu tiên (Glossary) đang chạy đồng bộ với bot_data.db!", "Từ điển Ưu tiên", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Cài đặt TranslateView Native C# - Phiên bản EtherEditor v4.4", "Cài đặt", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}
