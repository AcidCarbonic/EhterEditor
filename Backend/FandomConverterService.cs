using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class FandomConverterService
    {
        public static string GetNickname(string lang, string gameId = "hsr")
        {
            string cleanGame = gameId.Contains("_") ? gameId.Split('_')[0] : gameId;
            if (lang == "en")
            {
                if (cleanGame == "hsr") return "(Trailblazer)";
                if (cleanGame == "genshin") return "(Traveler)";
                if (cleanGame == "zzz") return "(Proxy)";
                return "(Proxy)";
            }
            else if (lang == "vi")
            {
                if (cleanGame == "hsr") return "(Nhà Khai Phá)";
                if (cleanGame == "genshin") return "(Nhà Lữ Hành)";
                if (cleanGame == "zzz") return "(Proxy)";
                return "(Proxy)";
            }
            return "(Player)";
        }

        public static string NormalizeText(string text, string lang, string gameId)
        {
            if (string.IsNullOrEmpty(text)) return "";

            text = text.Replace(@"\""", "\"");
            text = Regex.Replace(text, @"</?unbreak>", "", RegexOptions.IgnoreCase);
            text = text.Replace("—", "&mdash;");

            text = Regex.Replace(text, @"<color=#dbc291ff>(.*?)</color>", @"{{Color|keyword|nobold=1|$1}}", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<color=#f29e38ff>(.*?)</color>", @"{{Color|highlight|nobold=1|$1}}", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<color=(.*?)>(.*?)</color>", @"{{Color|$1|$2}}", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<size=(.*?)>(.*?)</size>", @"{{Size|$1|$2}}", RegexOptions.IgnoreCase);

            text = text.Replace(@"\\n", "<br />").Replace(@"\n", "<br />");

            string nickname = GetNickname(lang, gameId);
            text = text.Replace("{NICKNAME}", nickname);

            text = Regex.Replace(text, @"\{M#(.*?)\}\{F#(.*?)\}", @"{{MC|f=$2|m=$1}}", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{F#(.*?)\}\{M#(.*?)\}", @"{{MC|f=$1|m=$2}}", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"\{RUBY_B#(.*?)\}(.*?)\{RUBY_E#\}", @"{{Rubi|$2|$1}}", RegexOptions.IgnoreCase);

            text = Regex.Replace(text, @"</?i>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</?b>", "", RegexOptions.IgnoreCase);

            return text.Trim();
        }

        public bool ProcessAndMerge(string gameId, string enPath, string viPath, string outputPath)
        {
            try
            {
                if (!File.Exists(enPath) || !File.Exists(viPath)) return false;

                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;

                string enJson = File.ReadAllText(enPath);
                string viJson = File.ReadAllText(viPath);

                var dataEn = serializer.Deserialize<Dictionary<string, object>>(enJson);
                var dataVi = serializer.Deserialize<Dictionary<string, object>>(viJson);

                if (dataEn == null || dataVi == null) return false;

                var mergedData = new Dictionary<string, Dictionary<string, string>>();
                var allKeys = new HashSet<string>(dataEn.Keys);
                foreach (var k in dataVi.Keys) allKeys.Add(k);

                foreach (string key in allKeys)
                {
                    object enItem = dataEn.ContainsKey(key) ? dataEn[key] : null;
                    object viItem = dataVi.ContainsKey(key) ? dataVi[key] : null;

                    if (enItem == null) enItem = viItem;
                    if (viItem == null) viItem = enItem;

                    var outputItem = new Dictionary<string, string>();

                    string enStr = enItem as string;
                    if (enStr != null)
                    {
                        string normEn = NormalizeText(enStr, "en", gameId);
                        string viStr = viItem as string;
                        string normVi = NormalizeText(viStr != null ? viStr : normEn, "vi", gameId);
                        outputItem["name_en"] = normEn;
                        outputItem["name_vi"] = normVi;
                        outputItem["description_en"] = "";
                        outputItem["description_vi"] = "";
                    }
                    else
                    {
                        var enDict = enItem as Dictionary<string, object>;
                        if (enDict != null)
                        {
                            string enName = enDict.ContainsKey("name") && enDict["name"] != null ? enDict["name"].ToString() : "";
                            string enDesc = enDict.ContainsKey("description") && enDict["description"] != null ? enDict["description"].ToString() : "";

                            var viDict = viItem as Dictionary<string, object>;
                            if (viDict == null) viDict = enDict;

                            string viName = viDict.ContainsKey("name") && viDict["name"] != null ? viDict["name"].ToString() : enName;
                            string viDesc = viDict.ContainsKey("description") && viDict["description"] != null ? viDict["description"].ToString() : enDesc;

                            outputItem["name_en"] = NormalizeText(enName, "en", gameId);
                            outputItem["name_vi"] = NormalizeText(viName, "vi", gameId);
                            outputItem["description_en"] = NormalizeText(enDesc, "en", gameId);
                            outputItem["description_vi"] = NormalizeText(viDesc, "vi", gameId);
                        }
                    }

                    mergedData[key] = outputItem;
                }

                string outputJson = serializer.Serialize(mergedData);
                string dir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(outputPath, outputJson);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FandomConverter ProcessAndMerge Error: " + ex.Message);
                return false;
            }
        }

        private static string GetProxyDomain()
        {
            try
            {
                string current = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 4; i++)
                {
                    string envPath = Path.Combine(current, ".env");
                    if (File.Exists(envPath))
                    {
                        foreach (string line in File.ReadAllLines(envPath))
                        {
                            string trimmed = line.Trim();
                            if (trimmed.StartsWith("#") || !trimmed.Contains("=")) continue;
                            string[] parts = trimmed.Split(new[] { '=' }, 2);
                            if (parts.Length == 2 && parts[0].Trim() == "PROXY_SERVER_DOMAIN")
                            {
                                string val = parts[1].Trim().Trim('"', '\'');
                                if (!string.IsNullOrEmpty(val)) return val;
                            }
                        }
                        break;
                    }
                    DirectoryInfo parent = Directory.GetParent(current);
                    if (parent == null) break;
                    current = parent.FullName;
                }
            }
            catch { }
            return "fandom-proxy.vercel.app";
        }

        public static async Task<int> GetFandomArticleCountAsync(string domain, string wikiPath = "/")
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    string proxyDomain = GetProxyDomain();
                    string proxyUrl = "https://" + proxyDomain + wikiPath + "api.php?action=query&meta=siteinfo&siprop=statistics&format=json";

                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, proxyUrl);
                    request.Headers.Add("x-target-host", domain);

                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var serializer = new JavaScriptSerializer();
                        var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                        if (dict != null && dict.ContainsKey("query"))
                        {
                            var query = dict["query"] as Dictionary<string, object>;
                            if (query != null && query.ContainsKey("statistics"))
                            {
                                var stats = query["statistics"] as Dictionary<string, object>;
                                if (stats != null && stats.ContainsKey("articles"))
                                {
                                    return Convert.ToInt32(stats["articles"]);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetFandomArticleCount Error: " + ex.Message);
            }
            return 0;
        }

        public static async Task<int> GetFandomArticleCountCachedAsync(string domain, string wikiPath = "/", bool forceRefresh = false)
        {
            string cacheKey = domain + wikiPath;
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                string projectRoot = GetProxyDomain(); // Get base path fallback
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cacheFile = Path.Combine(baseDir, "gamedata", "fandom_stats_cache.json");

                // Check directory
                string cacheDir = Path.GetDirectoryName(cacheFile);
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                var serializer = new JavaScriptSerializer();
                Dictionary<string, object> cacheData = null;

                // 1. Read existing cache file
                if (!forceRefresh && File.Exists(cacheFile))
                {
                    try
                    {
                        string cacheJson = File.ReadAllText(cacheFile);
                        cacheData = serializer.Deserialize<Dictionary<string, object>>(cacheJson);
                        object lastUp = (cacheData != null && cacheData.ContainsKey("last_updated")) ? cacheData["last_updated"] : null;
                        if (cacheData != null && lastUp != null && lastUp.ToString() == todayStr)
                        {
                            if (cacheData.ContainsKey("stats"))
                            {
                                var statsDict = cacheData["stats"] as Dictionary<string, object>;
                                if (statsDict != null && statsDict.ContainsKey(cacheKey))
                                {
                                    int cachedCount = Convert.ToInt32(statsDict[cacheKey]);
                                    if (cachedCount > 0) return cachedCount;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. Fetch fresh stats from API
                int freshCount = await GetFandomArticleCountAsync(domain, wikiPath);
                if (freshCount <= 0) return 0;

                // 3. Update cache JSON
                if (cacheData == null) cacheData = new Dictionary<string, object>();
                cacheData["last_updated"] = todayStr;

                Dictionary<string, object> stats = null;
                if (cacheData.ContainsKey("stats")) stats = cacheData["stats"] as Dictionary<string, object>;
                if (stats == null) stats = new Dictionary<string, object>();

                stats[cacheKey] = freshCount;
                cacheData["stats"] = stats;

                File.WriteAllText(cacheFile, serializer.Serialize(cacheData));
                return freshCount;
            }
            catch { }

            return await GetFandomArticleCountAsync(domain, wikiPath);
        }

        public static async Task<int> GetFandomEditsCountAsync(string domain, string wikiPath = "/")
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    string proxyDomain = GetProxyDomain();
                    string proxyUrl = "https://" + proxyDomain + wikiPath + "api.php?action=query&meta=siteinfo&siprop=statistics&format=json";

                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, proxyUrl);
                    request.Headers.Add("x-target-host", domain);

                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var serializer = new JavaScriptSerializer();
                        var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                        if (dict != null && dict.ContainsKey("query"))
                        {
                            var query = dict["query"] as Dictionary<string, object>;
                            if (query != null && query.ContainsKey("statistics"))
                            {
                                var stats = query["statistics"] as Dictionary<string, object>;
                                if (stats != null && stats.ContainsKey("edits"))
                                {
                                    return Convert.ToInt32(stats["edits"]);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetFandomEditsCount Error: " + ex.Message);
            }
            return 0;
        }

        public static async Task<int> GetFandomEditsCountCachedAsync(string domain, string wikiPath = "/", bool forceRefresh = false)
        {
            string cacheKey = domain + wikiPath + "_edits";
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cacheFile = Path.Combine(baseDir, "gamedata", "fandom_stats_cache.json");

                var serializer = new JavaScriptSerializer();
                Dictionary<string, object> cacheData = null;

                if (!forceRefresh && File.Exists(cacheFile))
                {
                    try
                    {
                        string cacheJson = File.ReadAllText(cacheFile);
                        cacheData = serializer.Deserialize<Dictionary<string, object>>(cacheJson);
                        object lastUp = (cacheData != null && cacheData.ContainsKey("last_updated")) ? cacheData["last_updated"] : null;
                        if (cacheData != null && lastUp != null && lastUp.ToString() == todayStr)
                        {
                            if (cacheData.ContainsKey("stats"))
                            {
                                var statsDict = cacheData["stats"] as Dictionary<string, object>;
                                if (statsDict != null && statsDict.ContainsKey(cacheKey))
                                {
                                    int cachedCount = Convert.ToInt32(statsDict[cacheKey]);
                                    if (cachedCount > 0) return cachedCount;
                                }
                            }
                        }
                    }
                    catch { }
                }

                int freshCount = await GetFandomEditsCountAsync(domain, wikiPath);
                if (freshCount <= 0) return 0;

                if (cacheData == null) cacheData = new Dictionary<string, object>();
                cacheData["last_updated"] = todayStr;

                Dictionary<string, object> stats = null;
                if (cacheData.ContainsKey("stats")) stats = cacheData["stats"] as Dictionary<string, object>;
                if (stats == null) stats = new Dictionary<string, object>();

                stats[cacheKey] = freshCount;
                cacheData["stats"] = stats;

                File.WriteAllText(cacheFile, serializer.Serialize(cacheData));
                return freshCount;
            }
            catch { }

            return await GetFandomEditsCountAsync(domain, wikiPath);
        }
    }
}
