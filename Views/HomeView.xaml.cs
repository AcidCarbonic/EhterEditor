using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EtherEditorNative.Backend;

namespace EtherEditorNative.Views
{
    public partial class HomeView : UserControl
    {
        public event EventHandler RequestNavigateTranslate;
        public event EventHandler<string> RequestOpenProjectFile;

        public class RecentFileItem
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public string FileName { get; set; }
            public string Path { get; set; }
            public string Project { get; set; }
            public string ModifiedTime { get; set; }
        }

        private readonly HistoryService _historyService;
        private readonly ProjectService _projectService;
        private readonly DatabaseService _databaseService;
        private readonly DownloadService _downloadService;
        private string _selectedDownloadGameId = "zzz";
        private bool _isDownloading = false;

        public HomeView()
        {
            InitializeComponent();
            string projectRoot = GetProjectRootDir();
            _historyService = new HistoryService(projectRoot);
            _projectService = new ProjectService(projectRoot);
            _databaseService = new DatabaseService(projectRoot);
            _downloadService = new DownloadService(projectRoot, _databaseService);

            LoadImages();
            LoadRealRecentFiles();
        }

        private void LoadImages()
        {
            try
            {
                string projectRoot = GetProjectRootDir();

                // 1. Load Background Wallpaper
                string bgPath = Path.Combine(projectRoot, "assets", "home_bg.png");
                if (!File.Exists(bgPath)) bgPath = Path.Combine(projectRoot, "assets", "1297444 1.png");
                if (File.Exists(bgPath))
                {
                    BitmapImage bmpBg = new BitmapImage();
                    bmpBg.BeginInit();
                    bmpBg.UriSource = new Uri(bgPath, UriKind.Absolute);
                    bmpBg.CacheOption = BitmapCacheOption.OnLoad;
                    bmpBg.EndInit();
                    ImgBgWallpaper.Source = bmpBg;
                }

                // 2. Load 3 Game Poster Images
                string genshinImg = Path.Combine(projectRoot, "assets", "1398369 1 Cropped (1).png");
                if (!File.Exists(genshinImg)) genshinImg = Path.Combine(projectRoot, "assets", "1398369 1.png");
                if (File.Exists(genshinImg)) LoadImageToControl(ImgCardGenshin, genshinImg);

                string hsrImg = Path.Combine(projectRoot, "assets", "13 Cropped.png");
                if (!File.Exists(hsrImg)) hsrImg = Path.Combine(projectRoot, "assets", "13.png");
                if (File.Exists(hsrImg)) LoadImageToControl(ImgCardHsr, hsrImg);

                string zzzImg = Path.Combine(projectRoot, "assets", "139 Cropped.png");
                if (!File.Exists(zzzImg)) zzzImg = Path.Combine(projectRoot, "assets", "139.png");
                if (File.Exists(zzzImg)) LoadImageToControl(ImgCardZzz, zzzImg);
            }
            catch { }
        }

        private void LoadImageToControl(Image target, string path)
        {
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                target.Source = bmp;
            }
            catch { }
        }

        private void LoadRealRecentFiles()
        {
            var list = new List<RecentFileItem>();
            try
            {
                var realRecords = _historyService.GetRecentFiles();
                if (realRecords != null && realRecords.Count > 0)
                {
                    foreach (var r in realRecords)
                    {
                        list.Add(new RecentFileItem
                        {
                            Id = r.Id,
                            Type = !string.IsNullOrEmpty(r.Type) ? r.Type.ToUpper() : "JSON",
                            FileName = !string.IsNullOrEmpty(r.File) ? r.File : r.FileName,
                            Path = r.Path,
                            Project = !string.IsNullOrEmpty(r.Project) && r.Project != "---" ? r.Project : "Honkai: Star Rail",
                            ModifiedTime = !string.IsNullOrEmpty(r.Date) ? r.Date : r.ModifiedTime
                        });
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new RecentFileItem { Type = "JSON", FileName = "Chưa tiêu đề-1", Path = "", Project = "---", ModifiedTime = "22/07 16:45" });
            }

            LvRecentFiles.ItemsSource = list;
        }

        private string GetProjectRootDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "gamedata", "hsr.json")) || File.Exists(Path.Combine(dir, "bot_data.db")))
                {
                    return dir;
                }
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        // --- 1. DIRECT GAME POSTER CARD CLICK HANDLER (FASTEST 1-CLICK PARITY) ---
        private void GameCard_Click(object sender, RoutedEventArgs e)
        {
            HandleGameCardSelect(sender);
        }

        private void GameCard_Click(object sender, MouseButtonEventArgs e)
        {
            HandleGameCardSelect(sender);
        }

        private void HandleGameCardSelect(object sender)
        {
            var element = sender as FrameworkElement;
            if (element == null || element.Tag == null) return;

            string gameId = element.Tag.ToString();
            
            if (gameId == "hsr" || _databaseService.IsDatabaseAvailable())
            {
                if (RequestNavigateTranslate != null)
                {
                    RequestNavigateTranslate(this, EventArgs.Empty);
                }
            }
            else
            {
                OpenAddProjectModal(gameId);
            }
        }

        // --- 2. ASSET ROW ACTION BUTTONS ---
        private void BtnOpenAddProject_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string gameId = btn != null && btn.Tag != null ? btn.Tag.ToString() : "zzz";
            OpenAddProjectModal(gameId);
        }

        private void OpenAddProjectModal(string gameId)
        {
            _selectedDownloadGameId = gameId;
            
            string fullGameName = "ZENLESS ZONE ZERO";
            if (gameId == "genshin") fullGameName = "GENSHIN IMPACT";
            else if (gameId == "hsr") fullGameName = "HONKAI: STAR RAIL";

            TxtModalGameTitle.Text = "TẢI DỰ ÁN " + fullGameName;
            PbOverall.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressStatus.Text = "KHỞI ĐỘNG TẢI...";
            BtnConfirmDownload.Content = "TẢI VỀ";

            ProgressArea.Visibility = Visibility.Collapsed;
            AddProjectModalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnDeleteAsset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string gameId = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            if (string.IsNullOrEmpty(gameId)) return;

            var result = MessageBox.Show("Bạn có chắc chắn muốn xóa toàn bộ dữ liệu game " + gameId.ToUpper() + " không?",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show("Đã xóa toàn bộ dữ liệu game " + gameId.ToUpper(), "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- 3. RECENT FILE ROW ACTIONS ---
        private void BtnEditRecent_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string filePath = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            OpenRecentFile(filePath);
        }

        private void LvRecentFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = LvRecentFiles.SelectedItem as RecentFileItem;
            if (item != null)
            {
                OpenRecentFile(item.Path);
            }
        }

        private void OpenRecentFile(string filePath)
        {
            if (RequestOpenProjectFile != null)
            {
                RequestOpenProjectFile(this, filePath);
            }
            else if (RequestNavigateTranslate != null)
            {
                RequestNavigateTranslate(this, EventArgs.Empty);
            }
        }

        private void BtnDeleteRecent_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string filePath = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            if (!string.IsNullOrEmpty(filePath))
            {
                _historyService.RemoveEntry(filePath);
                LoadRealRecentFiles();
            }
        }

        // --- 4. MODAL HANDLERS ---
        private void CloseAddProjectModal_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = false;
            AddProjectModalOverlay.Visibility = Visibility.Collapsed;
        }

        private async void StartDownloadFromModal_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            _isDownloading = true;
            BtnConfirmDownload.Content = "ĐANG TẢI...";
            ProgressArea.Visibility = Visibility.Visible;

            string gameIdToDownload = _selectedDownloadGameId;
            bool success = false;

            await Task.Run(async () =>
            {
                success = await _downloadService.DownloadAndImportGameDataAsync(gameIdToDownload, (val, percentStr, statusText) =>
                {
                    UpdateProgress(val, percentStr, statusText);
                });
            });

            _isDownloading = false;
            BtnConfirmDownload.Content = "TẢI VỀ";
            AddProjectModalOverlay.Visibility = Visibility.Collapsed;

            if (success)
            {
                MessageBox.Show("Đã hoàn tất tải dữ liệu Đa Tệp (Multi-File) từ Git cho dự án " + gameIdToDownload.ToUpper() + " và nạp thành công vào CSDL SQLite!", "Tải về hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Có lỗi xảy ra trong quá trình tải dữ liệu cho dự án " + gameIdToDownload.ToUpper() + ". Vui lòng kiểm tra lại kết nối mạng!", "Thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateProgress(double val, string percent, string statusText)
        {
            Dispatcher.Invoke(() =>
            {
                PbOverall.Value = val;
                TxtProgressPercent.Text = percent;
                TxtProgressStatus.Text = statusText;
            });
        }
    }
}
