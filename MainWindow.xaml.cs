using System;
using System.Windows;
using EtherEditorNative.Views;

namespace EtherEditorNative
{
    public partial class MainWindow : Window
    {
        private HomeView _homeView;
        private TranslateView _translateView;

        public MainWindow()
        {
            InitializeComponent();
            StateChanged += MainWindow_StateChanged;

            _homeView = new HomeView();
            _homeView.RequestNavigateTranslate += (s, e) => NavigateToTranslate();

            _translateView = new TranslateView();

            NavigateToHome();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MainGrid.Margin = new Thickness(6, 6, 6, 6);
                if (BtnMaximize != null) BtnMaximize.Content = "\uE923";
            }
            else
            {
                MainGrid.Margin = new Thickness(0);
                if (BtnMaximize != null) BtnMaximize.Content = "\uE922";
            }
        }

        private void NavigateToHome()
        {
            BtnTabHome.Tag = "Active";
            BtnTabTranslate.Tag = null;
            MainContentArea.Content = _homeView;
        }

        private void NavigateToTranslate()
        {
            BtnTabHome.Tag = null;
            BtnTabTranslate.Tag = "Active";
            MainContentArea.Content = _translateView;
        }

        private void BtnTabHome_Click(object sender, RoutedEventArgs e)
        {
            NavigateToHome();
        }

        private void BtnTabTranslate_Click(object sender, RoutedEventArgs e)
        {
            NavigateToTranslate();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
