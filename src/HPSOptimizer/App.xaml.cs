using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using HPSOptimizer.Core;

namespace HPSOptimizer;

public partial class App : Application
{
    public static bool IsAdmin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error($"Lỗi không bắt được: {args.ExceptionObject}");

        using var identity = WindowsIdentity.GetCurrent();
        IsAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        Logger.Info($"HPS Optimizer khởi động. Quyền admin: {(IsAdmin ? "có" : "KHÔNG")}. " +
                    $"OS: {Environment.OSVersion.VersionString}");

        if (!IsAdmin)
        {
            MessageBox.Show(
                "App đang chạy không có quyền Administrator.\n\n" +
                "Các chức năng sửa registry HKLM, dịch vụ, phân vùng và tạo điểm khôi phục sẽ thất bại.\n" +
                "Hãy đóng app và mở lại bằng \"Run as administrator\".",
                "Thiếu quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Lỗi giao diện", e.Exception);
        MessageBox.Show($"Đã xảy ra lỗi:\n\n{e.Exception.Message}\n\nChi tiết đã ghi vào {Paths.LogFile}",
            "HPS Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
