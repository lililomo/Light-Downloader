using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

const string AppName = "Light Downloader";
const string Version = "1.1.0";
const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LightDownloader";

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
bool uninstall = argv.Contains("--uninstall");
bool noShortcuts = argv.Contains("--no-shortcuts");
bool noRegistry = argv.Contains("--no-registry");
bool noLaunch = argv.Contains("--no-launch");
bool silent = argv.Contains("--silent");
string? dirOverride = GetArg(argv, "--dir");

string installDir = dirOverride ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
string exePath = Path.Combine(installDir, "LightDownloader.exe");

try
{
    if (uninstall) Uninstall();
    else Install();
}
catch (Exception ex)
{
    Console.WriteLine("ERROR: " + ex.Message);
    if (!silent) Pause();
    Environment.Exit(1);
}

void Install()
{
    Console.WriteLine($"Installing {AppName} {Version}");
    Console.WriteLine("Target folder: " + installDir);
    Directory.CreateDirectory(installDir);

    var asm = Assembly.GetExecutingAssembly();
    using (var zs = asm.GetManifestResourceStream("payload.zip"))
    {
        if (zs == null) throw new Exception("Installer payload is missing (payload.zip not embedded).");
        using var archive = new ZipArchive(zs, ZipArchiveMode.Read);
        archive.ExtractToDirectory(installDir, overwriteFiles: true);
    }
    Console.WriteLine("Application files installed.");

    // Keep a copy of the installer inside the install folder so it can uninstall later.
    string setupCopy = Path.Combine(installDir, "LightDownloaderSetup.exe");
    try
    {
        var self = Environment.ProcessPath;
        if (self != null && !string.Equals(self, setupCopy, StringComparison.OrdinalIgnoreCase))
            File.Copy(self, setupCopy, true);
    }
    catch { /* non-fatal */ }

    if (!noShortcuts)
    {
        TryShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"), exePath, installDir);
        TryShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exePath, installDir);
        Console.WriteLine("Start Menu and Desktop shortcuts created.");
    }

    if (!noRegistry)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{setupCopy}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        Console.WriteLine("Registered in Add/Remove Programs.");
    }

    CheckRuntime();

    if (!noLaunch)
    {
        try { Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = installDir }); }
        catch (Exception ex) { Console.WriteLine("Could not launch the app: " + ex.Message); }
    }

    Console.WriteLine();
    Console.WriteLine($"Done. {AppName} is installed.");
    Console.WriteLine("Browser extensions to load are in: " + Path.Combine(installDir, "extension"));
    if (!silent) Pause();
}

void Uninstall()
{
    Console.WriteLine($"Uninstalling {AppName}...");
    foreach (var p in Process.GetProcessesByName("LightDownloader"))
        try { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } catch { }

    foreach (var lnk in new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk")
    })
        try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }

    try { Registry.CurrentUser.DeleteSubKeyTree(RegKey, throwOnMissingSubKey: false); } catch { }

    var self = Environment.ProcessPath;
    try
    {
        foreach (var f in Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(f); } catch { }
        }
    }
    catch { }

    Console.WriteLine("Uninstalled. (App data in %AppData%\\LightDownloader is kept; delete it manually if you want.)");
    if (!silent) Pause();
}

void CheckRuntime()
{
    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var dir = Path.Combine(pf, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
    bool ok = Directory.Exists(dir) && Directory.GetDirectories(dir, "8.*").Length > 0;
    if (!ok)
    {
        Console.WriteLine();
        Console.WriteLine("NOTE: the .NET 8 Desktop Runtime was not detected.");
        Console.WriteLine("Install it (Desktop Runtime, x64) from:");
        Console.WriteLine("  https://dotnet.microsoft.com/download/dotnet/8.0/runtime");
    }
}

void TryShortcut(string lnkPath, string target, string workingDir)
{
    try
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return;
        dynamic shell = Activator.CreateInstance(t)!;
        var sc = shell.CreateShortcut(lnkPath);
        sc.TargetPath = target;
        sc.WorkingDirectory = workingDir;
        sc.IconLocation = target + ",0";
        sc.Save();
    }
    catch { /* shortcut creation is best-effort */ }
}

void Pause()
{
    Console.WriteLine();
    Console.WriteLine("Press any key to close...");
    try { Console.ReadKey(true); } catch { }
}

static string? GetArg(string[] a, string name)
{
    int i = Array.IndexOf(a, name);
    return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
}
