using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace EtherEditorNative.Backend
{
    public class HttpServerService
    {
        private HttpListener _listener;
        private bool _isRunning;
        private readonly string _frontendDir;
        private readonly string _projectRoot;
        private readonly LogicService _logicService;

        public HttpServerService(string projectRoot)
        {
            _projectRoot = projectRoot;
            _frontendDir = Path.Combine(projectRoot, "frontend");
            if (!Directory.Exists(_frontendDir))
            {
                _frontendDir = projectRoot;
            }
            _logicService = new LogicService(projectRoot);
        }

        public void Start(int port = 8766)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", port));
                _listener.Start();
                _isRunning = true;

                Task.Run(() => ListenLoop());
            }
            catch (Exception ex)
            {
                Console.WriteLine("HttpServerService error: " + ex.Message);
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                if (_listener != null) _listener.Stop();
            }
            catch { }
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    Task.Run(() => ProcessRequest(context));
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            HttpListenerResponse resp = context.Response;

            try
            {
                resp.Headers.Add("Access-Control-Allow-Origin", "*");

                if (req.HttpMethod == "OPTIONS")
                {
                    resp.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                    resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                    resp.StatusCode = 200;
                    resp.Close();
                    return;
                }

                string urlPath = req.Url.AbsolutePath;

                if (urlPath == "/api/call" && req.HttpMethod == "POST")
                {
                    HandleApiCall(req, resp);
                    return;
                }

                if (urlPath == "/api/progress")
                {
                    string json = "{\"status\":\"Ready\",\"percent\":100,\"success\":true}";
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.OutputStream.Write(data, 0, data.Length);
                    resp.Close();
                    return;
                }

                // Serve static files
                if (urlPath == "/" || string.IsNullOrEmpty(urlPath))
                {
                    urlPath = "/index.html";
                }

                string relPath = Uri.UnescapeDataString(urlPath.TrimStart('/'));
                string filePath = Path.Combine(_frontendDir, relPath);
                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(_projectRoot, relPath);
                }

                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    string mime = "application/octet-stream";
                    if (ext == ".html") mime = "text/html";
                    else if (ext == ".css") mime = "text/css";
                    else if (ext == ".js" || ext == ".mjs") mime = "application/javascript";
                    else if (ext == ".json") mime = "application/json";
                    else if (ext == ".png") mime = "image/png";
                    else if (ext == ".jpg" || ext == ".jpeg") mime = "image/jpeg";
                    else if (ext == ".ico") mime = "image/x-icon";

                    byte[] content = File.ReadAllBytes(filePath);
                    resp.ContentType = mime + (mime.StartsWith("text/") || mime == "application/javascript" ? "; charset=utf-8" : "");
                    resp.ContentLength64 = content.Length;

                    if (ext == ".html" || ext == ".css" || ext == ".js")
                    {
                        resp.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
                    }
                    else
                    {
                        resp.Headers.Add("Cache-Control", "public, max-age=86400");
                    }

                    resp.OutputStream.Write(content, 0, content.Length);
                }
                else
                {
                    resp.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                resp.StatusCode = 500;
                Console.WriteLine("Error serving " + req.Url.AbsolutePath + ": " + ex.Message);
            }
            finally
            {
                try { resp.Close(); } catch { }
            }
        }

        private void HandleApiCall(HttpListenerRequest req, HttpListenerResponse resp)
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            {
                string body = reader.ReadToEnd();
                string resultJson = _logicService.DispatchCall(body);
                byte[] data = Encoding.UTF8.GetBytes(resultJson);
                resp.ContentType = "application/json; charset=utf-8";
                resp.OutputStream.Write(data, 0, data.Length);
            }
        }
    }
}
