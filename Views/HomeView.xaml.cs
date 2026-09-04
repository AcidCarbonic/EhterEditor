using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
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
        private string _targetRenameFilePath = "";
        private string _deleteMode = "";
        private string _targetDeleteFilePath = "";
        private string _targetDeleteGameId = "";

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
            LoadFandomWikiStatsAsync();
        }

        private async void LoadFandomWikiStatsAsync(bool forceRefresh = false)
        {
            try
            {
                int giEng = await FandomConverterService.GetFandomArticleCountCachedAsync("genshin-impact.fandom.com", "/", forceRefresh);
                int giVi  = await FandomConverterService.GetFandomArticleCountCachedAsync("genshin-impact.fandom.com", "/vi/", forceRefresh);

                int hsrEng = await FandomConverterService.GetFandomArticleCountCachedAsync("honkai-star-rail.fandom.com", "/", forceRefresh);
                int hsrVi  = await FandomConverterService.GetFandomArticleCountCachedAsync("honkai-star-rail.fandom.com", "/vi/", forceRefresh);

                int zzzEng = await FandomConverterService.GetFandomArticleCountCachedAsync("zenless-zone-zero.fandom.com", "/", forceRefresh);
                int zzzVi  = await FandomConverterService.GetFandomArticleCountCachedAsync("zenless-zone-zero.fandom.com", "/vi/", forceRefresh);

                Dispatcher.Invoke(() =>
                {
                    if (giEng > 0)  TxtGiEngArticles.Text  = giEng >= 1000 ? (giEng / 1000.0).ToString("0.0") + "K" : giEng.ToString();
                    if (giVi > 0)   TxtGiViArticles.Text   = giVi >= 1000 ? (giVi / 1000.0).ToString("0.0") + "K" : giVi.ToString();
                    if (hsrEng > 0) TxtHsrEngArticles.Text = hsrEng >= 1000 ? (hsrEng / 1000.0).ToString("0.0") + "K" : hsrEng.ToString();
                    if (hsrVi > 0)  TxtHsrViArticles.Text  = hsrVi >= 1000 ? (hsrVi / 1000.0).ToString("0.0") + "K" : hsrVi.ToString();
                    if (zzzEng > 0) TxtZzzEngArticles.Text = zzzEng >= 1000 ? (zzzEng / 1000.0).ToString("0.0") + "K" : zzzEng.ToString();
                    if (zzzVi > 0)  TxtZzzViArticles.Text  = zzzVi >= 1000 ? (zzzVi / 1000.0).ToString("0.0") + "K" : zzzVi.ToString();

                    // Scale dynamically using WPF Grid Star Columns (Max scale = 50,000)
                    double maxScale = 50000.0;
                    if (giEng > 0 || hsrEng > 0 || zzzEng > 0)
                    {
                        double valGiEng = Math.Min(maxScale, (double)giEng);
                        ColGiEngArticlesBar.Width = new GridLength(valGiEng, GridUnitType.Star);
                        ColGiEngArticlesRest.Width = new GridLength(maxScale - valGiEng, GridUnitType.Star);

                        double valGiVi = Math.Min(maxScale, (double)giVi);
                        ColGiViArticlesBar.Width = new GridLength(valGiVi, GridUnitType.Star);
                        ColGiViArticlesRest.Width = new GridLength(maxScale - valGiVi, GridUnitType.Star);

                        double valHsrEng = Math.Min(maxScale, (double)hsrEng);
                        ColHsrEngArticlesBar.Width = new GridLength(valHsrEng, GridUnitType.Star);
                        ColHsrEngArticlesRest.Width = new GridLength(maxScale - valHsrEng, GridUnitType.Star);

                        double valHsrVi = Math.Min(maxScale, (double)hsrVi);
                        ColHsrViArticlesBar.Width = new GridLength(valHsrVi, GridUnitType.Star);
                        ColHsrViArticlesRest.Width = new GridLength(maxScale - valHsrVi, GridUnitType.Star);

                        double valZzzEng = Math.Min(maxScale, (double)zzzEng);
                        ColZzzEngArticlesBar.Width = new GridLength(valZzzEng, GridUnitType.Star);
                        ColZzzEngArticlesRest.Width = new GridLength(maxScale - valZzzEng, GridUnitType.Star);

                        double valZzzVi = Math.Min(maxScale, (double)zzzVi);
                        ColZzzViArticlesBar.Width = new GridLength(valZzzVi, GridUnitType.Star);
                        ColZzzViArticlesRest.Width = new GridLength(maxScale - valZzzVi, GridUnitType.Star);
                    }
                });
            }
            catch { }
        }

        private void AnimateBarsOnly(params UIElement[] barElements)
        {
            foreach (var element in barElements)
            {
                if (element == null) continue;
                try
                {
                    var scale = new ScaleTransform(0.0, 1.0);
                    element.RenderTransformOrigin = new Point(0, 0.5);
                    element.RenderTransform = scale;

                    var scaleAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(320),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };

                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                }
                catch { }
            }
        }

        private void BtnFandomTabArticles_Click(object sender, RoutedEventArgs e)
        {
            if (PanelFandomArticles.Visibility == Visibility.Visible) return;

            PanelFandomArticles.Visibility = Visibility.Visible;
            PanelFandomUsers.Visibility = Visibility.Collapsed;
            AnimateBarsOnly(BarsRowGiArticles, BarsRowHsrArticles, BarsRowZzzArticles);

            BtnFandomTabArticles.Cursor = Cursors.Arrow;
            BtnFandomTabUsers.Cursor = Cursors.Hand;

            BtnFandomTabArticles.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
            BtnFandomTabArticles.FontWeight = FontWeights.Bold;

            BtnFandomTabUsers.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            BtnFandomTabUsers.FontWeight = FontWeights.SemiBold;

            if (LineTabArticles != null) LineTabArticles.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
            if (LineTabUsers != null) LineTabUsers.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"));

            if (LegendViBox != null) LegendViBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
        }

        private void BtnFandomTabUsers_Click(object sender, RoutedEventArgs e)
        {
            if (PanelFandomUsers.Visibility == Visibility.Visible) return;

            PanelFandomArticles.Visibility = Visibility.Collapsed;
            PanelFandomUsers.Visibility = Visibility.Visible;
            AnimateBarsOnly(BarsRowGiUsers, BarsRowHsrUsers, BarsRowZzzUsers);

            BtnFandomTabUsers.Cursor = Cursors.Arrow;
            BtnFandomTabArticles.Cursor = Cursors.Hand;

            BtnFandomTabUsers.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10b981"));
            BtnFandomTabUsers.FontWeight = FontWeights.Bold;

            BtnFandomTabArticles.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            BtnFandomTabArticles.FontWeight = FontWeights.SemiBold;

            if (LineTabUsers != null) LineTabUsers.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10b981"));
            if (LineTabArticles != null) LineTabArticles.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1e293b"));

            if (LegendViBox != null) LegendViBox.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10b981"));
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

                // 2. Load 3 Game Poster Images (Grayscale if database not loaded)
                string genshinImg = Path.Combine(projectRoot, "assets", "1398369 1 Cropped (1).png");
                if (!File.Exists(genshinImg)) genshinImg = Path.Combine(projectRoot, "assets", "1398369 1.png");
                if (File.Exists(genshinImg)) LoadImageToBrush(ImgBrushGenshin, genshinImg, isGrayscale: true);

                string hsrImg = Path.Combine(projectRoot, "assets", "13 Cropped.png");
                if (!File.Exists(hsrImg)) hsrImg = Path.Combine(projectRoot, "assets", "13.png");
                if (File.Exists(hsrImg)) LoadImageToBrush(ImgBrushHsr, hsrImg, isGrayscale: false);

                string zzzImg = Path.Combine(projectRoot, "assets", "139 Cropped.png");
                if (!File.Exists(zzzImg)) zzzImg = Path.Combine(projectRoot, "assets", "139.png");
                if (File.Exists(zzzImg)) LoadImageToBrush(ImgBrushZzz, zzzImg, isGrayscale: true);
            }
            catch { }
        }

        private void LoadImageToBrush(ImageBrush targetBrush, string path, bool isGrayscale = false)
        {
            if (targetBrush == null) return;
            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                if (isGrayscale)
                {
                    FormatConvertedBitmap grayBmp = new FormatConvertedBitmap();
                    grayBmp.BeginInit();
                    grayBmp.Source = bmp;
                    grayBmp.DestinationFormat = PixelFormats.Gray8;
                    grayBmp.EndInit();
                    targetBrush.ImageSource = grayBmp;
                    targetBrush.Opacity = 0.55;
                }
                else
                {
                    targetBrush.ImageSource = bmp;
                    targetBrush.Opacity = 1.0;
                }
            }
            catch { }
        }

        private void LoadRealRecentFiles()
        {
            var list = new List<RecentFileItem>();
            try
            {
                // 1. Scan physical files in <projectRoot>/saves folder
                string savesDir = Path.Combine(GetProjectRootDir(), "saves");
                if (!Directory.Exists(savesDir))
                {
                    Directory.CreateDirectory(savesDir);
                }

                string[] physicalFiles = Directory.GetFiles(savesDir);
                foreach (string filePath in physicalFiles)
                {
                    FileInfo fi = new FileInfo(filePath);
                    string ext = fi.Extension.TrimStart('.').ToUpper();
                    if (string.IsNullOrEmpty(ext)) ext = "JSON";

                    list.Add(new RecentFileItem
                    {
                        Id = fi.Name,
                        Type = ext,
                        FileName = fi.Name,
                        Path = fi.FullName,
                        Project = "saves",
                        ModifiedTime = fi.LastWriteTime.ToString("dd/MM HH:mm")
                    });
                }

                // 2. Load history records from HistoryService
                var realRecords = _historyService.GetRecentFiles();
                if (realRecords != null && realRecords.Count > 0)
                {
                    foreach (var r in realRecords)
                    {
                        if (!string.IsNullOrEmpty(r.Path) && File.Exists(r.Path) && !list.Exists(x => x.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase)))
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
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new RecentFileItem { Type = "JSON", FileName = "Chưa có tệp bản thảo nào trong saves", Path = "", Project = "saves", ModifiedTime = DateTime.Now.ToString("dd/MM HH:mm") });
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

        private ImageBrush GetCardImageBrush(Grid cardGrid)
        {
            if (cardGrid == null) return null;
            string tag = cardGrid.Tag != null ? cardGrid.Tag.ToString() : "";
            if (tag == "hsr") return ImgBrushHsr;
            if (tag == "genshin") return ImgBrushGenshin;
            if (tag == "zzz") return ImgBrushZzz;
            return null;
        }

        private void GameCard_MouseEnter(object sender, MouseEventArgs e)
        {
            var grid = sender as Grid;
            ImageBrush brush = GetCardImageBrush(grid);
            if (brush != null)
            {
                ScaleTransform scale = brush.RelativeTransform as ScaleTransform;
                if (scale != null)
                {
                    DoubleAnimation anim = new DoubleAnimation
                    {
                        To = 1.06,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            }
        }

        private void GameCard_MouseLeave(object sender, MouseEventArgs e)
        {
            var grid = sender as Grid;
            ImageBrush brush = GetCardImageBrush(grid);
            if (brush != null)
            {
                ScaleTransform scale = brush.RelativeTransform as ScaleTransform;
                if (scale != null)
                {
                    DoubleAnimation anim = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(220),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                }
            }
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

            TxtModalGameTitle.Text = "Chọn nguồn tải xuống";
            PbOverall.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressStatus.Text = "Khởi động tải...";
            BtnConfirmDownload.Content = "Tải về";
            BtnConfirmDownload.IsEnabled = true;
            BtnConfirmDownload.Opacity = 1.0;

            ProgressArea.Visibility = Visibility.Collapsed;
            ShowModalWithAnimation(AddProjectModalOverlay);
        }

        private void BtnDeleteAsset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string gameId = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            if (string.IsNullOrEmpty(gameId)) return;

            _deleteMode = "asset";
            _targetDeleteGameId = gameId;

            string fullGameName = "Zenless Zone Zero";
            if (gameId == "genshin") fullGameName = "Genshin Impact";
            else if (gameId == "hsr") fullGameName = "Honkai: Star Rail";

            TxtDeleteModalTitle.Text = "Xóa dữ liệu game?";
            TxtDeleteModalMessage.Text = "Bạn có chắc chắn muốn xóa toàn bộ CSDL " + fullGameName + " không?";
            ShowModalWithAnimation(DeleteModalOverlay);
        }

        // --- 3. RECENT FILE ROW ACTIONS ---
        private void RecentRow_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            var item = border.DataContext as RecentFileItem;
            if (item != null && !string.IsNullOrEmpty(item.Path))
            {
                OpenRecentFile(item.Path);
            }
        }

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

        private void BtnRenameRecent_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string filePath = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            _targetRenameFilePath = filePath;
            TxtRenameInput.Text = Path.GetFileName(filePath);
            ShowModalWithAnimation(RenameModalOverlay);
        }

        private void CloseRenameModal_Click(object sender, RoutedEventArgs e)
        {
            CloseModalWithAnimation(RenameModalOverlay);
        }

        private void ConfirmRenameModal_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_targetRenameFilePath) || !File.Exists(_targetRenameFilePath))
            {
                RenameModalOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            string newName = TxtRenameInput.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                ShowAlert("Thông báo", "Vui lòng nhập tên tệp hợp lệ!");
                return;
            }

            try
            {
                string dirPath = Path.GetDirectoryName(_targetRenameFilePath);
                string newPath = Path.Combine(dirPath, newName);

                if (!newPath.Equals(_targetRenameFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                {
                    ShowAlert("Cảnh báo", "Tệp trùng tên đã tồn tại trong thư mục!");
                    return;
                }

                File.Move(_targetRenameFilePath, newPath);
                _historyService.RemoveEntry(_targetRenameFilePath);
                _historyService.AddEntry("saves", newPath, newName, Path.GetExtension(newName).TrimStart('.').ToUpper());

                CloseModalWithAnimation(RenameModalOverlay);
                LoadRealRecentFiles();
            }
            catch (Exception ex)
            {
                ShowAlert("Lỗi", "Lỗi khi đổi tên tệp: " + ex.Message);
            }
        }

        private void BtnDeleteRecent_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string filePath = btn != null && btn.Tag != null ? btn.Tag.ToString() : "";
            if (string.IsNullOrEmpty(filePath)) return;

            _deleteMode = "recent";
            _targetDeleteFilePath = filePath;

            string fileName = Path.GetFileName(filePath);
            TxtDeleteModalTitle.Text = "Xóa tệp bản thảo?";
            TxtDeleteModalMessage.Text = "Bạn có chắc chắn muốn xóa tệp '" + fileName + "' không?";
            ShowModalWithAnimation(DeleteModalOverlay);
        }

        private void CloseDeleteModal_Click(object sender, RoutedEventArgs e)
        {
            CloseModalWithAnimation(DeleteModalOverlay);
        }

        private void ConfirmDeleteModal_Click(object sender, RoutedEventArgs e)
        {
            CloseModalWithAnimation(DeleteModalOverlay);

            if (_deleteMode == "recent")
            {
                try
                {
                    if (!string.IsNullOrEmpty(_targetDeleteFilePath) && File.Exists(_targetDeleteFilePath))
                    {
                        File.Delete(_targetDeleteFilePath);
                    }
                    _historyService.RemoveEntry(_targetDeleteFilePath);
                    LoadRealRecentFiles();
                    ShowAlert("Xóa thành công", "Đã xóa tệp bản thảo thành công!");
                }
                catch (Exception ex)
                {
                    ShowAlert("Lỗi", "Không thể xóa tệp: " + ex.Message);
                }
            }
            else if (_deleteMode == "asset")
            {
                try
                {
                    string fullGameName = "Zenless Zone Zero";
                    if (_targetDeleteGameId == "genshin") fullGameName = "Genshin Impact";
                    else if (_targetDeleteGameId == "hsr") fullGameName = "Honkai: Star Rail";

                    ShowAlert("Xóa thành công", "Đã xóa dữ liệu CSDL cho dự án " + fullGameName + " thành công!");
                }
                catch (Exception ex)
                {
                    ShowAlert("Lỗi", "Không thể xóa dữ liệu CSDL: " + ex.Message);
                }
            }
        }

        // --- 4. MODAL HANDLERS ---
        private void CloseAddProjectModal_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = false;
            CloseModalWithAnimation(AddProjectModalOverlay);
        }

        private async void StartDownloadFromModal_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) return;

            _isDownloading = true;
            BtnConfirmDownload.Content = "Đang tải...";
            BtnConfirmDownload.IsEnabled = false;
            BtnConfirmDownload.Opacity = 0.4;
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
            BtnConfirmDownload.Content = "Tải về";
            BtnConfirmDownload.IsEnabled = true;
            BtnConfirmDownload.Opacity = 1.0;
            CloseModalWithAnimation(AddProjectModalOverlay);

            if (success)
            {
                ShowAlert("Tải về hoàn tất", "Đã hoàn tất tải dữ liệu Đa Tệp (Multi-File) từ Git cho dự án " + gameIdToDownload.ToUpper() + " và nạp thành công vào CSDL SQLite!");
            }
            else
            {
                ShowAlert("Thất bại", "Có lỗi xảy ra trong quá trình tải dữ liệu cho dự án " + gameIdToDownload.ToUpper() + ". Vui lòng kiểm tra lại kết nối mạng!");
            }
        }

        private void ShowAlert(string title, string message)
        {
            TxtAlertModalTitle.Text = title;
            TxtAlertModalMessage.Text = message;
            ShowModalWithAnimation(AlertModalOverlay);
        }

        private void CloseAlertModal_Click(object sender, RoutedEventArgs e)
        {
            CloseModalWithAnimation(AlertModalOverlay);
        }

        private void ShowModalWithAnimation(Border modalOverlay)
        {
            if (modalOverlay == null) return;

            modalOverlay.Opacity = 0;
            modalOverlay.Visibility = Visibility.Visible;

            DoubleAnimation fadeAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            modalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

            Border innerCard = modalOverlay.Child as Border;
            if (innerCard != null)
            {
                ScaleTransform scale = innerCard.RenderTransform as ScaleTransform;
                if (scale == null || scale.IsFrozen)
                {
                    scale = new ScaleTransform(0.92, 0.92);
                    innerCard.RenderTransformOrigin = new Point(0.5, 0.5);
                    innerCard.RenderTransform = scale;
                }

                DoubleAnimation scaleAnim = new DoubleAnimation
                {
                    From = 0.92,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            }
        }

        private void CloseModalWithAnimation(Border modalOverlay)
        {
            if (modalOverlay == null || modalOverlay.Visibility != Visibility.Visible) return;

            DoubleAnimation fadeAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeAnim.Completed += (s, e) =>
            {
                modalOverlay.Visibility = Visibility.Collapsed;
            };
            modalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
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

        // --- 5. HIGH-PERFORMANCE REALTIME MOUSE-FOLLOWING PIE CHART TOOLTIP & FOCUS HANDLERS ---
        private void PieSlice_MouseEnter(object sender, MouseEventArgs e)
        {
            var hoveredPath = sender as System.Windows.Shapes.Path;
            if (hoveredPath != null)
            {
                // 1. Update ToolTip percentage
                if (hoveredPath.Tag != null)
                {
                    string[] parts = hoveredPath.Tag.ToString().Split('|');
                    if (parts.Length >= 2)
                    {
                        ToolTipPercent.Text = parts[1];
                        PieToolTipCard.Visibility = Visibility.Visible;
                    }
                }

                // 2. Animate Hovered Slice to 100% Opacity & 1.05 Scale
                AnimateSlice(hoveredPath, opacity: 1.0, scale: 1.05);

                // 3. Animate all Other Slices to 30% Dimmed Opacity & 1.0 Scale
                var allSlices = new[] { Slice1, Slice2, Slice3 };
                foreach (var slice in allSlices)
                {
                    if (slice != null && slice != hoveredPath)
                    {
                        AnimateSlice(slice, opacity: 0.30, scale: 1.0);
                    }
                }
            }
        }

        private void AnimateSlice(System.Windows.Shapes.Path slice, double opacity, double scale)
        {
            if (slice == null) return;
            try
            {
                // 1. Safely animate Opacity
                var animOpacity = new System.Windows.Media.Animation.DoubleAnimation(opacity, TimeSpan.FromSeconds(0.15));
                slice.BeginAnimation(UIElement.OpacityProperty, animOpacity);

                // 2. Ensure ScaleTransform is non-frozen and mutable
                ScaleTransform st = slice.RenderTransform as ScaleTransform;
                if (st == null || st.IsFrozen)
                {
                    st = new ScaleTransform(1.0, 1.0);
                    slice.RenderTransformOrigin = new Point(0.5, 0.5);
                    slice.RenderTransform = st;
                }

                var animScale = new System.Windows.Media.Animation.DoubleAnimation(scale, TimeSpan.FromSeconds(0.15));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);
            }
            catch { }
        }

        private void PieCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (PieToolTipCard.Visibility == Visibility.Visible)
            {
                Point pos = e.GetPosition(MainRootGrid);

                double cardWidth = PieToolTipCard.ActualWidth > 0 ? PieToolTipCard.ActualWidth : 52;
                double cardHeight = PieToolTipCard.ActualHeight > 0 ? PieToolTipCard.ActualHeight : 28;

                double targetX = pos.X + 12;
                double targetY = pos.Y + 12;

                // Smart flip left if tooltip overflows right boundary of window
                if (targetX + cardWidth > MainRootGrid.ActualWidth - 6)
                {
                    targetX = pos.X - cardWidth - 8;
                }

                // Smart flip up if tooltip overflows bottom boundary of window
                if (targetY + cardHeight > MainRootGrid.ActualHeight - 6)
                {
                    targetY = pos.Y - cardHeight - 8;
                }

                if (targetX < 0) targetX = 0;
                if (targetY < 0) targetY = 0;

                PieToolTipCard.Margin = new Thickness(targetX, targetY, 0, 0);
            }
        }

        private void PieCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            PieToolTipCard.Visibility = Visibility.Collapsed;

            // Restore all slices back to 100% Opacity & 1.0 Scale
            var allSlices = new[] { Slice1, Slice2, Slice3 };
            foreach (var slice in allSlices)
            {
                if (slice != null)
                {
                    AnimateSlice(slice, opacity: 1.0, scale: 1.0);
                }
            }
        }
    }
}
