using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class RecentFileRecord
    {
        public string Id { get; set; }
        public string File { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public double Timestamp { get; set; }
        public string Date { get; set; }
        public string FileName { get; set; }
        public string Project { get; set; }
        public string ModifiedTime { get; set; }
    }

    public class HistoryService
    {
        private readonly string _historyFilePath;

        public HistoryService(string projectRoot)
        {
            string userDataDir = System.IO.Path.Combine(projectRoot, "user_data");
            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }
            _historyFilePath = System.IO.Path.Combine(userDataDir, "recent_files.json");
        }

        public List<RecentFileRecord> GetRecentFiles()
        {
            var list = new List<RecentFileRecord>();
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    string json = File.ReadAllText(_historyFilePath);
                    var serializer = new JavaScriptSerializer();
                    var items = serializer.Deserialize<List<RecentFileRecord>>(json);
                    if (items != null) list = items;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("HistoryService Get Error: " + ex.Message);
            }
            return list;
        }

        public bool AddEntry(string projectId, string filePath, string fileName, string fileType)
        {
            try
            {
                var history = GetRecentFiles();
                history.RemoveAll(x => x.Path != null && x.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                double epoch = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                var newEntry = new RecentFileRecord
                {
                    Id = projectId,
                    File = fileName,
                    FileName = fileName,
                    Path = filePath,
                    Type = fileType,
                    Timestamp = epoch,
                    Date = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    ModifiedTime = DateTime.Now.ToString("dd/MM HH:mm"),
                    Project = projectId
                };

                history.Insert(0, newEntry);
                if (history.Count > 10)
                {
                    history = history.GetRange(0, 10);
                }

                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(history);
                File.WriteAllText(_historyFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("HistoryService Add Error: " + ex.Message);
                return false;
            }
        }

        public bool RemoveEntry(string filePath)
        {
            try
            {
                var history = GetRecentFiles();
                int countBefore = history.Count;
                history.RemoveAll(x => x.Path != null && x.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (history.Count != countBefore)
                {
                    var serializer = new JavaScriptSerializer();
                    string json = serializer.Serialize(history);
                    File.WriteAllText(_historyFilePath, json);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("HistoryService Remove Error: " + ex.Message);
                return false;
            }
        }

        // --- API MATCHING WRAPPERS ---
        public List<RecentFileRecord> ApiGetRecentFiles()
        {
            return GetRecentFiles();
        }

        public bool ApiAddRecentFile(string projectId, string filePath, string fileName, string fileType)
        {
            return AddEntry(projectId, filePath, fileName, fileType);
        }

        public bool ApiRemoveRecentFile(string filePath)
        {
            return RemoveEntry(filePath);
        }
    }
}
