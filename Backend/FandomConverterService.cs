using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    }
}
