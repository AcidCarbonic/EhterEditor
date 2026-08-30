using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class GameRecord
    {
        public string GameId { get; set; }
        public string FullName { get; set; }
        public string ViDataPath { get; set; }
        public string EnDataPath { get; set; }
        public string MergedDataPath { get; set; }
    }

    public class GameDataRecord
    {
        public string GameId { get; set; }
        public string ItemId { get; set; }
        public string NameEn { get; set; }
        public string NameVi { get; set; }
        public string DescriptionEn { get; set; }
        public string DescriptionVi { get; set; }
    }

    public class PaginatedSearchResult
    {
        public int TotalCount { get; set; }
        public List<GameDataRecord> Items { get; set; }

        public PaginatedSearchResult()
        {
            Items = new List<GameDataRecord>();
        }
    }

    public class DatabaseService
    {
        private readonly string _projectRoot;
        private readonly string _dbPath;

        public DatabaseService(string projectRoot)
        {
            _projectRoot = projectRoot;
            _dbPath = Path.Combine(projectRoot, "bot_data.db");
        }

        // --- 1. GetResourceDir ---
        public string GetResourceDir()
        {
            return _projectRoot;
        }

        // --- 2. GetConnectionString & IsDatabaseAvailable ---
        private string GetConnectionString()
        {
            return string.Format("Driver={{SQLite3 ODBC Driver}};Database={0};", _dbPath);
        }

        public bool IsDatabaseAvailable()
        {
            return File.Exists(_dbPath);
        }

        // --- 3. HasWord (Bắt ranh giới từ Regex tiếng Việt) ---
        public static bool HasWord(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
                return false;

            try
            {
                string pattern = @"(?<!\w)" + Regex.Escape(query.ToLower()) + @"(?!\w)";
                return Regex.IsMatch(text.ToLower(), pattern);
            }
            catch
            {
                return text.ToLower().Contains(query.ToLower());
            }
        }

        // --- 4. CleanStringForExactMatch (Xóa ký tự zero-width ẩn) ---
        public static string CleanStringForExactMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\xa0', ' ');
            s = Regex.Replace(s, @"[\u200b-\u200f\ufeff]", "");
            return s.Trim().ToLower();
        }

        // --- 5. GetSubGameIds ---
        public List<string> GetSubGameIds(string gameId)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(gameId)) return list;
            string cleanId = gameId.Contains("_") ? gameId.Split('_')[0] : gameId;
            list.Add(cleanId);
            return list;
        }

        // --- 6. CreateTables ---
        public void CreateTables(OdbcConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS games (
                    game_id TEXT PRIMARY KEY, full_name TEXT NOT NULL UNIQUE, 
                    vi_data_path TEXT, en_data_path TEXT, merged_data_path TEXT)";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS game_data (
                    game_id TEXT NOT NULL, item_id TEXT NOT NULL, 
                    name_en TEXT, name_vi TEXT, description_en TEXT, description_vi TEXT, 
                    PRIMARY KEY (game_id, item_id))";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_game_data_name_en ON game_data(game_id, name_en)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_game_data_desc_en ON game_data(game_id, description_en)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_game_data_name_vi ON game_data(game_id, name_vi)";
                cmd.ExecuteNonQuery();
            }
        }

        // --- 7. PopulateGamesTable ---
        public int PopulateGamesTable(OdbcConnection conn)
        {
            string gamedataPath = Path.Combine(_projectRoot, "gamedata");
            var games = new List<GameRecord>
            {
                new GameRecord { GameId = "genshin", FullName = "Genshin Impact", MergedDataPath = Path.Combine(gamedataPath, "genshin.json") },
                new GameRecord { GameId = "hsr", FullName = "Honkai: Star Rail", MergedDataPath = Path.Combine(gamedataPath, "hsr.json") },
                new GameRecord { GameId = "zzz", FullName = "Zenless Zone Zero", MergedDataPath = Path.Combine(gamedataPath, "zzz.json") }
            };

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM games";
                cmd.ExecuteNonQuery();

                foreach (var g in games)
                {
                    cmd.CommandText = "INSERT INTO games (game_id, full_name, vi_data_path, en_data_path, merged_data_path) VALUES (?, ?, ?, ?, ?)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("?", g.GameId);
                    cmd.Parameters.AddWithValue("?", g.FullName);
                    cmd.Parameters.AddWithValue("?", (object)g.ViDataPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("?", (object)g.EnDataPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("?", (object)g.MergedDataPath ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
            return games.Count;
        }

        // --- 8. PopulateGameDataFromJson ---
        public int PopulateGameDataFromJson(OdbcConnection conn, Action<string, int> statusCallback = null, string targetGameId = null)
        {
            int count = 0;
            var games = new List<KeyValuePair<string, string>>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = !string.IsNullOrEmpty(targetGameId) 
                    ? "SELECT game_id, merged_data_path FROM games WHERE game_id = ?"
                    : "SELECT game_id, merged_data_path FROM games";

                if (!string.IsNullOrEmpty(targetGameId))
                {
                    cmd.Parameters.AddWithValue("?", targetGameId);
                }

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        games.Add(new KeyValuePair<string, string>(reader[0].ToString(), reader[1] != DBNull.Value ? reader[1].ToString() : ""));
                    }
                }
            }

            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            foreach (var g in games)
            {
                string gameId = g.Key;
                string path = g.Value;

                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                if (statusCallback != null) statusCallback(string.Format("Loading {0}...", gameId), 10);

                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.CommandText = "DELETE FROM game_data WHERE game_id = ?";
                    delCmd.Parameters.AddWithValue("?", gameId);
                    delCmd.ExecuteNonQuery();
                }

                string jsonContent = File.ReadAllText(path);
                var dict = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(jsonContent);
                if (dict == null) continue;

                foreach (var kvp in dict)
                {
                    string itemId = kvp.Key;
                    var val = kvp.Value;
                    string nameEn = val.ContainsKey("name_en") && val["name_en"] != null ? val["name_en"].ToString() : "";
                    string nameVi = val.ContainsKey("name_vi") && val["name_vi"] != null ? val["name_vi"].ToString() : "";
                    string descEn = val.ContainsKey("description_en") && val["description_en"] != null ? val["description_en"].ToString() : "";
                    string descVi = val.ContainsKey("description_vi") && val["description_vi"] != null ? val["description_vi"].ToString() : "";

                    using (var insCmd = conn.CreateCommand())
                    {
                        insCmd.CommandText = "INSERT OR REPLACE INTO game_data VALUES (?, ?, ?, ?, ?, ?)";
                        insCmd.Parameters.AddWithValue("?", gameId);
                        insCmd.Parameters.AddWithValue("?", itemId);
                        insCmd.Parameters.AddWithValue("?", nameEn);
                        insCmd.Parameters.AddWithValue("?", nameVi);
                        insCmd.Parameters.AddWithValue("?", descEn);
                        insCmd.Parameters.AddWithValue("?", descVi);
                        insCmd.ExecuteNonQuery();
                    }
                    count++;
                }
            }

            return count;
        }

        // --- 9. ImportSingleGame ---
        public string ImportSingleGame(string gameId, Action<string, int> statusCallback = null)
        {
            if (!IsDatabaseAvailable()) return "DB not available";
            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();
                    CreateTables(conn);
                    PopulateGamesTable(conn);
                    int c = PopulateGameDataFromJson(conn, statusCallback, gameId);
                    return string.Format("Imported {0} items for {1}.", c, gameId);
                }
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        // --- 10. InitDb ---
        public void InitDb(Action<string, int> statusCallback = null)
        {
            if (!IsDatabaseAvailable()) return;
            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();
                    CreateTables(conn);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM games";
                        object res = cmd.ExecuteScalar();
                        if (res != null && Convert.ToInt32(res) == 0)
                        {
                            PopulateGamesTable(conn);
                            PopulateGameDataFromJson(conn, statusCallback);
                        }
                    }
                }
            }
            catch { }
        }

        // --- 11. ReloadDbData ---
        public string ReloadDbData()
        {
            if (!IsDatabaseAvailable()) return "DB not available";
            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();
                    CreateTables(conn);

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM game_data; DELETE FROM games;";
                        cmd.ExecuteNonQuery();
                    }

                    int g = PopulateGamesTable(conn);
                    int d = PopulateGameDataFromJson(conn);
                    return string.Format("Reloaded {0} games, {1} items.", g, d);
                }
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        // --- 12. GetExactMatchGameData ---
        public GameDataRecord GetExactMatchGameData(string gameId, string term)
        {
            if (!IsDatabaseAvailable() || string.IsNullOrEmpty(term)) return null;

            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT game_id, item_id, name_en, name_vi, description_en, description_vi 
                            FROM game_data 
                            WHERE game_id = ? AND (name_en = ? OR name_vi = ?)
                            ORDER BY (name_en = name_vi) DESC, length(name_en) ASC
                            LIMIT 1";

                        cmd.Parameters.AddWithValue("?", gameId);
                        cmd.Parameters.AddWithValue("?", term);
                        cmd.Parameters.AddWithValue("?", term);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read()) return ReadRecord(reader);
                        }
                    }

                    string termClean = CleanStringForExactMatch(term);
                    using (var cmd2 = conn.CreateCommand())
                    {
                        cmd2.CommandText = @"
                            SELECT game_id, item_id, name_en, name_vi, description_en, description_vi 
                            FROM game_data 
                            WHERE game_id = ? AND (LOWER(name_en) = ? OR LOWER(name_vi) = ?)
                            LIMIT 1";

                        cmd2.Parameters.AddWithValue("?", gameId);
                        cmd2.Parameters.AddWithValue("?", termClean);
                        cmd2.Parameters.AddWithValue("?", termClean);

                        using (var reader2 = cmd2.ExecuteReader())
                        {
                            if (reader2.Read()) return ReadRecord(reader2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetExactMatch Error: " + ex.Message);
            }
            return null;
        }

        // --- 13. GetTranslationBySourceText ---
        public string GetTranslationBySourceText(string gameId, string sourceText, string direction = "en_to_vi")
        {
            if (!IsDatabaseAvailable() || string.IsNullOrEmpty(sourceText)) return null;

            string scName = (direction == "en_to_vi") ? "name_en" : "name_vi";
            string tcName = (direction == "en_to_vi") ? "name_vi" : "name_en";

            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Format("SELECT {0} FROM game_data WHERE game_id = ? AND {1} = ? LIMIT 1", tcName, scName);
                        cmd.Parameters.AddWithValue("?", gameId);
                        cmd.Parameters.AddWithValue("?", sourceText);

                        object res = cmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value && !string.IsNullOrEmpty(res.ToString()))
                            return res.ToString();
                    }

                    string scDesc = (direction == "en_to_vi") ? "description_en" : "description_vi";
                    string tcDesc = (direction == "en_to_vi") ? "description_vi" : "description_en";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = string.Format("SELECT {0} FROM game_data WHERE game_id = ? AND {1} = ? LIMIT 1", tcDesc, scDesc);
                        cmd.Parameters.AddWithValue("?", gameId);
                        cmd.Parameters.AddWithValue("?", sourceText);

                        object res = cmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value && !string.IsNullOrEmpty(res.ToString()))
                            return res.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        // --- 14. GetBulkTranslations ---
        public Dictionary<string, string> GetBulkTranslations(string gameId, List<string> terms, string direction = "en_to_vi")
        {
            var resultMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!IsDatabaseAvailable() || terms == null || terms.Count == 0) return resultMap;

            string scCol = (direction == "en_to_vi") ? "name_en" : "name_vi";
            string tcCol = (direction == "en_to_vi") ? "name_vi" : "name_en";

            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();

                    int chunkSize = 200;
                    for (int i = 0; i < terms.Count; i += chunkSize)
                    {
                        int count = Math.Min(chunkSize, terms.Count - i);
                        List<string> chunk = terms.GetRange(i, count);

                        var placeholders = new List<string>();
                        for (int j = 0; j < chunk.Count; j++) placeholders.Add("?");

                        string sql = string.Format(@"
                            SELECT {0}, {1} 
                            FROM game_data 
                            WHERE game_id = ? AND {0} IN ({2}) AND {1} IS NOT NULL AND {1} != ''",
                            scCol, tcCol, string.Join(",", placeholders.ToArray()));

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            cmd.Parameters.AddWithValue("?", gameId);
                            foreach (var term in chunk)
                            {
                                cmd.Parameters.AddWithValue("?", term);
                            }

                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string key = reader[0] != DBNull.Value ? reader[0].ToString() : "";
                                    string val = reader[1] != DBNull.Value ? reader[1].ToString() : "";
                                    if (!string.IsNullOrEmpty(key))
                                    {
                                        resultMap[key] = val;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetBulkTranslations Error: " + ex.Message);
            }

            return resultMap;
        }

        // --- 15. SearchGameData ---
        public List<GameDataRecord> SearchGameData(string gameId, string query)
        {
            var list = new List<GameDataRecord>();
            if (!IsDatabaseAvailable() || string.IsNullOrEmpty(query)) return list;

            string lq = "%" + query + "%";
            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT game_id, item_id, name_en, name_vi, description_en, description_vi 
                            FROM game_data 
                            WHERE game_id = ? AND (name_en LIKE ? OR name_vi LIKE ? OR description_en LIKE ? OR description_vi LIKE ?) 
                            LIMIT 30";

                        cmd.Parameters.AddWithValue("?", gameId);
                        cmd.Parameters.AddWithValue("?", lq);
                        cmd.Parameters.AddWithValue("?", lq);
                        cmd.Parameters.AddWithValue("?", lq);
                        cmd.Parameters.AddWithValue("?", lq);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(ReadRecord(reader));
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        // --- 16. SearchGameDataPaginated ---
        public PaginatedSearchResult SearchGameDataPaginated(string gameId, string query, string searchIn = "All", bool caseSensitive = false, int page = 1, int pageSize = 50)
        {
            var result = new PaginatedSearchResult();
            if (!IsDatabaseAvailable()) return result;

            int offset = (page - 1) * pageSize;
            string lq = "%" + (query != null ? query.Trim() : "") + "%";

            try
            {
                using (var conn = new OdbcConnection(GetConnectionString()))
                {
                    conn.Open();

                    string whereSql = "game_id = ?";
                    if (!string.IsNullOrEmpty(query))
                    {
                        if (searchIn == "ID")
                        {
                            whereSql += " AND item_id LIKE ?";
                        }
                        else if (searchIn == "En")
                        {
                            whereSql += " AND (name_en LIKE ? OR description_en LIKE ?)";
                        }
                        else if (searchIn == "Vi")
                        {
                            whereSql += " AND (name_vi LIKE ? OR description_vi LIKE ?)";
                        }
                        else
                        {
                            whereSql += " AND (item_id LIKE ? OR name_en LIKE ? OR name_vi LIKE ? OR description_en LIKE ? OR description_vi LIKE ?)";
                        }
                    }

                    using (var countCmd = conn.CreateCommand())
                    {
                        countCmd.CommandText = "SELECT COUNT(*) FROM game_data WHERE " + whereSql;
                        countCmd.Parameters.AddWithValue("?", gameId);
                        if (!string.IsNullOrEmpty(query))
                        {
                            if (searchIn == "ID") countCmd.Parameters.AddWithValue("?", lq);
                            else if (searchIn == "En" || searchIn == "Vi")
                            {
                                countCmd.Parameters.AddWithValue("?", lq);
                                countCmd.Parameters.AddWithValue("?", lq);
                            }
                            else
                            {
                                for (int k = 0; k < 5; k++) countCmd.Parameters.AddWithValue("?", lq);
                            }
                        }

                        object countObj = countCmd.ExecuteScalar();
                        if (countObj != null && countObj != DBNull.Value)
                        {
                            result.TotalCount = Convert.ToInt32(countObj);
                        }
                    }

                    using (var selectCmd = conn.CreateCommand())
                    {
                        selectCmd.CommandText = string.Format(@"
                            SELECT game_id, item_id, name_en, name_vi, description_en, description_vi 
                            FROM game_data 
                            WHERE {0} 
                            LIMIT {1} OFFSET {2}", whereSql, pageSize, offset);

                        selectCmd.Parameters.AddWithValue("?", gameId);
                        if (!string.IsNullOrEmpty(query))
                        {
                            if (searchIn == "ID") selectCmd.Parameters.AddWithValue("?", lq);
                            else if (searchIn == "En" || searchIn == "Vi")
                            {
                                selectCmd.Parameters.AddWithValue("?", lq);
                                selectCmd.Parameters.AddWithValue("?", lq);
                            }
                            else
                            {
                                for (int k = 0; k < 5; k++) selectCmd.Parameters.AddWithValue("?", lq);
                            }
                        }

                        using (var reader = selectCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var rec = ReadRecord(reader);
                                if (string.IsNullOrEmpty(query) || HasWord(rec.NameEn, query) || HasWord(rec.NameVi, query) || HasWord(rec.ItemId, query))
                                {
                                    result.Items.Add(rec);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SearchGameDataPaginated Error: " + ex.Message);
            }

            return result;
        }

        private GameDataRecord ReadRecord(IDataReader reader)
        {
            return new GameDataRecord
            {
                GameId = reader["game_id"] != DBNull.Value ? reader["game_id"].ToString() : "",
                ItemId = reader["item_id"] != DBNull.Value ? reader["item_id"].ToString() : "",
                NameEn = reader["name_en"] != DBNull.Value ? reader["name_en"].ToString() : "",
                NameVi = reader["name_vi"] != DBNull.Value ? reader["name_vi"].ToString() : "",
                DescriptionEn = reader["description_en"] != DBNull.Value ? reader["description_en"].ToString() : "",
                DescriptionVi = reader["description_vi"] != DBNull.Value ? reader["description_vi"].ToString() : ""
            };
        }
    }
}
