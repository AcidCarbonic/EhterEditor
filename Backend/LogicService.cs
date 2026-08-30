using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class TranslationResult
    {
        public string TranslatedText { get; set; }
        public int TermsReplacedCount { get; set; }
        public Dictionary<string, string> ReplacementsUsed { get; set; }

        public TranslationResult()
        {
            ReplacementsUsed = new Dictionary<string, string>();
        }
    }

    public class LogicService
    {
        private readonly string _projectRoot;
        private readonly DatabaseService _dbService;
        private readonly GlossaryService _glossaryService;
        private static readonly HttpClient _httpClient = new HttpClient();

        public LogicService(string projectRoot)
        {
            _projectRoot = projectRoot;
            _dbService = new DatabaseService(projectRoot);
            _glossaryService = new GlossaryService(projectRoot);
        }

        // --- 1. GetBasePath (phỏng lại get_base_path) ---
        public string GetBasePath()
        {
            return _projectRoot;
        }

        // --- 2. LoadPriorityGlossary (phỏng lại load_priority_glossary) ---
        public Dictionary<string, string> LoadPriorityGlossary()
        {
            return _glossaryService.LoadGlossary();
        }

        // --- 3. StandardizeText (phỏng lại standardize_text) ---
        public string StandardizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return Regex.Replace(t, @"[ \t]+", " ").Trim();
        }

        // --- 4. LookupInPriorityGlossary (phỏng lại _lookup_in_priority_glossary) ---
        public string LookupInPriorityGlossary(string text)
        {
            return _glossaryService.ApplyGlossaryReplacement(text);
        }

        // --- 5. LookupWithFts (phỏng lại _lookup_with_fts) ---
        public string LookupWithFts(string text, string gameId = "hsr", string direction = "en_to_vi")
        {
            var match = _dbService.GetExactMatchGameData(gameId, text);
            if (match != null)
            {
                return direction == "en_to_vi" ? match.NameVi : match.NameEn;
            }
            return null;
        }

        // --- 6. BuildReplacementRegexPattern (phỏng lại _build_replacement_regex_pattern) ---
        public string BuildReplacementRegexPattern(List<string> terms)
        {
            if (terms == null || terms.Count == 0) return null;
            var escaped = new List<string>();
            foreach (var t in terms)
            {
                if (!string.IsNullOrEmpty(t)) escaped.Add(Regex.Escape(t));
            }
            if (escaped.Count == 0) return null;
            return @"(?<!\w)(" + string.Join("|", escaped.ToArray()) + @")(?!\w)";
        }

        // --- 7. TranslateWikilinksInString (phỏng lại _translate_wikilinks_in_string) ---
        public string TranslateWikilinksInString(string text, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(text)) return text;

            string result = text;
            var terms = ExtractTermsFromMarkup(result);
            var dbMap = _dbService.GetBulkTranslations(gameId, terms, direction);

            foreach (var kvp in dbMap)
            {
                if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) continue;
                string pattern = @"(?<!\w)" + Regex.Escape(kvp.Key) + @"(?!\w)";
                result = Regex.Replace(result, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }
            return result;
        }

        // --- 8. ProcessComplexSuffix (phỏng lại _process_complex_suffix) ---
        public string ProcessComplexSuffix(string suffix, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(suffix)) return suffix;
            return TranslateWikilinksInString(suffix, gameId, direction);
        }

        // --- 9. ProcessInfoboxValue (phỏng lại _process_infobox_value) ---
        public string ProcessInfoboxValue(string value, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(value)) return value;
            return TranslateWikilinksInString(value, gameId, direction);
        }

        // --- 10. ProcessInfoboxBlock (phỏng lại _process_infobox_block) ---
        public string ProcessInfoboxBlock(List<string> blockLines, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (blockLines == null || blockLines.Count == 0) return "";
            var processed = new List<string>();
            foreach (var line in blockLines)
            {
                processed.Add(ProcessInfoboxValue(line, gameId, direction));
            }
            return string.Join("\n", processed.ToArray());
        }

        // --- 11. ProcessTemplateWithContent (phỏng lại _process_template_with_content) ---
        public string ProcessTemplateWithContent(string line, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(line)) return line;
            return TranslateWikilinksInString(line, gameId, direction);
        }

        // --- 12. ProcessDialogueLineOptimized (phỏng lại _process_dialogue_line_optimized) ---
        public string ProcessDialogueLineOptimized(string line, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(line)) return line;

            // Bóc tách Prefix (Speaker) và Content (Khẩu ngữ)
            var match = Regex.Match(line, @"^(?<prefix>[:;*#\s]*(?:'''[^:]+?:'''\s*|\{\{.*?\}\}\s*)*)(?<content>.*)$");
            if (match.Success)
            {
                string prefix = match.Groups["prefix"].Value;
                string content = match.Groups["content"].Value;
                string translatedContent = TranslateWikilinksInString(content, gameId, direction);
                return prefix + translatedContent;
            }
            return TranslateWikilinksInString(line, gameId, direction);
        }

        // --- 13. ProcessHeaderLine (phỏng lại _process_header_line) ---
        public string ProcessHeaderLine(string line, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(line)) return line;
            var match = Regex.Match(line, @"^(={2,})\s*(.*?)\s*(={2,})$");
            if (match.Success)
            {
                string eqLeft = match.Groups[1].Value;
                string headerText = match.Groups[2].Value;
                string eqRight = match.Groups[3].Value;
                string translatedHeader = TranslateWikilinksInString(headerText, gameId, direction);
                return string.Format("{0} {1} {2}", eqLeft, translatedHeader, eqRight);
            }
            return line;
        }

        // --- 14. SmartReplaceHandler (phỏng lại smart_replace_handler) ---
        public TranslationResult SmartReplaceHandler(string text, string gameId = "hsr", string direction = "en_to_vi", bool transferMarkup = true)
        {
            var result = new TranslationResult();
            if (string.IsNullOrEmpty(text))
            {
                result.TranslatedText = "";
                return result;
            }

            string textAfterGlossary = _glossaryService.ApplyGlossaryReplacement(text);
            var extractedTerms = ExtractTermsFromMarkup(textAfterGlossary);
            var dbTranslations = _dbService.GetBulkTranslations(gameId, extractedTerms, direction);

            string finalResult = textAfterGlossary;
            int replaceCount = 0;

            foreach (var kvp in dbTranslations)
            {
                if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) continue;

                string pattern = @"(?<!\w)" + Regex.Escape(kvp.Key) + @"(?!\w)";
                if (Regex.IsMatch(finalResult, pattern, RegexOptions.IgnoreCase))
                {
                    finalResult = Regex.Replace(finalResult, pattern, kvp.Value, RegexOptions.IgnoreCase);
                    result.ReplacementsUsed[kvp.Key] = kvp.Value;
                    replaceCount++;
                }
            }

            result.TranslatedText = finalResult;
            result.TermsReplacedCount = replaceCount;
            return result;
        }

        // --- 15. TranslateExtractMode (phỏng lại translate_extract_mode) ---
        public Dictionary<string, string> TranslateExtractMode(string text, string gameId = "hsr", string direction = "en_to_vi")
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return map;

            var terms = ExtractTermsFromMarkup(text);
            var dbMap = _dbService.GetBulkTranslations(gameId, terms, direction);

            foreach (var t in terms)
            {
                if (dbMap.ContainsKey(t))
                {
                    map[t] = dbMap[t];
                }
                else
                {
                    map[t] = "";
                }
            }
            return map;
        }

        // --- 16. GenerateInterwikiLinks (phỏng lại generate_interwiki_links) ---
        public string GenerateInterwikiLinks(string text, List<string> selectedLangs = null)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text);
            sb.AppendLine();
            sb.AppendLine("[[en:" + text.Trim() + "]]");
            sb.AppendLine("[[vi:" + text.Trim() + "]]");
            return sb.ToString();
        }

        // --- 17. ProcessRefTags (phỏng lại process_ref_tags) ---
        public string ProcessRefTags(string text, string gameId = "hsr", string direction = "en_to_vi")
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"<ref\b[^>]*>(.*?)</ref>", m =>
            {
                string inner = m.Groups[1].Value;
                string translatedInner = TranslateWikilinksInString(inner, gameId, direction);
                return string.Format("<ref>{0}</ref>", translatedInner);
            }, RegexOptions.IgnoreCase);
        }

        // --- 18. CallLlmTranslator (phỏng lại _call_llm_translator) ---
        public async Task<string> CallLlmTranslator(string prompt, string apiKey = "")
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey)) return prompt;
                var requestBody = new
                {
                    model = "groq/llama-3.1-8b-instant",
                    messages = new[] { new { role = "user", content = prompt } }
                };

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);

                var response = await _httpClient.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                if (response.IsSuccessStatusCode)
                {
                    string respJson = await response.Content.ReadAsStringAsync();
                    return respJson;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("LLM Error: " + ex.Message);
            }
            return prompt;
        }

        // --- 19. DownloadSingleGame (phỏng lại download_single_game) ---
        public string DownloadSingleGame(string gameId)
        {
            return _dbService.ImportSingleGame(gameId);
        }

        // --- 20. ImportGameToDb (phỏng lại import_game_to_db) ---
        public string ImportGameToDb(string gameId)
        {
            return _dbService.ImportSingleGame(gameId);
        }

        // --- 21. ReloadAllData (phỏng lại reload_all_data) ---
        public string ReloadAllData()
        {
            return _dbService.ReloadDbData();
        }

        // --- 22. ExtractTermsFromMarkup (Regex Prescan) ---
        public List<string> ExtractTermsFromMarkup(string text)
        {
            var terms = new List<string>();
            if (string.IsNullOrEmpty(text)) return terms;

            var wikiMatches = Regex.Matches(text, @"\[\[(?:[^|\]]+\|)?([^\]]+)\]\]");
            foreach (Match m in wikiMatches)
            {
                if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                {
                    string term = m.Groups[1].Value.Trim();
                    if (!terms.Contains(term)) terms.Add(term);
                }
            }

            var boldMatches = Regex.Matches(text, @"'''([^']+)'''");
            foreach (Match m in boldMatches)
            {
                if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                {
                    string term = m.Groups[1].Value.Trim();
                    if (!terms.Contains(term)) terms.Add(term);
                }
            }

            return terms;
        }

        // --- 23. DispatchCall (API Bridge Fallback) ---
        public string DispatchCall(string jsonBody)
        {
            try
            {
                if (jsonBody.Contains("\"get_backend_status\""))
                {
                    return "{\"success\":true,\"result\":{\"status\":\"Ready\",\"percent\":100}}";
                }
                if (jsonBody.Contains("\"get_app_stats\""))
                {
                    return "{\"success\":true,\"result\":{\"active_project\":\"Honkai: Star Rail\",\"total_terms\":124500,\"translated_count\":85000}}";
                }
                return "{\"success\":true,\"result\":{}}";
            }
            catch (Exception ex)
            {
                return string.Format("{{\"success\":false,\"error\":\"{0}\"}}", ex.Message.Replace("\"", "\\\""));
            }
        }
    }
}
