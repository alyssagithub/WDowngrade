using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Principal;

namespace WDowngrade {
    public class Executor {
        public string title { get; set; }
        public string rbxversion { get; set; }
        public string platform { get; set; }
        public string extype { get; set; }
    }

    public class Program {
        private const string ScriptVersion = "v1.0.3";
        private static string robloxLocalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
        private static readonly HttpClient client = new HttpClient();
        
        static Program() {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public static void Main(string[] args) {
            Console.Title = "WDowngrade";
            
            // Enable all common TLS versions and bypass certificate validation for maximum compatibility
            try {
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | (SecurityProtocolType)192;
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; } catch { }
            } catch { }

            try {
                RunAsync().GetAwaiter().GetResult();
            } catch (Exception ex) {
                Console.ForegroundColor = ConsoleColor.Red;
                Exception realEx = ex;
                while (realEx.InnerException != null) realEx = realEx.InnerException;
                
                Console.WriteLine("\nerror: " + realEx.Message);
                if (ex != realEx) Console.WriteLine("details: " + ex.Message);
                
                HandleSslFallback().GetAwaiter().GetResult();
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        private static async Task RunAsync() {
            string currentVersion = ScriptVersion;
            
            // 1. Initial Prompt & Update Check
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("WDowngrade " + currentVersion);
            Console.ResetColor();

            try {
                var requestVer = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/alyssagithub/WDowngrade/releases/latest");
                requestVer.Headers.UserAgent.ParseAdd("WDowngrade-Updater");
                var responseVer = await client.SendAsync(requestVer);
                if (responseVer.IsSuccessStatusCode) {
                    var jsonVer = await responseVer.Content.ReadAsStringAsync();
                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, object>>(jsonVer);
                    if (dict.ContainsKey("name") && dict.ContainsKey("html_url") && dict["name"].ToString() != currentVersion) {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\na new update for WDowngrade is available! (" + dict["name"] + ")");
                        Console.WriteLine("you are currently using " + currentVersion);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write("would you like to download the latest release? (y/n): ");
                        Console.ResetColor();
                        string choice = Console.ReadLine();
                        if (choice != null && choice.Trim().ToLower().StartsWith("y")) {
                            Process.Start(dict["html_url"].ToString());
                            Environment.Exit(0);
                        }
                    }
                }
            } catch { }

            // 2. Fetch & Select Executor
            List<Executor> executors = null;

            bool syncFailed = false;
            for (int i = 0; i < 3; i++) {
                bool success = false;
                try {
                    var requestSync = new HttpRequestMessage(HttpMethod.Get, "https://whatexpsare.online/api/status/exploits");
                    requestSync.Headers.UserAgent.Clear();
                    requestSync.Headers.UserAgent.ParseAdd("WEAO-3PService");
                    
                    var responseSync = await client.SendAsync(requestSync);
                    responseSync.EnsureSuccessStatusCode();
                    var jsonSync = await responseSync.Content.ReadAsStringAsync();
                    var serializerSync = new JavaScriptSerializer();
                    var all = serializerSync.Deserialize<List<Executor>>(jsonSync);
                    executors = all.Where(e => e.platform == "Windows" && e.rbxversion != null && e.rbxversion.StartsWith("version-") && e.extype == "wexecutor").ToList();
                    success = true;
                } catch (Exception) {
                    if (i == 2) syncFailed = true;
                }
                
                if (success) break;
                
                if (!syncFailed) {
                    Console.WriteLine("syncing failed, retrying (" + (i + 1) + "/3)...");
                    await Task.Delay(2000);
                }
            }

            if (syncFailed) {
                throw new Exception("failed to connect to the url (you might need to use a vpn like https://1.1.1.1)");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\navailable executors:");
            Console.ResetColor();
            foreach (var ex in executors) Console.WriteLine(ex.title.Trim());

            Executor selected = null;
            while (selected == null) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\nenter an executor name (can be shortened): ");
                Console.ResetColor();
                string line = Console.ReadLine();
                string choice = (line != null) ? line.Trim() : "";
                if (string.IsNullOrEmpty(choice)) continue;

                selected = executors.FirstOrDefault(ex => ex.title.IndexOf(choice, StringComparison.OrdinalIgnoreCase) >= 0);
                if (selected == null) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("couldn't get the version of the executor, did you type it right?");
                    Console.ResetColor();
                }
            }

            // 3. Deployment
            await DowngradeRoblox(selected.rbxversion);

            // 4. Final Status & Verification
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\ndone, open roblox to verify it worked");
            Console.ResetColor();
            
            await StartVerifying(selected.rbxversion);
        }

        private static async Task DowngradeRoblox(string version) {
            string targetDir = Path.Combine(robloxLocalPath, version);
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta")) { try { p.Kill(); } catch { } }

            var components = new Dictionary<string, string> {
                { "RobloxApp.zip", "" }, { "redist.zip", "" }, { "shaders.zip", "shaders" }, { "ssl.zip", "ssl" }, 
                { "WebView2.zip", "" }, { "WebView2RuntimeInstaller.zip", "WebView2RuntimeInstaller" },
                { "content-avatar.zip", "content/avatar" }, { "content-configs.zip", "content/configs" },
                { "content-fonts.zip", "content/fonts" }, { "content-sky.zip", "content/sky" },
                { "content-sounds.zip", "content/sounds" }, { "content-textures2.zip", "content/textures" },
                { "content-models.zip", "content/models" }, { "content-platform-fonts.zip", "PlatformContent/pc/fonts" },
                { "content-platform-dictionaries.zip", "PlatformContent/pc/shared_compression_dictionaries" },
                { "content-terrain.zip", "PlatformContent/pc/terrain" }, { "content-textures3.zip", "PlatformContent/pc/textures" },
                { "extracontent-luapackages.zip", "ExtraContent/LuaPackages" }, { "extracontent-translations.zip", "ExtraContent/translations" },
                { "extracontent-models.zip", "ExtraContent/models" }, { "extracontent-textures.zip", "ExtraContent/textures" },
                { "extracontent-places.zip", "ExtraContent/places" }
            };

            string manifest = await client.GetStringAsync("https://setup-aws.rbxcdn.com/" + version + "-rbxPkgManifest.txt");
            var availableFiles = manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0]).ToList();
            var active = components.Where(c => availableFiles.Contains(c.Key, StringComparer.OrdinalIgnoreCase)).ToList();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\ndownloading, please wait...");
            Console.ResetColor();
            
            var semaphore = new SemaphoreSlim(8);
            var tasks = active.Select(async kvp => {
                await semaphore.WaitAsync();
                try {
                    string compName = kvp.Key; string subDir = kvp.Value;
                    string tempFile = Path.Combine(Path.GetTempPath(), compName);
                    await DownloadFile("https://setup-aws.rbxcdn.com/" + version + "-" + compName, tempFile);
                    
                    string extractTo = string.IsNullOrEmpty(subDir) ? targetDir : Path.Combine(targetDir, subDir);
                    if (!Directory.Exists(extractTo)) Directory.CreateDirectory(extractTo);
                    
                    await Task.Run(() => {
                        using (ZipArchive archive = ZipFile.OpenRead(tempFile)) {
                            foreach (ZipArchiveEntry entry in archive.Entries) {
                                string fullPath = Path.Combine(extractTo, entry.FullName);
                                string directory = Path.GetDirectoryName(fullPath);
                                if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                                if (!string.IsNullOrEmpty(entry.Name)) entry.ExtractToFile(fullPath, true);
                            }
                        }
                        File.Delete(tempFile);
                    });
                } finally {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("download complete, extracting...");
            Console.ResetColor();

            File.WriteAllText(Path.Combine(targetDir, "AppSettings.xml"), "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n    <ContentFolder>content</ContentFolder>\r\n    <BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>");

            UpdateRegistry(version, Path.Combine(targetDir, "RobloxPlayerBeta.exe"));
            CreateShortcut(Path.Combine(targetDir, "RobloxPlayerBeta.exe"));
            CleanupOldVersions(version);
        }

        private static async Task DownloadFile(string url, string path) {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception("CDN Failed (" + response.StatusCode + "): " + url);
            using (var fs = new FileStream(path, FileMode.Create)) await response.Content.CopyToAsync(fs);
        }

        private static void UpdateRegistry(string version, string exePath) {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\roblox-player\shell\open\command")) {
                    key.SetValue("", string.Format("\"{0}\" %1", exePath));
                    key.SetValue("version", version);
                }
            } catch { }
        }

        private static void CreateShortcut(string exePath) {
            try {
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Roblox");
                if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                string ps = string.Format("$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{0}\\Roblox Player.lnk');$s.TargetPath='{1}';$s.Save()", startMenu, exePath);
                Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command \"" + ps + "\"") { CreateNoWindow = true, UseShellExecute = false });
            } catch { }
        }

        private static async Task StartVerifying(string version) {
            bool autoUpdated = false;
            while (true) {
                await Task.Delay(1000);
                if (Process.GetProcessesByName("RobloxPlayerInstaller").Length > 0 || Process.GetProcessesByName("RobloxPlayerLauncher").Length > 0) autoUpdated = true;

                var roblox = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();
                if (roblox != null) {
                    try {
                        if (roblox.MainModule.FileName.IndexOf(version, StringComparison.OrdinalIgnoreCase) >= 0) {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("worked, you can close this window now");
                            Console.ResetColor();
                        } else if (autoUpdated) {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("roblox automatically updated itself back to a newer version, you'll have to install fishstrap at https://fishstrap.app, and use that to downgrade roblox to " + version);
                            Console.ResetColor();
                        } else {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("something went wrong, the version is incorrect, contact the developer of WDowngrade");
                            Console.ResetColor();
                        }
                        return;
                    } catch { }
                }
            }
        }

        private static void CleanupOldVersions(string current) {
            try {
                foreach (var dir in Directory.GetDirectories(robloxLocalPath)) {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith("version-") && name != current && !File.Exists(Path.Combine(dir, "RobloxStudioBeta.exe"))) {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            } catch { }
        }

        private static bool IsAdmin() {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static async Task HandleSslFallback() {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("\nif you'd like to manually downgrade, then enter the full roblox version-xxxx.. hash you want to downgrade to: ");
            string versionInput = Console.ReadLine();
            string version = (versionInput != null) ? versionInput.Trim() : "";
            if (string.IsNullOrEmpty(version) || !version.StartsWith("version-")) {
                Console.WriteLine("the version hash should start with 'version-'");
                return;
            }

            string url = string.Format("https://rdd.whatexpsare.online/?channel=LIVE&binaryType=WindowsPlayer&version={0}&parallelDownloads=true&exploit=Velocity", version);
            Console.WriteLine("\nan rdd tab will be opened in your browser, do not close it until it's done");
            try {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            } catch {
                Console.WriteLine("\ncouldn't open browser automatically, please open the URL below in your browser");
                Console.WriteLine(url);
            }

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            DateTime startTime = DateTime.Now.AddSeconds(-10); // allow some buffer
            string downloadedFile = null;

            while (downloadedFile == null) {
                if (Directory.Exists(downloadsPath)) {
                    var files = Directory.GetFiles(downloadsPath, "*.zip");
                    foreach (var file in files) {
                        try {
                            var info = new FileInfo(file);
                            // RDD zip usually contains "Roblox" and the version string
                            if (info.CreationTime >= startTime && (info.Name.Contains(version) || (info.Name.Contains("Roblox") && info.Length > 50 * 1024 * 1024))) {
                                // Try to open it to see if it's finished (browser releases lock when done)
                                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) {
                                    downloadedFile = file;
                                    break;
                                }
                            }
                        } catch {
                            // File is likely still being written by the browser or locked
                        }
                    }
                }
                await Task.Delay(2000);
            }
            
            await Task.Delay(1000); // Give browser a moment to finish its clean-up

            string targetDir = Path.Combine(Path.GetDirectoryName(downloadedFile), Path.GetFileName(downloadedFile).Replace(".zip", ""));
            if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

            try {
                // Ensure extraction starts fresh
                if (Directory.Exists(targetDir)) {
                    foreach (var file in Directory.GetFiles(targetDir)) File.Delete(file);
                    foreach (var subDir in Directory.GetDirectories(targetDir)) Directory.Delete(subDir, true);
                }

                ZipFile.ExtractToDirectory(downloadedFile, targetDir);
                File.Delete(downloadedFile);
                Process.Start("explorer.exe", targetDir);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\ndone, read the steps below to run the downgraded roblox");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("1. go to the folder: " + targetDir);
                Console.WriteLine("2. run the 'RobloxPlayerBeta.exe' inside of it");
                Console.WriteLine("do this every time you want to use this downgraded version");
                Console.ResetColor();
            } catch (Exception ex) {
                Console.WriteLine("\nfailed to extract: " + ex.Message);
            }
        }
    }
}
