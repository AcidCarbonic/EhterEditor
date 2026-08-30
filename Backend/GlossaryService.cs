using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class GlossaryService
    {
        private readonly string _glossaryPath;
        private Dictionary<string, string> _glossary;

        public GlossaryService(string projectRoot)
        {
            _glossaryPath = Path.Combine(projectRoot, "data", "priority_glossary.json");
            if (!File.Exists(_glossaryPath))
            {
                _glossaryPath = Path.Combine(projectRoot, "frontend", "data", "priority_glossary.json");
            }
            LoadGlossary();
        }

        public Dictionary<string, string> LoadGlossary()
        {
            _glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_glossaryPath))
                {
                    string json = File.ReadAllText(_glossaryPath);
                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _glossary[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GlossaryService Load Error: " + ex.Message);
            }
            return _glossary;
        }

        public bool SaveGlossary(Dictionary<string, string> glossaryDict)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(glossaryDict);
                string dir = Path.GetDirectoryName(_glossaryPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_glossaryPath, json);
                _glossary = new Dictionary<string, string>(glossaryDict, StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GlossaryService Save Error: " + ex.Message);
                return false;
            }
        }

        public string ApplyGlossaryReplacement(string text)
        {
            if (string.IsNullOrEmpty(text) || _glossary == null || _glossary.Count == 0)
                return text;

            string result = text;
            foreach (var entry in _glossary)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;

                // Word boundary regex replacement
                string pattern = @"\b" + Regex.Escape(entry.Key) + @"\b";
                result = Regex.Replace(result, pattern, entry.Value, RegexOptions.IgnoreCase);
            }
            return result;
        }
    }
}
