using System.IO;
using System.Windows;
namespace ByteFlow;
public static class Localization
{
    public static string Language { get; private set; } = "zh";
    public static string T(string zh, string en) => Language == "en" ? en : zh;
    public static void Load()
    {
        try { Language = File.ReadAllText(Path.Combine(UsageStore.DataDirectory, "language.txt")).Trim() == "en" ? "en" : "zh"; } catch { }
    }
    public static void Set(string language)
    {
        Directory.CreateDirectory(UsageStore.DataDirectory);
        File.WriteAllText(Path.Combine(UsageStore.DataDirectory, "language.txt"), language);
        Language = language;
        Apply();
    }
    public static readonly Dictionary<string, string> Text = new()
    {
        ["应用"]="Application", ["进程数"]="Processes", ["↓ 下载速率"]="↓ Rate", ["↑ 上传速率"]="↑ Rate", ["累计流量"]="Session usage", ["联网开关"]="Internet",
        ["已禁止"]="Blocked", ["允许联网"]="Allowed", ["显示系统进程"]="System processes", ["恢复全部联网"]="Restore all", ["搜索应用、PID 或路径"]="Search apps, PID or path",
        ["应用详情"]="Application details", ["选择一个应用"]="Select an application", ["实时速度"]="Live rates", ["本次监控累计"]="Session usage", ["控制范围"]="Network control",
        ["开关按程序路径生效，同一应用的所有进程统一控制。关闭开关后，Windows 防火墙将阻止该程序收发网络数据。"]="Controls all processes using this executable. Turning the switch off blocks its network traffic through Windows Firewall.",
        ["退出 ByteFlow 后，断网规则仍保留。重新打开即可恢复联网。"]="Blocks remain after closing ByteFlow. Reopen the app to restore access.",
        ["开：允许联网；关：阻止该程序联网"]="On: allow internet; off: block internet", ["统计数据"]="Usage", ["设置"]="Settings",
        ["正在启动实时速率采集…"]="Starting live rate capture…", ["正在启动网络监控…"]="Starting network monitor…"
    };
    public static void Apply()
    {
        foreach (var pair in Text) Application.Current.Resources[pair.Key] = T(pair.Key, pair.Value);
    }
}
