# 🚀 Ether Editor Native (Pure C# Desktop Application)

Dự án **Ether Editor Native** là phiên bản **100% C# Native Desktop Application (WPF)** được chuyển đổi và tối ưu hoàn toàn từ phiên bản Python/Webview cũ, loại bỏ 100% phụ thuộc vào Python runtime, Webview hoặc trình duyệt bên ngoài.

---

## 📁 Cấu Trúc Dự Án (Project Architecture)

```
experimental_csharp/
├── EtherEditorNative.sln        # Tệp Solution Visual Studio (Mở bằng VS 2019/2022/Rider/VS Code)
├── EtherEditorNative.csproj     # Tệp C# Project Manifest (.NET Framework 4.8 / C# 7.3+)
├── build_experimental.bat       # Kịch bản tự động dò tìm MSBuild & Biên dịch 1-click
├── .gitignore                   # Loại bỏ thư mục build tạm (bin/, obj/, .vs/)
│
├── Backend/                     # 100% Dịch vụ backend viết bằng C# Pure Native
│   ├── DatabaseService.cs       # Quản lý SQLite CSDL bot_data.db (16 hàm core)
│   ├── LogicService.cs          # Logic chuẩn hóa XIG HTML, bóc tách tag (23 hàm core)
│   ├── GlossaryService.cs       # Quản lý Thuật ngữ & Từ điển Replace (4 hàm core)
│   ├── FandomConverterService.cs# Chuyển đổi Biệt danh Fandom & Merge Text (3 hàm core)
│   ├── ProjectService.cs        # Quản lý Workspace & Đọc tệp tin Dự án (5 hàm core)
│   ├── HistoryService.cs        # Quản lý Lịch sử tệp mở gần đây (6 hàm core)
│   └── HttpServerService.cs     # C# HttpListener tích hợp API ngầm cho Extension
│
├── Views/                       # Giao diện WPF XAML Thuần
│   ├── HomeView.xaml            # Giao diện Trang chủ 55%/45%, Card Poster 3D & Modals
│   ├── HomeView.xaml.cs         # Event Handlers & Async Download Pipeline
│   ├── TranslateView.xaml       # Giao diện Dịch thuật 3 cột (Bảng danh sách, khung gốc/dịch)
│   └── TranslateView.xaml.cs    # Logic tìm kiếm, lọc chưa dịch, cập nhật SQLite realtime
│
├── App.xaml / App.xaml.cs       # Điểm khởi chạy ứng dụng WPF
└── README.md                    # Tài liệu hướng dẫn phát triển
```

---

## 🛠️ Hướng Dẫn Biên Dịch & Khởi Chạy (Building & Running)

### 1. Phương pháp 1-Click (Dành cho mọi Dev):
Nhấp đúp chuột vào tệp `build_experimental.bat`. Kịch bản sẽ tự động:
- Dò tìm Visual Studio 2022/2019/2017 hoặc .NET MSBuild trên máy.
- Tự động dừng tiến trình cũ nếu đang chạy.
- Biên dịch ứng dụng ở chế độ `Release`.
- Khởi chạy ngay ứng dụng `EtherEditorNative.exe`.

### 2. Mở bằng Visual Studio (Visual Studio 2019 / 2022 / JetBrains Rider):
1. Mở tệp **`EtherEditorNative.sln`**.
2. Nhấn **F5** để khởi chạy Debug hoặc **Ctrl + F5** để chạy không Debug.

---

## 🤝 Quy Chuẩn Đóng Góp (Contributing Guidelines)

1. Khi thêm dịch vụ C# backend mới, tạo tệp trong thư mục `Backend/` và nhớ đăng ký tệp vào `<Compile Include="Backend\FileMoi.cs" />` trong `EtherEditorNative.csproj`.
2. Giữ nguyên hiệu ứng Hover Animation và màu chuẩn tối `#111418` trong XAML để đảm bảo giao diện thống nhất.
