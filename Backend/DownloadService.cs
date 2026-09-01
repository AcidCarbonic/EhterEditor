using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class GameFileConfig
    {
        public string EnSource { get; set; }
        public string ViSource { get; set; }
        public string EnDest { get; set; }
        public string ViDest { get; set; }
        public string MergedPrefix { get; set; }
    }

    public class GameRepoConfig
    {
        public string GameId { get; set; }
        public string RepoUrl { get; set; }
        public string Branch { get; set; }
        public string ApiUrl { get; set; }
        public Dictionary<string, GameFileConfig> Files { get; set; }

        public GameRepoConfig()
        {
            Files = new Dictionary<string, GameFileConfig>();
        }
    }

    public class DownloadService
    {
        private readonly string _projectRoot;
        private readonly string _gameDataDir;
        private readonly DatabaseService _databaseService;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public Dictionary<string, GameRepoConfig> GamesConfig { get; private set; }

        public DownloadService(string projectRoot, DatabaseService databaseService)
        {
            _projectRoot = projectRoot;
            _gameDataDir = Path.Combine(projectRoot, "gamedata");
            _databaseService = databaseService;
            Directory.CreateDirectory(_gameDataDir);

            InitializeGamesConfig();
        }

        private void InitializeGamesConfig()
        {
            GamesConfig = new Dictionary<string, GameRepoConfig>();

            // 1. GENSHIN IMPACT
            var genshin = new GameRepoConfig
            {
                GameId = "genshin",
                RepoUrl = "https://gitlab.com/Dimbreath/AnimeGameData.git",
                Branch = "master",
                ApiUrl = "https://gitlab.com/api/v4/projects/Dimbreath%2FAnimeGameData/repository/commits?ref_name=master&per_page=1"
            };
            genshin.Files["base"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMapEN.json",
                ViSource = "TextMap/TextMapVI.json",
                EnDest = Path.Combine(_gameDataDir, "genshin_en.json"),
                ViDest = Path.Combine(_gameDataDir, "genshin_vi.json"),
                MergedPrefix = "genshin_base"
            };
            genshin.Files["medium"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMap_MediumEN.json",
                ViSource = "TextMap/TextMap_MediumVI.json",
                EnDest = Path.Combine(_gameDataDir, "genshin_medium_en.json"),
                ViDest = Path.Combine(_gameDataDir, "genshin_medium_vi.json"),
                MergedPrefix = "genshin_medium"
            };
            GamesConfig["genshin"] = genshin;

            // 2. HONKAI: STAR RAIL
            var hsr = new GameRepoConfig
            {
                GameId = "hsr",
                RepoUrl = "https://gitlab.com/Dimbreath/turnbasedgamedata.git",
                Branch = "main",
                ApiUrl = "https://gitlab.com/api/v4/projects/Dimbreath%2Fturnbasedgamedata/repository/commits?ref_name=main&per_page=1"
            };
            hsr.Files["base"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMapEN.json",
                ViSource = "TextMap/TextMapVI.json",
                EnDest = Path.Combine(_gameDataDir, "hsr_en.json"),
                ViDest = Path.Combine(_gameDataDir, "hsr_vi.json"),
                MergedPrefix = "hsr_base"
            };
            hsr.Files["main"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMapMainEN.json",
                ViSource = "TextMap/TextMapMainVI.json",
                EnDest = Path.Combine(_gameDataDir, "hsr_main_en.json"),
                ViDest = Path.Combine(_gameDataDir, "hsr_main_vi.json"),
                MergedPrefix = "hsr_main"
            };
            GamesConfig["hsr"] = hsr;

            // 3. ZENLESS ZONE ZERO
            var zzz = new GameRepoConfig
            {
                GameId = "zzz",
                RepoUrl = "https://git.mero.moe/dimbreath/ZenlessData.git",
                Branch = "master",
                ApiUrl = "https://git.mero.moe/api/v1/repos/dimbreath/ZenlessData/commits?limit=1"
            };
            zzz.Files["base"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMap_ENTemplateTb.json",
                ViSource = "TextMap/TextMap_VITemplateTb.json",
                EnDest = Path.Combine(_gameDataDir, "zzz_en.json"),
                ViDest = Path.Combine(_gameDataDir, "zzz_vi.json"),
                MergedPrefix = "zzz_base"
            };
            zzz.Files["login"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMap_Login_ENTemplateTb.json",
                ViSource = "TextMap/TextMap_Login_VITemplateTb.json",
                EnDest = Path.Combine(_gameDataDir, "zzz_login_en.json"),
                ViDest = Path.Combine(_gameDataDir, "zzz_login_vi.json"),
                MergedPrefix = "zzz_login"
            };
            zzz.Files["overwrite"] = new GameFileConfig
            {
                EnSource = "TextMap/TextMap_ENOverwriteTemplateTb.json",
                ViSource = "TextMap/TextMap_VIOverwriteTemplateTb.json",
                EnDest = Path.Combine(_gameDataDir, "zzz_overwrite_en.json"),
                ViDest = Path.Combine(_gameDataDir, "zzz_overwrite_vi.json"),
                MergedPrefix = "zzz_overwrite"
            };
            GamesConfig["zzz"] = zzz;
        }

        private string GetRawUrl(string repoUrl, string branch, string filePath)
        {
            repoUrl = repoUrl.TrimEnd('/');
            if (repoUrl.EndsWith(".git")) repoUrl = repoUrl.Substring(0, repoUrl.Length - 4);

            if (repoUrl.Contains("gitlab.com"))
                return string.Format("{0}/-/raw/{1}/{2}", repoUrl, branch, filePath);
            else if (repoUrl.Contains("git.mero.moe"))
                return string.Format("{0}/raw/branch/{1}/{2}", repoUrl, branch, filePath);
            
            return string.Format("{0}/raw/{1}/{2}", repoUrl, branch, filePath);
        }

        // --- MAIN DOWNLOAD & IMPORT PIPELINE ---
        public async Task<bool> DownloadAndImportGameDataAsync(string gameId, Action<double, string, string> progressCallback)
        {
            if (string.IsNullOrEmpty(gameId) || !GamesConfig.ContainsKey(gameId)) return false;

            var config = GamesConfig[gameId];
            int totalParts = config.Files.Count;
            int totalFilesToDownload = totalParts * 2;
            int currentFileIndex = 0;

            // 1. STAGE 1: MULTI-FILE DOWNLOAD FROM GIT
            foreach (var partPair in config.Files)
            {
                string partKey = partPair.Key;
                var filesConfig = partPair.Value;

                // Download English file
                currentFileIndex++;
                bool dlEn = await DownloadSingleFileAsync(config, filesConfig.EnSource, filesConfig.EnDest, 
                    partKey, "EN", currentFileIndex, totalFilesToDownload, progressCallback);
                if (!dlEn) return false;

                // Download Vietnamese file
                currentFileIndex++;
                bool dlVi = await DownloadSingleFileAsync(config, filesConfig.ViSource, filesConfig.ViDest, 
                    partKey, "VI", currentFileIndex, totalFilesToDownload, progressCallback);
                if (!dlVi) return false;
            }

            // 2. STAGE 2: MERGE MULTI-FILE PARTS INTO MERGED GAME JSON
            if (progressCallback != null) progressCallback(75, "75%", "ĐANG GỘP CÁC PHÂN ĐOẠN DATA...");
            await Task.Delay(500);

            string mergedJsonPath = Path.Combine(_gameDataDir, string.Format("{0}.json", gameId));
            bool mergeSuccess = MergeMultiFileParts(config, mergedJsonPath);
            if (!mergeSuccess) return false;

            // 3. STAGE 3: INGEST MERGED DATA INTO SQLITE DATABASE
            if (progressCallback != null) progressCallback(90, "90%", "NẠP VÀO SQLITE DATABASE (bot_data.db)...");
            await Task.Delay(500);

            try
            {
                using (var conn = new System.Data.Odbc.OdbcConnection(string.Format("Driver={{SQLite3 ODBC Driver}};Database={0};", Path.Combine(_projectRoot, "bot_data.db"))))
                {
                    conn.Open();
                    _databaseService.CreateTables(conn);
                    _databaseService.PopulateGamesTable(conn);
                    _databaseService.PopulateGameDataFromJson(conn, (msg, pct) =>
                    {
                        if (progressCallback != null) progressCallback(90 + (pct * 0.08), string.Format("{0}%", (int)(90 + (pct * 0.08))), msg);
                    }, gameId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Database Ingestion Exception: " + ex.Message);
            }

            // 4. STAGE 4: CLEANUP TEMP EN/VI MULTI-FILES TO SAVE DISK SPACE (KEEP ONLY SQLITE DB & MERGED FILE)
            CleanUpTempFiles(config);

            if (progressCallback != null) progressCallback(100, "100%", "HOÀN TẤT VÀ TỐI ƯU CSDL!");
            return true;
        }

        private async Task<bool> DownloadSingleFileAsync(GameRepoConfig config, string sourcePath, string destPath, 
            string partKey, string langKey, int currentFileIndex, int totalFiles, Action<double, string, string> progressCallback)
        {
            string url = GetRawUrl(config.RepoUrl, config.Branch, sourcePath);
            string tempFile = destPath + ".tmp";

            try
            {
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode) return false;

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long downloadedBytes = 0;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[65536];
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;

                            if (progressCallback != null)
                            {
                                double filePct = totalBytes > 0 ? (double)downloadedBytes / totalBytes : 0.5;
                                double overallStagePct = ((currentFileIndex - 1) + filePct) / totalFiles * 70; // 0 - 70% range for downloads
                                double mbDownloaded = (double)downloadedBytes / (1024 * 1024);

                                string statusMsg = string.Format("TẢI {0} {1} ({2:F1} MB)...", partKey.ToUpper(), langKey.ToUpper(), mbDownloaded);
                                progressCallback(overallStagePct, string.Format("{0}%", (int)overallStagePct), statusMsg);
                            }
                        }
                    }
                }

                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tempFile, destPath);

                // Decompress GZIP if needed
                CheckAndDecompressGzip(destPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Download Exception: " + ex.Message);
                if (File.Exists(tempFile)) try { File.Delete(tempFile); } catch { }
                return false;
            }
        }

        private void CheckAndDecompressGzip(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                byte[] header = new byte[2];
                using (var fs = File.OpenRead(filePath))
                {
                    if (fs.Read(header, 0, 2) < 2) return;
                }

                if (header[0] == 0x1f && header[1] == 0x8b)
                {
                    string decompressedFile = filePath + ".decomp";
                    using (var originalStream = File.OpenRead(filePath))
                    using (var gzipStream = new GZipStream(originalStream, CompressionMode.Decompress))
                    using (var decompressedStream = File.Create(decompressedFile))
                    {
                        gzipStream.CopyTo(decompressedStream);
                    }
                    File.Delete(filePath);
                    File.Move(decompressedFile, filePath);
                }
            }
            catch { }
        }

        private bool MergeMultiFileParts(GameRepoConfig config, string mergedDestPath)
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var masterDict = new Dictionary<string, Dictionary<string, string>>();

                foreach (var partPair in config.Files)
                {
                    var filesConfig = partPair.Value;
                    if (!File.Exists(filesConfig.EnDest) || !File.Exists(filesConfig.ViDest)) continue;

                    string enContent = File.ReadAllText(filesConfig.EnDest);
                    string viContent = File.ReadAllText(filesConfig.ViDest);

                    var enDict = serializer.Deserialize<Dictionary<string, object>>(enContent);
                    var viDict = serializer.Deserialize<Dictionary<string, object>>(viContent);

                    if (enDict != null)
                    {
                        foreach (var kvp in enDict)
                        {
                            string itemId = kvp.Key;
                            string nameEn = ConvertItemToString(kvp.Value);
                            string nameVi = viDict != null && viDict.ContainsKey(itemId) ? ConvertItemToString(viDict[itemId]) : nameEn;

                            if (!masterDict.ContainsKey(itemId))
                            {
                                masterDict[itemId] = new Dictionary<string, string>
                                {
                                    { "name_en", nameEn },
                                    { "name_vi", nameVi }
                                };
                            }
                        }
                    }
                }

                string mergedJson = serializer.Serialize(masterDict);
                File.WriteAllText(mergedDestPath, mergedJson);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Merge Multi-File Exception: " + ex.Message);
                return false;
            }
        }

        private string ConvertItemToString(object obj)
        {
            if (obj == null) return "";
            string s = obj as string;
            if (s != null) return s;

            var dict = obj as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.ContainsKey("Text") && dict["Text"] != null) return dict["Text"].ToString();
                if (dict.ContainsKey("Name") && dict["Name"] != null) return dict["Name"].ToString();
            }
            return obj.ToString();
        }

        private void CleanUpTempFiles(GameRepoConfig config)
        {
            foreach (var partPair in config.Files)
            {
                try
                {
                    if (File.Exists(partPair.Value.EnDest)) File.Delete(partPair.Value.EnDest);
                    if (File.Exists(partPair.Value.ViDest)) File.Delete(partPair.Value.ViDest);
                }
                catch { }
            }
        }
    }
}
