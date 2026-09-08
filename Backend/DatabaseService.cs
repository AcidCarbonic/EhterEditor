using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>
    /// Pure Native C# SQLite Database Service using Windows built-in winsqlite3.dll
    /// 100% Native, 0% Python dependency, 0 external ODBC drivers required.
    /// </summary>
    public class DatabaseService
    {
        #region Native SQLite P/Invoke (winsqlite3.dll built into Windows 10/11)
        private const string SQLITE_DLL = "winsqlite3.dll";

        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private const int SQLITE_OPEN_READONLY = 0x00000001;
        private const int SQLITE_OPEN_READWRITE = 0x00000002;
        private const int SQLITE_OPEN_CREATE = 0x00000004;
        private const int SQLITE_UTF8 = 1;

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_column_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr stmt, int iCol);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_column_text", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_bind_text", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int iCol, byte[] zData, int nData, IntPtr xDel);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_bind_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int(IntPtr stmt, int iCol, int iValue);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SQLiteCallback(IntPtr context, int argc, IntPtr argv);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_create_function", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_create_function(
            IntPtr db,
            byte[] zFunctionName,
            int nArg,
            int eTextRep,
            IntPtr pApp,
            SQLiteCallback xFunc,
            IntPtr xStep,
            IntPtr xFinal
        );

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_value_text", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_value_text(IntPtr val);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_result_int", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_result_int(IntPtr ctx, int val);

        [DllImport(SQLITE_DLL, EntryPoint = "sqlite3_result_text", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_result_text(IntPtr ctx, byte[] val, int nVal, IntPtr xDel);

        private static string PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }

        private static byte[] Utf8NullTerminated(string s)
        {
            if (s == null) s = "";
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        private static readonly SQLiteCallback HasWordDelegate = HasWordCallback;
        private static readonly SQLiteCallback CleanExactDelegate = CleanExactCallback;

        private static void HasWordCallback(IntPtr context, int argc, IntPtr argv)
        {
            if (argc < 2) { sqlite3_result_int(context, 0); return; }
            IntPtr val0 = Marshal.ReadIntPtr(argv, 0);
            IntPtr val1 = Marshal.ReadIntPtr(argv, IntPtr.Size);
            string text = PtrToStringUtf8(sqlite3_value_text(val0));
            string query = PtrToStringUtf8(sqlite3_value_text(val1));

            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            {
                sqlite3_result_int(context, 0);
                return;
            }

            try
            {
                // Word boundary matching across Unicode (Vietnamese accents, Japanese, CJK, etc.)
                string pattern = @"(?<![\w\p{L}\p{N}])" + Regex.Escape(query.ToLower()) + @"(?![\w\p{L}\p{N}])";
                bool match = Regex.IsMatch(text.ToLower(), pattern);
                sqlite3_result_int(context, match ? 1 : 0);
            }
            catch
            {
                sqlite3_result_int(context, text.ToLower().Contains(query.ToLower()) ? 1 : 0);
            }
        }

        private static void CleanExactCallback(IntPtr context, int argc, IntPtr argv)
        {
            if (argc < 1) { sqlite3_result_text(context, Utf8NullTerminated(""), 0, IntPtr.Zero); return; }
            IntPtr val0 = Marshal.ReadIntPtr(argv, 0);
            string text = PtrToStringUtf8(sqlite3_value_text(val0));
            string cleaned = CleanStringForExactMatch(text);
            byte[] bytes = Encoding.UTF8.GetBytes(cleaned);
            sqlite3_result_text(context, bytes, bytes.Length, IntPtr.Zero);
        }
        #endregion

        private readonly string _projectRoot;
        private readonly string _dbPath;
        private readonly object _dbLock = new object();

        private static string GetAppBaseDirectory()
        {
            try
            {
                string loc = typeof(DatabaseService).Assembly.Location;
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
                    File.Exists(Path.Combine(dir, "db", "bot_data.db")) ||
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

        public DatabaseService(string projectRoot)
        {
            _projectRoot = ResolveProjectRoot(projectRoot);
            
            // Confine database search strictly within experimental_csharp
            string appDir = GetAppBaseDirectory();
            var candidates = new string[]
            {
                Path.Combine(_projectRoot, "db", "bot_data.db"),
                Path.Combine(_projectRoot, "bot_data.db"),
                Path.Combine(appDir, "db", "bot_data.db"),
                Path.Combine(appDir, "bot_data.db")
            };

            string bestPath = null;
            long maxLen = -1;
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    try
                    {
                        var fi = new FileInfo(c);
                        if (fi.Length > maxLen)
                        {
                            maxLen = fi.Length;
                            bestPath = fi.FullName;
                        }
                    }
                    catch { }
                }
            }

            _dbPath = bestPath ?? Path.Combine(_projectRoot, "db", "bot_data.db");
        }

        public string GetResourceDir()
        {
            return _projectRoot;
        }

        public bool IsDatabaseAvailable()
        {
            return File.Exists(_dbPath);
        }

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

        public static string CleanStringForExactMatch(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\xa0', ' ');
            s = Regex.Replace(s, @"[\u200b-\u200f\ufeff]", "");
            return s.Trim().ToLower();
        }

        public List<string> GetSubGameIds(string gameId)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(gameId)) return list;
            string cleanId = gameId.Contains("_") ? gameId.Split('_')[0] : gameId;
            list.Add(cleanId);
            return list;
        }

        private IntPtr _readDb = IntPtr.Zero;

        private IntPtr GetOrCreateReadConnection()
        {
            if (_readDb != IntPtr.Zero) return _readDb;
            _readDb = OpenDbConnection(true);
            return _readDb;
        }

        private void CloseReadConnection()
        {
            if (_readDb != IntPtr.Zero)
            {
                sqlite3_close(_readDb);
                _readDb = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            lock (_dbLock)
            {
                CloseReadConnection();
            }
        }

        private IntPtr OpenDbConnection(bool readOnly = true)
        {
            if (!File.Exists(_dbPath) && readOnly) return IntPtr.Zero;

            IntPtr db;
            int flags = readOnly ? SQLITE_OPEN_READONLY : (SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE);
            int rc = sqlite3_open_v2(Utf8NullTerminated(_dbPath), out db, flags, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                if (db != IntPtr.Zero) sqlite3_close(db);
                return IntPtr.Zero;
            }

            // Apply high-performance SQLite PRAGMAs (RAM cache, Memory mapped I/O)
            IntPtr err;
            sqlite3_exec(db, Utf8NullTerminated("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA mmap_size=300000000; PRAGMA cache_size=-64000; PRAGMA temp_store=MEMORY;"), IntPtr.Zero, IntPtr.Zero, out err);

            // Register custom C# functions: has_word, clean_exact
            sqlite3_create_function(db, Utf8NullTerminated("has_word"), 2, SQLITE_UTF8, IntPtr.Zero, HasWordDelegate, IntPtr.Zero, IntPtr.Zero);
            sqlite3_create_function(db, Utf8NullTerminated("clean_exact"), 1, SQLITE_UTF8, IntPtr.Zero, CleanExactDelegate, IntPtr.Zero, IntPtr.Zero);

            return db;
        }

        // --- 1. GetExactMatchGameData ---
        public GameDataRecord GetExactMatchGameData(string gameId, string term)
        {
            if (!IsDatabaseAvailable() || string.IsNullOrEmpty(term)) return null;

            lock (_dbLock)
            {
                IntPtr db = GetOrCreateReadConnection();
                if (db == IntPtr.Zero) return null;

                try
                {
                    var subIds = GetSubGameIds(gameId);
                    if (subIds.Count == 0) subIds.Add("hsr");

                    string placeholders = string.Join(",", new string[subIds.Count]);
                    placeholders = placeholders.Replace("", "?").TrimEnd(',');

                    // 1. FAST-PATH (Index-optimized)
                    string fastSql = string.Format(@"
                        SELECT item_id, name_en, name_vi, description_en, description_vi 
                        FROM game_data 
                        WHERE game_id IN ({0}) 
                          AND (name_en = ? OR name_vi = ?)
                        ORDER BY 
                          (name_en = name_vi) DESC,
                          (game_id LIKE '%_base') DESC,
                          length(name_en) ASC
                        LIMIT 1", placeholders);

                    IntPtr stmt;
                    byte[] sqlBytes = Utf8NullTerminated(fastSql);
                    if (sqlite3_prepare_v2(db, sqlBytes, -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        int bindIndex = 1;
                        foreach (var sid in subIds)
                        {
                            byte[] b = Encoding.UTF8.GetBytes(sid);
                            sqlite3_bind_text(stmt, bindIndex++, b, b.Length, IntPtr.Zero);
                        }
                        byte[] termBytes = Encoding.UTF8.GetBytes(term);
                        sqlite3_bind_text(stmt, bindIndex++, termBytes, termBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, bindIndex++, termBytes, termBytes.Length, IntPtr.Zero);

                        if (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            var rec = new GameDataRecord
                            {
                                GameId = gameId,
                                ItemId = PtrToStringUtf8(sqlite3_column_text(stmt, 0)),
                                NameEn = PtrToStringUtf8(sqlite3_column_text(stmt, 1)),
                                NameVi = PtrToStringUtf8(sqlite3_column_text(stmt, 2)),
                                DescriptionEn = PtrToStringUtf8(sqlite3_column_text(stmt, 3)),
                                DescriptionVi = PtrToStringUtf8(sqlite3_column_text(stmt, 4))
                            };
                            sqlite3_finalize(stmt);
                            return rec;
                        }
                        sqlite3_finalize(stmt);
                    }

                    // 2. SLOW-PATH FALLBACK (clean_exact)
                    string termClean = CleanStringForExactMatch(term);
                    string slowSql = string.Format(@"
                        SELECT item_id, name_en, name_vi, description_en, description_vi 
                        FROM game_data 
                        WHERE game_id IN ({0}) 
                          AND (clean_exact(name_en) = ? OR clean_exact(name_vi) = ?)
                        ORDER BY 
                          (clean_exact(name_en) = clean_exact(name_vi)) DESC,
                          (game_id LIKE '%_base') DESC,
                          length(name_en) ASC
                        LIMIT 1", placeholders);

                    if (sqlite3_prepare_v2(db, Utf8NullTerminated(slowSql), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        int bindIndex = 1;
                        foreach (var sid in subIds)
                        {
                            byte[] b = Encoding.UTF8.GetBytes(sid);
                            sqlite3_bind_text(stmt, bindIndex++, b, b.Length, IntPtr.Zero);
                        }
                        byte[] termBytes = Encoding.UTF8.GetBytes(termClean);
                        sqlite3_bind_text(stmt, bindIndex++, termBytes, termBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, bindIndex++, termBytes, termBytes.Length, IntPtr.Zero);

                        if (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            var rec = new GameDataRecord
                            {
                                GameId = gameId,
                                ItemId = PtrToStringUtf8(sqlite3_column_text(stmt, 0)),
                                NameEn = PtrToStringUtf8(sqlite3_column_text(stmt, 1)),
                                NameVi = PtrToStringUtf8(sqlite3_column_text(stmt, 2)),
                                DescriptionEn = PtrToStringUtf8(sqlite3_column_text(stmt, 3)),
                                DescriptionVi = PtrToStringUtf8(sqlite3_column_text(stmt, 4))
                            };
                            sqlite3_finalize(stmt);
                            return rec;
                        }
                        sqlite3_finalize(stmt);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error in GetExactMatchGameData: " + ex.Message);
                }
                finally
                {
                    // No sqlite3_close(db) here, we maintain persistent read connection
                }
            }
            return null;
        }

        // --- 2. GetTranslationBySourceText ---
        public string GetTranslationBySourceText(string gameId, string sourceText, string direction = "en_to_vi")
        {
            var match = GetExactMatchGameData(gameId, sourceText);
            if (match != null)
            {
                if (direction == "en_to_vi" && !string.IsNullOrEmpty(match.NameVi)) return match.NameVi;
                if (direction != "en_to_vi" && !string.IsNullOrEmpty(match.NameEn)) return match.NameEn;
            }
            return null;
        }

        // --- 3. GetBulkTranslations ---
        public Dictionary<string, string> GetBulkTranslations(string gameId, List<string> terms, string direction = "en_to_vi")
        {
            var resultMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!IsDatabaseAvailable() || terms == null || terms.Count == 0) return resultMap;

            string scCol = (direction == "en_to_vi") ? "name_en" : "name_vi";
            string tcCol = (direction == "en_to_vi") ? "name_vi" : "name_en";

            lock (_dbLock)
            {
                IntPtr db = GetOrCreateReadConnection();
                if (db == IntPtr.Zero) return resultMap;

                try
                {
                    var subIds = GetSubGameIds(gameId);
                    int chunkSize = 200;
                    for (int i = 0; i < terms.Count; i += chunkSize)
                    {
                        var chunk = terms.GetRange(i, Math.Min(chunkSize, terms.Count - i));
                        var subPlaceholders = string.Join(",", new string[subIds.Count]).Replace("", "?").TrimEnd(',');
                        var chunkPlaceholders = string.Join(",", new string[chunk.Count]).Replace("", "?").TrimEnd(',');

                        string query = string.Format(@"
                            SELECT {0}, {1} 
                            FROM game_data 
                            WHERE game_id IN ({2}) 
                              AND {0} IN ({3}) 
                              AND {1} IS NOT NULL 
                              AND {1} != ''",
                            scCol, tcCol, subPlaceholders, chunkPlaceholders);

                        IntPtr stmt;
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated(query), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            int bindIndex = 1;
                            foreach (var sid in subIds)
                            {
                                byte[] b = Encoding.UTF8.GetBytes(sid);
                                sqlite3_bind_text(stmt, bindIndex++, b, b.Length, IntPtr.Zero);
                            }
                            foreach (var term in chunk)
                            {
                                byte[] b = Encoding.UTF8.GetBytes(term);
                                sqlite3_bind_text(stmt, bindIndex++, b, b.Length, IntPtr.Zero);
                            }

                            while (sqlite3_step(stmt) == SQLITE_ROW)
                            {
                                string key = PtrToStringUtf8(sqlite3_column_text(stmt, 0));
                                string val = PtrToStringUtf8(sqlite3_column_text(stmt, 1));
                                if (!string.IsNullOrEmpty(key))
                                {
                                    resultMap[key] = val;
                                }
                            }
                            sqlite3_finalize(stmt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Bulk Query Error: " + ex.Message);
                }
            }

            return resultMap;
        }

        // --- 4. SearchGameData (Quick lookup limit 30) ---
        public List<GameDataRecord> SearchGameData(string gameId, string query)
        {
            var list = new List<GameDataRecord>();
            if (!IsDatabaseAvailable() || string.IsNullOrEmpty(query)) return list;

            lock (_dbLock)
            {
                IntPtr db = GetOrCreateReadConnection();
                if (db == IntPtr.Zero) return list;

                try
                {
                    string lq = "%" + query.Trim() + "%";
                    string qClean = query.Trim();
                    string sql = @"
                        SELECT item_id, name_en, name_vi, description_en, description_vi 
                        FROM game_data 
                        WHERE game_id = ? AND (item_id LIKE ? OR (name_en LIKE ? AND has_word(name_en, ?)) OR (name_vi LIKE ? AND has_word(name_vi, ?))) 
                        LIMIT 30";

                    IntPtr stmt;
                    if (sqlite3_prepare_v2(db, Utf8NullTerminated(sql), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        byte[] gBytes = Encoding.UTF8.GetBytes(gameId.ToLower());
                        byte[] qLikeBytes = Encoding.UTF8.GetBytes(lq);
                        byte[] qExactBytes = Encoding.UTF8.GetBytes(qClean);

                        sqlite3_bind_text(stmt, 1, gBytes, gBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, 2, qLikeBytes, qLikeBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, 3, qLikeBytes, qLikeBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, 4, qExactBytes, qExactBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, 5, qLikeBytes, qLikeBytes.Length, IntPtr.Zero);
                        sqlite3_bind_text(stmt, 6, qExactBytes, qExactBytes.Length, IntPtr.Zero);

                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            list.Add(new GameDataRecord
                            {
                                GameId = gameId,
                                ItemId = PtrToStringUtf8(sqlite3_column_text(stmt, 0)),
                                NameEn = PtrToStringUtf8(sqlite3_column_text(stmt, 1)),
                                NameVi = PtrToStringUtf8(sqlite3_column_text(stmt, 2)),
                                DescriptionEn = PtrToStringUtf8(sqlite3_column_text(stmt, 3)),
                                DescriptionVi = PtrToStringUtf8(sqlite3_column_text(stmt, 4))
                            });
                        }
                        sqlite3_finalize(stmt);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error in SearchGameData: " + ex.Message);
                }
            }
            return list;
        }

        // --- 5. SearchGameDataPaginated (Fast native LIKE search + exact match ranking) ---
        public PaginatedSearchResult SearchGameDataPaginated(string gameId, string query, string searchIn = "All", bool caseSensitive = false, int page = 1, int pageSize = 20)
        {
            var result = new PaginatedSearchResult();
            if (!IsDatabaseAvailable()) return result;

            lock (_dbLock)
            {
                IntPtr db = GetOrCreateReadConnection();
                if (db == IntPtr.Zero) return result;

                try
                {
                    int offset = Math.Max(0, (page - 1) * pageSize);
                    string q = (query != null ? query.Trim() : "");
                    string searchPattern = caseSensitive ? q : q.ToLower();
                    string likePattern = "%" + searchPattern + "%";

                    bool isAllGames = string.IsNullOrEmpty(gameId) || gameId.Equals("all", StringComparison.OrdinalIgnoreCase) || gameId.StartsWith("Tất cả", StringComparison.OrdinalIgnoreCase);

                    var subIds = isAllGames ? new List<string>() : GetSubGameIds(gameId);

                    var whereClauses = new List<string>();
                    var boundStrings = new List<string>();

                    if (!isAllGames && subIds.Count > 0)
                    {
                        var ph = new List<string>();
                        for (int i = 0; i < subIds.Count; i++) ph.Add("?");
                        whereClauses.Add(string.Format("game_id IN ({0})", string.Join(",", ph.ToArray())));
                        boundStrings.AddRange(subIds);
                    }

                    if (!string.IsNullOrEmpty(q))
                    {
                        if (searchIn.Equals("ID", StringComparison.OrdinalIgnoreCase) || searchIn.IndexOf("ID", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            whereClauses.Add("item_id LIKE ?");
                            boundStrings.Add(likePattern);
                        }
                        else if (searchIn.IndexOf("Anh", StringComparison.OrdinalIgnoreCase) >= 0 || searchIn.Equals("En", StringComparison.OrdinalIgnoreCase))
                        {
                            whereClauses.Add("(name_en LIKE ? AND has_word(name_en, ?))");
                            boundStrings.Add(likePattern);
                            boundStrings.Add(searchPattern);
                        }
                        else if (searchIn.IndexOf("Việt", StringComparison.OrdinalIgnoreCase) >= 0 || searchIn.Equals("Vi", StringComparison.OrdinalIgnoreCase))
                        {
                            whereClauses.Add("(name_vi LIKE ? AND has_word(name_vi, ?))");
                            boundStrings.Add(likePattern);
                            boundStrings.Add(searchPattern);
                        }
                        else // Tất cả (Ưu tiên quét Tên / ID tốc độ cao có bắt ranh giới từ has_word)
                        {
                            whereClauses.Add("(item_id LIKE ? OR (name_en LIKE ? AND has_word(name_en, ?)) OR (name_vi LIKE ? AND has_word(name_vi, ?)))");
                            boundStrings.Add(likePattern);
                            boundStrings.Add(likePattern);
                            boundStrings.Add(searchPattern);
                            boundStrings.Add(likePattern);
                            boundStrings.Add(searchPattern);
                        }
                    }

                    string whereSql = whereClauses.Count > 0 ? string.Join(" AND ", whereClauses.ToArray()) : "1=1";

                    // 1. SELECT PAGINATED FIRST (Fast early exit without slow unindexed sort)
                    string selectSql = string.Format(@"
                        SELECT item_id, name_en, name_vi, description_en, description_vi, game_id 
                        FROM game_data 
                        WHERE {0} 
                        LIMIT ? OFFSET ?", whereSql);

                    IntPtr selectStmt;
                    if (sqlite3_prepare_v2(db, Utf8NullTerminated(selectSql), -1, out selectStmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        int bindIndex = 1;
                        for (int i = 0; i < boundStrings.Count; i++)
                        {
                            byte[] b = Encoding.UTF8.GetBytes(boundStrings[i]);
                            sqlite3_bind_text(selectStmt, bindIndex++, b, b.Length, IntPtr.Zero);
                        }

                        // LIMIT & OFFSET
                        sqlite3_bind_int(selectStmt, bindIndex++, pageSize);
                        sqlite3_bind_int(selectStmt, bindIndex++, offset);

                        while (sqlite3_step(selectStmt) == SQLITE_ROW)
                        {
                            result.Items.Add(new GameDataRecord
                            {
                                ItemId = PtrToStringUtf8(sqlite3_column_text(selectStmt, 0)),
                                NameEn = PtrToStringUtf8(sqlite3_column_text(selectStmt, 1)),
                                NameVi = PtrToStringUtf8(sqlite3_column_text(selectStmt, 2)),
                                DescriptionEn = PtrToStringUtf8(sqlite3_column_text(selectStmt, 3)),
                                DescriptionVi = PtrToStringUtf8(sqlite3_column_text(selectStmt, 4)),
                                GameId = PtrToStringUtf8(sqlite3_column_text(selectStmt, 5))
                            });
                        }
                        sqlite3_finalize(selectStmt);
                    }

                    // 2. COUNT (If page 1 and items < pageSize, count is simply items.Count - avoids heavy 2nd scan)
                    if (page == 1 && result.Items.Count < pageSize)
                    {
                        result.TotalCount = result.Items.Count;
                    }
                    else
                    {
                        string countSql = "SELECT COUNT(*) FROM game_data WHERE " + whereSql;
                        IntPtr countStmt;
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated(countSql), -1, out countStmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            for (int i = 0; i < boundStrings.Count; i++)
                            {
                                byte[] b = Encoding.UTF8.GetBytes(boundStrings[i]);
                                sqlite3_bind_text(countStmt, i + 1, b, b.Length, IntPtr.Zero);
                            }

                            if (sqlite3_step(countStmt) == SQLITE_ROW)
                            {
                                result.TotalCount = sqlite3_column_int(countStmt, 0);
                            }
                            sqlite3_finalize(countStmt);
                        }
                        else
                        {
                            result.TotalCount = result.Items.Count;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("SQL Paginated Search Error: " + ex.Message);
                }
            }

            return result;
        }

        // --- 6. Table Maintenance & Game Queries ---
        public List<GameRecord> GetInstalledGames()
        {
            var list = new List<GameRecord>();
            if (!IsDatabaseAvailable()) return list;

            lock (_dbLock)
            {
                IntPtr db = GetOrCreateReadConnection();
                if (db == IntPtr.Zero) return list;

                try
                {
                    // Query distinct games that actually have records in game_data
                    string sql = @"
                        SELECT DISTINCT g.game_id, g.full_name 
                        FROM games g 
                        INNER JOIN game_data d ON g.game_id = d.game_id";

                    IntPtr stmt;
                    if (sqlite3_prepare_v2(db, Utf8NullTerminated(sql), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            list.Add(new GameRecord
                            {
                                GameId = PtrToStringUtf8(sqlite3_column_text(stmt, 0)),
                                FullName = PtrToStringUtf8(sqlite3_column_text(stmt, 1))
                            });
                        }
                        sqlite3_finalize(stmt);
                    }

                    // Fallback if games table mapping is empty: query directly from game_data
                    if (list.Count == 0)
                    {
                        string rawSql = "SELECT DISTINCT game_id FROM game_data";
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated(rawSql), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            while (sqlite3_step(stmt) == SQLITE_ROW)
                            {
                                string gid = PtrToStringUtf8(sqlite3_column_text(stmt, 0));
                                string fname = gid.ToUpper();
                                if (gid.Equals("hsr", StringComparison.OrdinalIgnoreCase)) fname = "Honkai: Star Rail";
                                else if (gid.Equals("genshin", StringComparison.OrdinalIgnoreCase)) fname = "Genshin Impact";
                                else if (gid.Equals("zzz", StringComparison.OrdinalIgnoreCase)) fname = "Zenless Zone Zero";

                                list.Add(new GameRecord { GameId = gid, FullName = fname });
                            }
                            sqlite3_finalize(stmt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error in GetInstalledGames: " + ex.Message);
                }
            }
            return list;
        }

        public void CreateTables()
        {
            lock (_dbLock)
            {
                IntPtr db = OpenDbConnection(false);
                if (db == IntPtr.Zero) return;
                try
                {
                    string sql = @"
                        CREATE TABLE IF NOT EXISTS games (
                            game_id TEXT PRIMARY KEY, full_name TEXT NOT NULL UNIQUE, 
                            vi_data_path TEXT, en_data_path TEXT, merged_data_path TEXT);
                        CREATE TABLE IF NOT EXISTS game_data (
                            game_id TEXT NOT NULL, item_id TEXT NOT NULL, 
                            name_en TEXT, name_vi TEXT, description_en TEXT, description_vi TEXT, 
                            PRIMARY KEY (game_id, item_id));
                        CREATE INDEX IF NOT EXISTS idx_game_data_name_en ON game_data(game_id, name_en);
                        CREATE INDEX IF NOT EXISTS idx_game_data_desc_en ON game_data(game_id, description_en);
                        CREATE INDEX IF NOT EXISTS idx_game_data_name_vi ON game_data(game_id, name_vi);";
                    IntPtr err;
                    sqlite3_exec(db, Utf8NullTerminated(sql), IntPtr.Zero, IntPtr.Zero, out err);
                }
                finally
                {
                    sqlite3_close(db);
                }
            }
        }

        public int PopulateGamesTable()
        {
            string gamedataPath = Path.Combine(_projectRoot, "gamedata");
            var games = new List<GameRecord>
            {
                new GameRecord { GameId = "genshin", FullName = "Genshin Impact", MergedDataPath = Path.Combine(gamedataPath, "genshin.json") },
                new GameRecord { GameId = "hsr", FullName = "Honkai: Star Rail", MergedDataPath = Path.Combine(gamedataPath, "hsr.json") },
                new GameRecord { GameId = "zzz", FullName = "Zenless Zone Zero", MergedDataPath = Path.Combine(gamedataPath, "zzz.json") }
            };

            lock (_dbLock)
            {
                IntPtr db = OpenDbConnection(false);
                if (db == IntPtr.Zero) return 0;
                try
                {
                    IntPtr err;
                    sqlite3_exec(db, Utf8NullTerminated("DELETE FROM games;"), IntPtr.Zero, IntPtr.Zero, out err);

                    foreach (var g in games)
                    {
                        IntPtr stmt;
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated("INSERT INTO games VALUES (?, ?, ?, ?, ?)"), -1, out stmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            byte[] gId = Encoding.UTF8.GetBytes(g.GameId);
                            byte[] gName = Encoding.UTF8.GetBytes(g.FullName);
                            byte[] gMerged = Encoding.UTF8.GetBytes(g.MergedDataPath ?? "");

                            sqlite3_bind_text(stmt, 1, gId, gId.Length, IntPtr.Zero);
                            sqlite3_bind_text(stmt, 2, gName, gName.Length, IntPtr.Zero);
                            sqlite3_bind_text(stmt, 3, Utf8NullTerminated(""), 0, IntPtr.Zero);
                            sqlite3_bind_text(stmt, 4, Utf8NullTerminated(""), 0, IntPtr.Zero);
                            sqlite3_bind_text(stmt, 5, gMerged, gMerged.Length, IntPtr.Zero);

                            sqlite3_step(stmt);
                            sqlite3_finalize(stmt);
                        }
                    }
                }
                finally
                {
                    sqlite3_close(db);
                }
            }
            return games.Count;
        }

        public int PopulateGameDataFromJson(Action<string, int> statusCallback = null, string targetGameId = null)
        {
            int count = 0;
            var games = new List<KeyValuePair<string, string>>();

            lock (_dbLock)
            {
                IntPtr db = OpenDbConnection(false);
                if (db == IntPtr.Zero) return 0;
                try
                {
                    string q = !string.IsNullOrEmpty(targetGameId)
                        ? "SELECT game_id, merged_data_path FROM games WHERE game_id = ?"
                        : "SELECT game_id, merged_data_path FROM games";

                    IntPtr qStmt;
                    if (sqlite3_prepare_v2(db, Utf8NullTerminated(q), -1, out qStmt, IntPtr.Zero) == SQLITE_OK)
                    {
                        if (!string.IsNullOrEmpty(targetGameId))
                        {
                            byte[] tBytes = Encoding.UTF8.GetBytes(targetGameId);
                            sqlite3_bind_text(qStmt, 1, tBytes, tBytes.Length, IntPtr.Zero);
                        }

                        while (sqlite3_step(qStmt) == SQLITE_ROW)
                        {
                            string gid = PtrToStringUtf8(sqlite3_column_text(qStmt, 0));
                            string path = PtrToStringUtf8(sqlite3_column_text(qStmt, 1));
                            games.Add(new KeyValuePair<string, string>(gid, path));
                        }
                        sqlite3_finalize(qStmt);
                    }

                    var serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = int.MaxValue;

                    foreach (var g in games)
                    {
                        string gameId = g.Key;
                        string path = g.Value;

                        if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                        if (statusCallback != null) statusCallback(string.Format("Loading {0}...", gameId), 10);

                        IntPtr delStmt;
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated("DELETE FROM game_data WHERE game_id = ?"), -1, out delStmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            byte[] gBytes = Encoding.UTF8.GetBytes(gameId);
                            sqlite3_bind_text(delStmt, 1, gBytes, gBytes.Length, IntPtr.Zero);
                            sqlite3_step(delStmt);
                            sqlite3_finalize(delStmt);
                        }

                        string jsonContent = File.ReadAllText(path, Encoding.UTF8);
                        var dict = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(jsonContent);
                        if (dict == null) continue;

                        IntPtr err;
                        sqlite3_exec(db, Utf8NullTerminated("BEGIN TRANSACTION;"), IntPtr.Zero, IntPtr.Zero, out err);

                        IntPtr insStmt;
                        if (sqlite3_prepare_v2(db, Utf8NullTerminated("INSERT OR REPLACE INTO game_data VALUES (?, ?, ?, ?, ?, ?)"), -1, out insStmt, IntPtr.Zero) == SQLITE_OK)
                        {
                            foreach (var kvp in dict)
                            {
                                string itemId = kvp.Key;
                                var val = kvp.Value;
                                string nameEn = val.ContainsKey("name_en") && val["name_en"] != null ? val["name_en"].ToString() : "";
                                string nameVi = val.ContainsKey("name_vi") && val["name_vi"] != null ? val["name_vi"].ToString() : "";
                                string descEn = val.ContainsKey("description_en") && val["description_en"] != null ? val["description_en"].ToString() : "";
                                string descVi = val.ContainsKey("description_vi") && val["description_vi"] != null ? val["description_vi"].ToString() : "";

                                byte[] gBytes = Encoding.UTF8.GetBytes(gameId);
                                byte[] idBytes = Encoding.UTF8.GetBytes(itemId);
                                byte[] neBytes = Encoding.UTF8.GetBytes(nameEn);
                                byte[] nvBytes = Encoding.UTF8.GetBytes(nameVi);
                                byte[] deBytes = Encoding.UTF8.GetBytes(descEn);
                                byte[] dvBytes = Encoding.UTF8.GetBytes(descVi);

                                sqlite3_bind_text(insStmt, 1, gBytes, gBytes.Length, IntPtr.Zero);
                                sqlite3_bind_text(insStmt, 2, idBytes, idBytes.Length, IntPtr.Zero);
                                sqlite3_bind_text(insStmt, 3, neBytes, neBytes.Length, IntPtr.Zero);
                                sqlite3_bind_text(insStmt, 4, nvBytes, nvBytes.Length, IntPtr.Zero);
                                sqlite3_bind_text(insStmt, 5, deBytes, deBytes.Length, IntPtr.Zero);
                                sqlite3_bind_text(insStmt, 6, dvBytes, dvBytes.Length, IntPtr.Zero);

                                sqlite3_step(insStmt);
                                count++;
                            }
                            sqlite3_finalize(insStmt);
                        }

                        sqlite3_exec(db, Utf8NullTerminated("COMMIT;"), IntPtr.Zero, IntPtr.Zero, out err);
                    }
                }
                finally
                {
                    sqlite3_close(db);
                }
            }
            return count;
        }

        public string ImportSingleGame(string gameId, Action<string, int> statusCallback = null)
        {
            try
            {
                CreateTables();
                PopulateGamesTable();
                int c = PopulateGameDataFromJson(statusCallback, gameId);
                return string.Format("Imported {0} items for {1}.", c, gameId);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        public string ReloadDbData()
        {
            try
            {
                CreateTables();
                lock (_dbLock)
                {
                    IntPtr db = OpenDbConnection(false);
                    if (db != IntPtr.Zero)
                    {
                        IntPtr err;
                        sqlite3_exec(db, Utf8NullTerminated("DELETE FROM game_data; DELETE FROM games;"), IntPtr.Zero, IntPtr.Zero, out err);
                        sqlite3_close(db);
                    }
                }
                int g = PopulateGamesTable();
                int d = PopulateGameDataFromJson();
                return string.Format("Reloaded {0} games, {1} items.", g, d);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
