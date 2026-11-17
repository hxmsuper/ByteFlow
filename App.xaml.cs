using System.Windows;
namespace ByteFlow;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new Mutex(true, "Local\\ByteFlow.Desktop.Instance", out bool created);
        if (!created)
        {
            Localization.Load();
            MessageBox.Show(Localization.T("ByteFlow 已在运行，请先退出旧版本再打开。", "ByteFlow is already running. Close the existing instance first."), "ByteFlow");
            Shutdown(); return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
