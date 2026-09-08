using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class GlossaryService
    {
        private static GlossaryService _instance;
        public static GlossaryService Instance
        {
            get
            {
                if (_instance == null)
                {
                    string root = GetAppBaseDirectory();
                    _instance = new GlossaryService(root);
                }
                return _instance;
            }
        }

        private static string GetAppBaseDirectory()
        {
            try
            {
                string loc = typeof(GlossaryService).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string dir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string ResolveProjectRoot(string projectRoot)
        {
            string dir = !string.IsNullOrEmpty(projectRoot) ? projectRoot : GetAppBaseDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "EtherEditorNative.csproj")) || 
                    File.Exists(Path.Combine(dir, "data", "priority_glossary.json")) ||
                    Directory.Exists(Path.Combine(dir, "Views")))
                {
                    return dir;
                }
                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return GetAppBaseDirectory();
        }

        private readonly string _glossaryPath;
        private Dictionary<string, Dictionary<string, string>> _categorizedGlossary;
        private Dictionary<string, string> _glossary;

        public GlossaryService(string projectRoot)
        {
            _glossaryPath = ResolveGlossaryPath(projectRoot);
            LoadCategorizedGlossary();
        }

        private static string ResolveGlossaryPath(string projectRoot)
        {
            string root = ResolveProjectRoot(projectRoot);
            string appDir = GetAppBaseDirectory();
            var candidates = new string[]
            {
                Path.Combine(root, "data", "priority_glossary.json"),
                Path.Combine(root, "priority_glossary.json"),
                Path.Combine(appDir, "data", "priority_glossary.json"),
                Path.Combine(appDir, "priority_glossary.json")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    return Path.GetFullPath(c);
                }
            }

            return Path.Combine(root, "data", "priority_glossary.json");
        }

        public Dictionary<string, Dictionary<string, string>> LoadCategorizedGlossary()
        {
            var categorized = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            categorized["global"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            categorized["hsr"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            categorized["genshin"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            categorized["zzz"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(_glossaryPath))
                {
                    string json = File.ReadAllText(_glossaryPath);
                    var serializer = new JavaScriptSerializer();
                    var rawDict = serializer.Deserialize<Dictionary<string, object>>(json);
                    if (rawDict != null)
                    {
                        foreach (KeyValuePair<string, object> kvp in rawDict)
                        {
                            string catKey = kvp.Key.ToLower();
                            if (!categorized.ContainsKey(catKey))
                            {
                                categorized[catKey] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            }

                            Dictionary<string, object> subDict = kvp.Value as Dictionary<string, object>;
                            if (subDict != null)
                            {
                                object enViObj;
                                if (subDict.TryGetValue("en_to_vi", out enViObj))
                                {
                                    Dictionary<string, object> enVi = enViObj as Dictionary<string, object>;
                                    if (enVi != null)
                                    {
                                        foreach (KeyValuePair<string, object> term in enVi)
                                        {
                                            if (term.Value != null)
                                            {
                                                categorized[catKey][term.Key] = term.Value.ToString();
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (KeyValuePair<string, object> term in subDict)
                                    {
                                        if (term.Value != null)
                                        {
                                            categorized[catKey][term.Key] = term.Value.ToString();
                                        }
                                    }
                                }
                            }
                            else if (kvp.Value is string)
                            {
                                categorized["global"][kvp.Key] = kvp.Value.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GlossaryService Load Error: " + ex.Message);
            }
            _categorizedGlossary = categorized;

            // Update flat dict for fast lookup
            _glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, string>> catKvp in _categorizedGlossary)
            {
                foreach (KeyValuePair<string, string> termKvp in catKvp.Value)
                {
                    _glossary[termKvp.Key] = termKvp.Value;
                }
            }

            return _categorizedGlossary;
        }

        public Dictionary<string, Dictionary<string, string>> GetCategorizedGlossary()
        {
            if (_categorizedGlossary == null) LoadCategorizedGlossary();
            return _categorizedGlossary;
        }

        public Dictionary<string, string> LoadGlossary()
        {
            LoadCategorizedGlossary();
            return _glossary;
        }

        public Dictionary<string, string> GetGlossary()
        {
            if (_glossary == null) LoadCategorizedGlossary();
            return _glossary;
        }

        public void AddTermToCategory(string category, string source, string target)
        {
            if (string.IsNullOrEmpty(source)) return;
            if (string.IsNullOrEmpty(category)) category = "global";
            category = category.ToLower();

            if (_categorizedGlossary == null) LoadCategorizedGlossary();

            if (!_categorizedGlossary.ContainsKey(category))
            {
                _categorizedGlossary[category] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            _categorizedGlossary[category][source] = target;
            if (_glossary == null) _glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _glossary[source] = target;
        }

        public void RemoveTermFromCategory(string category, string source)
        {
            if (string.IsNullOrEmpty(source)) return;
            if (_categorizedGlossary == null) LoadCategorizedGlossary();

            if (!string.IsNullOrEmpty(category) && _categorizedGlossary.ContainsKey(category.ToLower()))
            {
                _categorizedGlossary[category.ToLower()].Remove(source);
            }
            else
            {
                foreach (KeyValuePair<string, Dictionary<string, string>> catKvp in _categorizedGlossary)
                {
                    catKvp.Value.Remove(source);
                }
            }

            if (_glossary != null) _glossary.Remove(source);
        }

        public void AddTerm(string source, string target)
        {
            AddTermToCategory("global", source, target);
        }

        public void RemoveTerm(string source)
        {
            RemoveTermFromCategory(null, source);
        }

        public Dictionary<string, string> SearchTerms(string query)
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_glossary == null) LoadGlossary();

            if (string.IsNullOrEmpty(query))
            {
                foreach (KeyValuePair<string, string> kvp in _glossary) res[kvp.Key] = kvp.Value;
                return res;
            }

            foreach (KeyValuePair<string, string> kvp in _glossary)
            {
                if (kvp.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    kvp.Value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    res[kvp.Key] = kvp.Value;
                }
            }

            return res;
        }

        public bool SaveCategorizedGlossary()
        {
            try
            {
                if (_categorizedGlossary == null) return false;

                var saveStructure = new Dictionary<string, object>();
                foreach (KeyValuePair<string, Dictionary<string, string>> catKvp in _categorizedGlossary)
                {
                    var enToVi = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, string> termKvp in catKvp.Value)
                    {
                        enToVi[termKvp.Key] = termKvp.Value;
                    }
                    var catData = new Dictionary<string, object>();
                    catData["en_to_vi"] = enToVi;
                    catData["vi_to_en"] = new Dictionary<string, string>();
                    saveStructure[catKvp.Key] = catData;
                }

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(saveStructure);
                string dir = Path.GetDirectoryName(_glossaryPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_glossaryPath, json);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GlossaryService Save Error: " + ex.Message);
                return false;
            }
        }

        public bool SaveGlossary(Dictionary<string, string> glossaryDict)
        {
            return SaveCategorizedGlossary();
        }

        public string ApplyGlossaryReplacement(string text)
        {
            if (string.IsNullOrEmpty(text) || _glossary == null || _glossary.Count == 0)
                return text;

            string result = text;
            foreach (KeyValuePair<string, string> entry in _glossary)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;

                string pattern = @"\b" + Regex.Escape(entry.Key) + @"\b";
                result = Regex.Replace(result, pattern, entry.Value, RegexOptions.IgnoreCase);
            }
            return result;
        }
    }
}

