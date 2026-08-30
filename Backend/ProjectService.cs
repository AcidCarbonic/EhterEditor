using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace EtherEditorNative.Backend
{
    public class ProjectItem
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string GameType { get; set; }
        public string ModifiedTime { get; set; }
    }

    public class ProjectFileResult
    {
        public string Status { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
        public string Game { get; set; }
        public string Path { get; set; }
        public string Message { get; set; }
    }

    public class ProjectService
    {
        private readonly string _savesDir;

        public ProjectService(string projectRoot)
        {
            _savesDir = System.IO.Path.Combine(projectRoot, "saves");
            if (!Directory.Exists(_savesDir))
            {
                Directory.CreateDirectory(_savesDir);
            }
        }

        public string GetSavesDirectory()
        {
            return _savesDir;
        }

        public string ResolvePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            if (!System.IO.Path.IsPathRooted(filePath) && string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(filePath)))
            {
                return System.IO.Path.Combine(_savesDir, filePath);
            }
            return System.IO.Path.GetFullPath(filePath);
        }

        public ProjectFileResult LoadFile(string filePath)
        {
            string resolvedPath = ResolvePath(filePath);
            if (!File.Exists(resolvedPath))
            {
                return new ProjectFileResult { Status = "error", Message = "File không tồn tại tại: " + resolvedPath };
            }

            try
            {
                string content = File.ReadAllText(resolvedPath);
                var serializer = new JavaScriptSerializer();

                try
                {
                    var data = serializer.Deserialize<Dictionary<string, object>>(content);
                    if (data != null)
                    {
                        string src = data.ContainsKey("source") && data["source"] != null ? data["source"].ToString() 
                            : (data.ContainsKey("ether_source") && data["ether_source"] != null ? data["ether_source"].ToString() : "");
                        string tgt = data.ContainsKey("target") && data["target"] != null ? data["target"].ToString() 
                            : (data.ContainsKey("ether_target") && data["ether_target"] != null ? data["ether_target"].ToString() : "");
                        string gameId = data.ContainsKey("game") && data["game"] != null ? data["game"].ToString() : "hsr";

                        if (data.ContainsKey("source") || data.ContainsKey("ether_source"))
                        {
                            return new ProjectFileResult
                            {
                                Status = "success",
                                Type = "project",
                                Source = src,
                                Target = tgt,
                                Game = gameId,
                                Path = resolvedPath
                            };
                        }
                    }
                }
                catch { }

                return new ProjectFileResult
                {
                    Status = "success",
                    Type = "text",
                    Source = content,
                    Target = "",
                    Path = resolvedPath
                };
            }
            catch (Exception ex)
            {
                return new ProjectFileResult { Status = "error", Message = ex.Message };
            }
        }

        public ProjectFileResult SaveWorkspace(string filePath, string sourceText, string targetText, string gameId = "hsr")
        {
            string resolvedPath = ResolvePath(filePath);
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(resolvedPath)))
            {
                resolvedPath += ".json";
            }

            var data = new Dictionary<string, object>
            {
                { "meta_info", "EtherEditor Project File" },
                { "timestamp", DateTime.Now.ToString("o") },
                { "game", gameId },
                { "source", sourceText },
                { "target", targetText },
                { "ether_source", sourceText },
                { "ether_target", targetText }
            };

            try
            {
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(data);
                File.WriteAllText(resolvedPath, json);
                return new ProjectFileResult { Status = "success", Path = resolvedPath };
            }
            catch (Exception ex)
            {
                return new ProjectFileResult { Status = "error", Message = ex.Message };
            }
        }

        public List<ProjectItem> ListProjects()
        {
            var list = new List<ProjectItem>();
            try
            {
                if (Directory.Exists(_savesDir))
                {
                    string[] files = Directory.GetFiles(_savesDir, "*.json");
                    foreach (string file in files)
                    {
                        var info = new FileInfo(file);
                        list.Add(new ProjectItem
                        {
                            Name = System.IO.Path.GetFileNameWithoutExtension(file),
                            FilePath = file,
                            GameType = "Honkai: Star Rail",
                            ModifiedTime = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ProjectService List Error: " + ex.Message);
            }
            return list;
        }
    }
}
