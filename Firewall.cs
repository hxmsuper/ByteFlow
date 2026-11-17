using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace ByteFlow;
public sealed class Firewall
{
    private const string Group = "ByteFlow.Managed.v1";
    private dynamic Policy => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
    public HashSet<string> Blocked()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic rule in Policy.Rules)
            if ((string?)rule.Grouping == Group && rule.Enabled && rule.Action == 0 && !string.IsNullOrEmpty((string?)rule.ApplicationName)) paths.Add((string)rule.ApplicationName);
        return paths;
    }
    public void Block(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException(Localization.T("程序文件已不存在，无法创建规则。", "Executable no longer exists. Cannot create rule."));
        dynamic policy = Policy;
        for (int profile = 1; profile <= 4; profile *= 2)
            if (!policy.FirewallEnabled[profile]) throw new InvalidOperationException(Localization.T("请先开启 Windows 防火墙的所有网络配置文件，再使用断网功能。", "Enable Windows Firewall for all network profiles before blocking an app."));
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        try
        {
            foreach (int direction in new[] { 1, 2 })
            {
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
                rule.Name = $"ByteFlow-{id}-{direction}";
                rule.Description = "由 ByteFlow 创建的应用断网规则，可通过 ByteFlow 恢复。";
                rule.Grouping = Group; rule.ApplicationName = path; rule.Protocol = 256;
                rule.Direction = direction; rule.Action = 0; rule.Profiles = 7; rule.Enabled = true;
                policy.Rules.Add(rule);
            }
        }
        catch { Restore(path); throw; }
    }
    public void Restore(string? path = null)
    {
        dynamic policy = Policy;
        var names = new List<string>();
        foreach (dynamic rule in policy.Rules)
            if ((string?)rule.Grouping == Group && (path == null || string.Equals((string?)rule.ApplicationName, path, StringComparison.OrdinalIgnoreCase))) names.Add((string)rule.Name);
        foreach (var name in names) policy.Rules.Remove(name);
    }
}
