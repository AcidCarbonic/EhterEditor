using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EtherEditorNative.Backend;

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
        private List<TextMapRow> _currentRows;

        public TranslateView()
        {
            InitializeComponent();
            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));
            _dbService = new DatabaseService(projectRoot);
            _logicService = new LogicService(projectRoot);

            _currentRows = new List<TextMapRow>();
            LoadRealDataFromDatabase();
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

                    DgTextMap.ItemsSource = null;
                    DgTextMap.ItemsSource = _currentRows;
                    TxtStatus.Text = string.Format("Đã nạp {0}/{1} câu từ CSDL SQLite bot_data.db ({2})", 
                        _currentRows.Count, result != null ? result.TotalCount : 0, gameId.ToUpper());
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("TranslateView Data Load Error: " + ex.Message);
            }

            // Fallback to sample rows if SQLite is not populated yet
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

            DgTextMap.ItemsSource = _currentRows;
            TxtStatus.Text = string.Format("Hiển thị {0} dòng câu dịch mẫu (C# Native)", _currentRows.Count);
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
            string query = TxtSearchInput.Text != null ? TxtSearchInput.Text.Trim() : "";
            if (query == "nhập từ khóa tìm kiếm textmap...") query = "";

            string selectedGame = "hsr";
            if (CmbGameSelect != null)
            {
                var item = CmbGameSelect.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                {
                    selectedGame = item.Tag.ToString();
                }
            }

            LoadRealDataFromDatabase(query, selectedGame);
        }

        private void CmbGameSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                PerformSearch();
            }
        }
    }
}
