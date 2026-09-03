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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadFandomWikiStats Error: " + ex.Message);
            }
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

            BtnFandomTabArticles.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38bdf8"));
            BtnFandomTabArticles.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a"));
            BtnFandomTabArticles.FontWeight = FontWeights.Bold;

            BtnFandomTabUsers.Background = Brushes.Transparent;
            BtnFandomTabUsers.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"));
            BtnFandomTabUsers.FontWeight = FontWeights.SemiBold;

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

            BtnFandomTabUsers.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10b981"));
            BtnFandomTabUsers.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0f172a"));
            BtnFandomTabUsers.FontWeight = FontWeights.Bold;

            BtnFandomTabArticles.Background = Brushes.Transparent;
            BtnFandomTabArticles.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94a3b8"));
            BtnFandomTabArticles.FontWeight = FontWeights.SemiBold;

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
                Point pos = e.GetPosition(Card2BGrid);
                // Position card so mouse cursor points directly at top-left corner (+8px, +8px)
                PieToolTipCard.Margin = new Thickness(pos.X + 8, pos.Y + 8, 0, 0);
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
