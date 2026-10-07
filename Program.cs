using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FindSteamPath
{
    class Program
    {
        #region 数据模型

        class GameInfo
        {
            public string Name { get; set; }
            public string LocalSaveFolder { get; set; }
        }

        class AccountInfo
        {
            public string SteamId3;
            public string SteamId64;
            public string DisplayName;
            public string FolderPath;
        }

        static Dictionary<string, GameInfo> _gameInfoMap = new Dictionary<string, GameInfo>();

        static readonly Dictionary<string, GameInfo> DefaultGameMap = new Dictionary<string, GameInfo>
        {
            { "1144400", new GameInfo { Name = "千恋万花",     LocalSaveFolder = "SenrenBanka" } },
            { "1277930", new GameInfo { Name = "Riddle joker", LocalSaveFolder = "RiddleJoker" } }
        };

        static readonly JsonSerializerOptions JsonWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static readonly JsonSerializerOptions JsonReadOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };

        #endregion

        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("注意：本作品用于备份关闭steam云同步后的游戏存档\n");
            Console.WriteLine("如果要使用直接备份至本地存档，那么请确保关闭云同步后运行一次游戏并创建任意存档点\n");
            Console.WriteLine("如果使用普通方式备份，则可以无视以上事项\n");
            Console.WriteLine("本程序全部在本地运行，并严禁以任何形式盈利，若同意，请输入 Y 来使用\n");
            Console.Write("请输入: ");
            string NEXT = Console.ReadLine()?.Trim() ?? "";

            if (NEXT.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                
                
            }
            else
            {
               
                return;
            }
            Console.WriteLine("正在查找 Steam 安装路径...\n");

            LoadOrCreateGameMap();
            Console.WriteLine();

            string steamPath = FindSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                PauseAndExit("✗ 未找到 Steam 安装路径");
                return;
            }

            WriteOk("✓ 找到 Steam:");
            Console.WriteLine("  路径: " + steamPath);
            Console.WriteLine("  是否存在: " + (File.Exists(Path.Combine(steamPath, "steam.exe")) ? "是" : "否"));

            var accountNames = LoadAccountNames(steamPath);

            Console.WriteLine();
            var accounts = PrintUserDataFolders(steamPath, accountNames);
            if (accounts.Count == 0)
            {
                PauseAndExit("");
                return;
            }

            InteractiveSelect(accounts);
        }

        #region 彩色输出辅助

        static void WriteOk(string msg) => WriteColored(msg, ConsoleColor.Green);
        static void WriteErr(string msg) => WriteColored(msg, ConsoleColor.Red);
        static void WriteWarn(string msg) => WriteColored(msg, ConsoleColor.Yellow);

        static void WriteColored(string text, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
        }

        static void WriteColoredInline(string text, ConsoleColor color)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = old;
        }

        #endregion

        static void PauseAndExit(string message)
        {
            if (!string.IsNullOrEmpty(message))
                WriteErr(message);

            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n按任意键退出...");
            Console.ForegroundColor = old;

            Console.ReadKey();
        }

        #region 游戏映射 (game_map.json)

        static string GetGameMapPath()
        {
            string exeDir = AppContext.BaseDirectory;
            if (CanWrite(exeDir))
                return Path.Combine(exeDir, "game_map.json");

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FindSteamPath");
            Directory.CreateDirectory(appDataDir);
            return Path.Combine(appDataDir, "game_map.json");
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string test = Path.Combine(dir, ".write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(test, "");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        static void LoadOrCreateGameMap()
        {
            string path = GetGameMapPath();

            try
            {
                if (!File.Exists(path))
                {
                    WriteWarn($"未找到 {path}，正在创建默认配置...");
                    SaveGameMap(path, DefaultGameMap);
                    _gameInfoMap = new Dictionary<string, GameInfo>(DefaultGameMap);
                    WriteOk($"✓ 已创建 game_map.json（{_gameInfoMap.Count} 条记录）");
                    return;
                }

                string json = File.ReadAllText(path);
                var map = JsonSerializer.Deserialize<Dictionary<string, GameInfo>>(json, JsonReadOptions);

                if (map == null || map.Count == 0)
                {
                    WriteWarn("game_map.json 内容为空，重新写入默认数据...");
                    SaveGameMap(path, DefaultGameMap);
                    _gameInfoMap = new Dictionary<string, GameInfo>(DefaultGameMap);
                    return;
                }

                _gameInfoMap = map;
                WriteOk($"✓ 已加载 {_gameInfoMap.Count} 条游戏映射");

                bool changed = false;
                foreach (var kv in DefaultGameMap)
                {
                    if (!_gameInfoMap.ContainsKey(kv.Key))
                    {
                        _gameInfoMap[kv.Key] = kv.Value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveGameMap(path, _gameInfoMap);
                    WriteOk("✓ game_map.json 已补充新增条目");
                }
            }
            catch (Exception ex)
            {
                WriteErr("读取 game_map.json 出错: " + ex.Message);
                WriteWarn("将使用内存中的默认数据继续运行。");
                _gameInfoMap = new Dictionary<string, GameInfo>(DefaultGameMap);
            }
        }

        static void SaveGameMap(string path, Dictionary<string, GameInfo> map)
        {
            string json = JsonSerializer.Serialize(map, JsonWriteOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        static string GetGameName(string appId)
            => _gameInfoMap.TryGetValue(appId, out var info) ? info.Name : null;

        static string GetLocalSaveFolder(string appId)
            => _gameInfoMap.TryGetValue(appId, out var info) ? info.LocalSaveFolder : null;

        #endregion

        #region 账户选择与详情

        static void InteractiveSelect(List<AccountInfo> accounts)
        {
            while (true)
            {
                Console.WriteLine("\n请选择要进入的账户文件夹 (输入序号)，或输入 Q 退出:");
                for (int i = 0; i < accounts.Count; i++)
                {
                    Console.Write($"  {i + 1}. ");
                    WriteColoredInline(accounts[i].DisplayName, ConsoleColor.Green);
                    Console.WriteLine($"  [{accounts[i].SteamId3}]");
                }

                Console.Write("\n请输入: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrEmpty(input)) continue;
                if (input.Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("已退出。");
                    return;
                }

                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= accounts.Count)
                {
                    var acc = accounts[choice - 1];
                    var appIds = ShowAccountDetail(acc);
                    if (appIds.Count > 0)
                        BackupMenu(acc, appIds);
                }
                else
                {
                    WriteErr($"✗ 输入无效，请输入 1 ~ {accounts.Count} 之间的数字，或 Q 退出。");
                }
            }
        }

        static List<string> ShowAccountDetail(AccountInfo acc)
        {
            var appIds = new List<string>();

            Console.WriteLine();
            Console.WriteLine(new string('=', 60));
            Console.Write("进入账户: ");
            WriteColoredInline(acc.DisplayName, ConsoleColor.Green);
            Console.WriteLine();
            Console.WriteLine($"文件夹: {acc.FolderPath}");
            Console.WriteLine(new string('=', 60));

            try
            {
                var gameFolders = Directory.GetDirectories(acc.FolderPath)
                                           .Select(d => new DirectoryInfo(d))
                                           .OrderBy(d => d.Name)
                                           .ToArray();

                if (gameFolders.Length == 0)
                {
                    WriteWarn("  该账户下没有游戏数据文件夹");
                    Console.WriteLine(new string('=', 60));
                    return appIds;
                }

                Console.WriteLine($"共有 {gameFolders.Length} 个游戏数据文件夹:\n");

                int i = 1;
                foreach (var g in gameFolders)
                {
                    string appId = g.Name;
                    string gameName = GetGameName(appId);
                    int fileCount = CountFilesSafe(g.FullName);

                    Console.WriteLine($"  [{i++}] AppID: {appId}" +
                                      (string.IsNullOrEmpty(gameName) ? "" : $"  →  {gameName}"));
                    Console.WriteLine($"        最后修改 : {g.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"        数据文件数: {fileCount}");

                    appIds.Add(appId);
                }
            }
            catch (Exception ex)
            {
                WriteErr("读取账户文件夹出错: " + ex.Message);
            }

            Console.WriteLine(new string('=', 60));
            return appIds;
        }

        static int CountFilesSafe(string dir)
        {
            int count = 0;
            foreach (string sub in new[] { "remote", "local" })
            {
                try
                {
                    string p = Path.Combine(dir, sub);
                    if (Directory.Exists(p))
                        count += Directory.GetFiles(p, "*", SearchOption.AllDirectories).Length;
                }
                catch { }
            }
            return count;
        }

        #endregion

        #region 备份 / 加载菜单

        static void BackupMenu(AccountInfo acc, List<string> appIds)
        {
            string yuzuRoot = GetYuzuSoftSaveRoot();
            bool yuzuAvailable = Directory.Exists(yuzuRoot);

            while (true)
            {
                Console.WriteLine("\n请选择要备份的游戏 (输入序号)，或输入 B 返回上一级:");
                PrintAppList(appIds);

                if (yuzuAvailable)
                    WriteOk($"  L. 直接加载到本地存档 ");
                else
                    WriteWarn($"  L. 直接加载到本地存档  [不可用: 未找到 {yuzuRoot}]");

                Console.Write("\n请输入: ");
                string input = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrEmpty(input)) continue;
                if (input.Equals("B", StringComparison.OrdinalIgnoreCase)) return;

                if (input.Equals("L", StringComparison.OrdinalIgnoreCase))
                {
                    if (!yuzuAvailable)
                    {
                        WriteErr($"✗ 本地存档目录不存在: {yuzuRoot}");
                        WriteWarn("  请确认柚子社游戏已至少运行过一次并生成存档。");
                        continue;
                    }
                    HandleLoadToLocalSave(acc, appIds);
                    continue;
                }

                if (!int.TryParse(input, out int choice) || choice < 1 || choice > appIds.Count)
                {
                    WriteErr($"✗ 输入无效，请输入 1 ~ {appIds.Count} 之间的数字，或 L / B。");
                    continue;
                }

                string appId = appIds[choice - 1];
                string sourcePath = Path.Combine(acc.FolderPath, appId);
                string gameName = GetGameName(appId);

                Console.WriteLine();
                Console.WriteLine($"即将备份 AppID: {appId}");
                if (!string.IsNullOrEmpty(gameName))
                    Console.WriteLine($"游戏名  : {gameName}");
                Console.WriteLine($"源文件夹: {sourcePath}");

                string targetRoot = AskTargetPath();
                if (string.IsNullOrEmpty(targetRoot))
                {
                    WriteWarn("已取消。");
                    continue;
                }

                string safeName = GetSafeFolderName(string.IsNullOrEmpty(gameName) ? appId : gameName);
                string destFolder = Path.Combine(targetRoot,
                    $"{appId}_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}");

                try
                {
                    Console.WriteLine($"\n正在复制到: {destFolder}");
                    CopyDirectory(sourcePath, destFolder);
                    WriteOk("✓ 备份完成！");
                }
                catch (Exception ex)
                {
                    WriteErr("✗ 备份失败: " + ex.Message);
                }
            }
        }

        static void PrintAppList(List<string> appIds)
        {
            for (int i = 0; i < appIds.Count; i++)
            {
                string gameName = GetGameName(appIds[i]);
                string label = string.IsNullOrEmpty(gameName)
                    ? appIds[i]
                    : $"{appIds[i]}  ({gameName})";
                Console.WriteLine($"  {i + 1}. {label}");
            }
        }

        static string AskTargetPath()
        {
            Console.Write("\n请输入备份目标文件夹路径 (例如 D:\\SteamBackup)，直接回车取消: ");
            string input = Console.ReadLine()?.Trim() ?? "";
            return string.IsNullOrEmpty(input) ? null : input.Trim('"');
        }

        #endregion

        #region 加载到本地存档 (YuzuSoft)

        static string GetYuzuSoftSaveRoot()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YuzuSoft");

        static void HandleLoadToLocalSave(AccountInfo acc, List<string> appIds)
        {
            Console.WriteLine();
            Console.WriteLine("=== 加载到本地存档 ===");

            for (int i = 0; i < appIds.Count; i++)
            {
                string appId = appIds[i];
                string gameName = GetGameName(appId);
                string localFolder = GetLocalSaveFolder(appId);

                string suffix = "";
                if (string.IsNullOrEmpty(localFolder))
                {
                    suffix = "  [未配置本地存档目录]";
                }
                else if (!Directory.Exists(Path.Combine(GetYuzuSoftSaveRoot(), localFolder)))
                {
                    suffix = $"  [本地目录不存在: {localFolder}]";
                }

                string remote = Path.Combine(acc.FolderPath, appId, "remote");
                if (!Directory.Exists(remote))
                    suffix += "  [无 remote 目录]";

                string label = string.IsNullOrEmpty(gameName) ? appId : $"{appId}  ({gameName})";
                Console.WriteLine($"  {i + 1}. {label}{suffix}");
            }

            Console.Write("\n请选择要加载到本地的游戏 (输入序号)，或输入 B 取消: ");
            string input = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(input)) return;
            if (input.Equals("B", StringComparison.OrdinalIgnoreCase)) return;

            if (!int.TryParse(input, out int choice) || choice < 1 || choice > appIds.Count)
            {
                WriteErr("✗ 输入无效。");
                return;
            }

            string selectedAppId = appIds[choice - 1];
            string localSub = GetLocalSaveFolder(selectedAppId);

            if (string.IsNullOrEmpty(localSub))
            {
                WriteErr($"✗ AppID {selectedAppId} 未在 game_map.json 中配置 localSaveFolder，无法加载到本地。");
                return;
            }

            string remotePath = Path.Combine(acc.FolderPath, selectedAppId, "remote");
            if (!Directory.Exists(remotePath))
            {
                WriteErr($"✗ 找不到 remote 目录: {remotePath}");
                WriteWarn("  该游戏在 Steam 云存档中可能没有 remote 数据。");
                return;
            }

            string localSaveRoot = Path.Combine(GetYuzuSoftSaveRoot(), localSub);
            if (!Directory.Exists(localSaveRoot))
            {
                WriteErr($"✗ 本地存档目录不存在: {localSaveRoot}");
                WriteWarn("  请先运行该游戏一次，确保存档目录已生成。");
                return;
            }

            string savedataPath = Path.Combine(localSaveRoot, "savedata");
            string destPath = Directory.Exists(savedataPath) ? savedataPath : localSaveRoot;

            Console.WriteLine();
            Console.WriteLine("即将加载到本地存档:");
            Console.WriteLine($"  源(remote 内容): {remotePath}");
            Console.WriteLine($"  目标            : {destPath}");
            Console.Write("  确认覆盖？(Y/N): ");
            string confirm = Console.ReadLine()?.Trim() ?? "";
            if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                WriteWarn("已取消。");
                return;
            }

            try
            {
                string backupDir = destPath + $"_bak_{DateTime.Now:yyyyMMdd_HHmmss}";
                if (Directory.Exists(destPath))
                {
                    Console.WriteLine($"正在备份原存档到: {backupDir}");
                    CopyDirectory(destPath, backupDir);
                }

                Console.WriteLine($"正在复制 remote 内容到: {destPath}");
                CopyDirectory(remotePath, destPath);
                WriteOk("✓ 加载完成！");
            }
            catch (Exception ex)
            {
                WriteErr("✗ 加载失败: " + ex.Message);
            }
        }

        #endregion

        #region 文件复制辅助

        static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

            foreach (string subDir in Directory.GetDirectories(sourceDir))
                CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }

        static string GetSafeFolderName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        #endregion

        #region loginusers.vdf 解析

        static Dictionary<string, string> LoadAccountNames(string steamPath)
        {
            var result = new Dictionary<string, string>();

            string vdfPath = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (!File.Exists(vdfPath))
            {
                WriteWarn("提示: 未找到 loginusers.vdf，无法显示昵称");
                return result;
            }

            try
            {
                string content = File.ReadAllText(vdfPath);
                var matches = Regex.Matches(content, @"""(\d{17})""\s*\{(.*?)\}", RegexOptions.Singleline);

                foreach (Match m in matches)
                {
                    string steamId64 = m.Groups[1].Value;
                    string block = m.Groups[2].Value;

                    string personaName = MatchFirst(block, @"""PersonaName""\s+""([^""]*)""");
                    string accountName = MatchFirst(block, @"""AccountName""\s+""([^""]*)""") ?? "";

                    if (!string.IsNullOrEmpty(personaName))
                    {
                        personaName = personaName.Replace("\\\"", "\"");
                        result[steamId64] = string.IsNullOrEmpty(accountName)
                            ? personaName
                            : $"{personaName}  ({accountName})";
                    }
                }
            }
            catch (Exception ex)
            {
                WriteErr("读取 loginusers.vdf 出错: " + ex.Message);
            }

            return result;
        }

        static string MatchFirst(string input, string pattern)
        {
            var m = Regex.Match(input, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        static string SteamId3ToId64(string steamId3)
        {
            const long baseOffset = 76561197960265728L;
            return long.TryParse(steamId3, out long id3) ? (id3 + baseOffset).ToString() : null;
        }

        #endregion

        #region userdata 读取

        static List<AccountInfo> PrintUserDataFolders(string steamPath, Dictionary<string, string> accountNames)
        {
            var list = new List<AccountInfo>();
            string userDataPath = Path.Combine(steamPath, "userdata");

            if (!Directory.Exists(userDataPath))
            {
                WriteErr("✗ userdata 文件夹不存在");
                WriteWarn("  查找路径: " + userDataPath);
                return list;
            }

            WriteOk("✓ 找到 userdata 文件夹: " + userDataPath);
            Console.WriteLine();

            try
            {
                var folders = Directory.GetDirectories(userDataPath)
                                       .Select(d => new DirectoryInfo(d))
                                       .OrderBy(d => d.Name)
                                       .ToArray();

                if (folders.Length == 0)
                {
                    WriteWarn("  userdata 中没有子文件夹");
                    return list;
                }

                Console.WriteLine($"共有 {folders.Length} 个文件夹:");
                Console.WriteLine(new string('-', 60));

                int index = 1;
                foreach (var dirInfo in folders)
                {
                    string steamId64 = SteamId3ToId64(dirInfo.Name);
                    string displayName = "未知账户";

                    if (steamId64 != null && accountNames.TryGetValue(steamId64, out string nick))
                        displayName = nick;

                    int subFolderCount = 0;
                    try { subFolderCount = dirInfo.GetDirectories().Length; } catch { }

                    Console.WriteLine($"  [{index}] SteamID3: {dirInfo.Name}");
                    Console.WriteLine($"      SteamID64: {steamId64 ?? "N/A"}");

                    Console.Write("      昵称     : ");
                    WriteColoredInline(displayName, ConsoleColor.Green);
                    Console.WriteLine();

                    Console.WriteLine($"      最后修改 : {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"      子文件夹 : {subFolderCount}");
                    Console.WriteLine();

                    list.Add(new AccountInfo
                    {
                        SteamId3 = dirInfo.Name,
                        SteamId64 = steamId64,
                        DisplayName = displayName,
                        FolderPath = dirInfo.FullName
                    });
                    index++;
                }

                Console.WriteLine(new string('-', 60));
            }
            catch (Exception ex)
            {
                WriteErr("读取 userdata 出错: " + ex.Message);
            }

            return list;
        }

        #endregion

        #region Steam 路径查找

        static string FindSteamPath()
        {
            string path = FindFromRegistry();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                return path;

            string[] commonPaths =
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                @"D:\Steam",
                @"D:\Program Files (x86)\Steam",
                @"E:\Steam"
            };

            foreach (string p in commonPaths)
                if (Directory.Exists(p) && File.Exists(Path.Combine(p, "steam.exe")))
                    return p;

            return null;
        }

        static string FindFromRegistry()
        {
            string[] regPaths =
            {
                @"SOFTWARE\Valve\Steam",
                @"SOFTWARE\WOW6432Node\Valve\Steam"
            };

            foreach (string regPath in regPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        string p = key?.GetValue("InstallPath")?.ToString()
                                ?? key?.GetValue("SteamPath")?.ToString();
                        if (!string.IsNullOrEmpty(p)) return p;
                    }

                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                    {
                        string p = key?.GetValue("SteamPath")?.ToString();
                        if (!string.IsNullOrEmpty(p)) return p;
                    }
                }
                catch { }
            }

            return null;
        }

        #endregion
    }
}