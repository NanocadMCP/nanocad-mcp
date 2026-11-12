using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("nanoCAD MCP — Setup")]
[assembly: AssemblyProduct("nanoCAD MCP")]
[assembly: AssemblyVersion("0.453.0.0")]
[assembly: AssemblyFileVersion("0.453.0.0")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool verify = args.Length == 2 && args[0] == "--verify";
        Form window = null;
        try
        {
            if (args.Length != 0 && !verify) throw new ArgumentException("Unknown installer arguments.");
            if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Windows x64 is required.");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool cancelled = false;
            ProgressBar progress = null;
            if (!verify)
            {
                window = new Form { Text = "nanoCAD MCP Setup", ClientSize = new Size(470, 120),
                    StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false, MinimizeBox = false };
                window.Controls.Add(new Label { Text = "Preparing local installation...", Left = 22, Top = 22, Width = 420, Height = 24 });
                progress = new ProgressBar { Left = 22, Top = 62, Width = 420, Height = 22 };
                window.Controls.Add(progress);
                window.FormClosing += delegate { cancelled = true; };
                window.Show(); Application.DoEvents();
            }
            string directory = verify ? Path.GetFullPath(args[1]) : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NanoCADMcp", "SetupCache", "447-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("The preparation folder already exists; its contents will not be changed.");
            Directory.CreateDirectory(directory);
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (payload == null) throw new InvalidDataException("The embedded package is missing.");
                if (!String.Equals(Hash(payload), BuildInfo.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The embedded package is damaged. Obtain a fresh copy of the installer.");
                payload.Position = 0;
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count > 4096) throw new InvalidDataException("The package contains too many files.");
                    long total = 0; int index = 0;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in archive.Entries)
                    {
                        Application.DoEvents();
                        if (cancelled) throw new OperationCanceledException();
                        total += entry.Length;
                        if (total > 512L * 1024 * 1024 || entry.FullName.Contains(":")) throw new InvalidDataException("Invalid file size or path.");
                        string path = Confined(directory, entry.FullName);
                        if (!seen.Add(path)) throw new InvalidDataException("Duplicate file path in the package.");
                        if (String.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(path);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            using (var source = entry.Open())
                            using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) source.CopyTo(destination);
                        }
                        if (progress != null) progress.Value = (++index * 100) / archive.Entries.Count;
                    }
                }
            }
            int checkedFiles = VerifyFiles(directory);
            if (cancelled) throw new OperationCanceledException();
            // The same tested installer is embedded. No console or script handling is exposed to the engineer.
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(directory, "Setup.ps1") + "\" -Mode " + (verify ? "Plan" : "Gui") };
            // Let Windows PowerShell rebuild its module paths instead of inheriting
            // an incompatible PowerShell 7 module environment from the launcher.
            Environment.SetEnvironmentVariable("PSModulePath", null, EnvironmentVariableTarget.Process);
            if (window != null) window.Hide();
            using (var child = Process.Start(start))
            {
                var stdout = child.StandardOutput.ReadToEndAsync();
                var stderr = child.StandardError.ReadToEndAsync();
                child.WaitForExit();
                File.WriteAllText(Path.Combine(directory, "setup.log"), stdout.Result + Environment.NewLine + stderr.Result, Encoding.UTF8);
                if (child.ExitCode != 0) throw new InvalidOperationException("Setup did not complete. Log: " + Path.Combine(directory, "setup.log"));
            }
            if (verify)
                File.WriteAllText(Path.Combine(directory, "bootstrapper-verification.json"),
                    "{\"verified\":true,\"files\":" + checkedFiles + ",\"mode\":\"extract_hash_and_plan_only\",\"installed\":false}", Encoding.UTF8);
            return 0;
        }
        catch (OperationCanceledException) { return 2; }
        catch (Exception error)
        {
            if (verify) { try { File.WriteAllText(args[1] + ".error.txt", error.ToString(), Encoding.UTF8); } catch { } }
            else
            {
                string message = "Setup could not start. Please try again. If the problem continues, obtain a fresh copy of the installer or contact support.";
                if (!Environment.Is64BitOperatingSystem) message = "This installer requires 64-bit Windows.";
                try
                {
                    string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NanoCADMCP", "Logs");
                    Directory.CreateDirectory(logs);
                    string log = Path.Combine(logs, "setup-" + Guid.NewGuid().ToString("N") + ".txt");
                    File.WriteAllText(log, error.ToString(), Encoding.UTF8);
                    message += "\r\n\r\nDetails for support: " + log;
                }
                catch { }
                MessageBox.Show(message, "nanoCAD MCP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return 1;
        }
        finally { if (window != null) window.Dispose(); }
    }

    private static string Confined(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The file path is outside the package folder.");
        return path;
    }
    private static string Hash(Stream stream)
    {
        using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }
    private static int VerifyFiles(string directory)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        var manifest = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        if ((string)manifest["platform"] != "win-x64") throw new InvalidDataException("Unsupported package platform.");
        int count = 0;
        foreach (Dictionary<string, object> item in (IEnumerable)manifest["files"])
        {
            using (var stream = File.OpenRead(Confined(directory, (string)item["path"])))
                if (!String.Equals(Hash(stream), (string)item["sha256"], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Damaged file: " + item["path"]);
            count++;
        }
        return count;
    }
}
