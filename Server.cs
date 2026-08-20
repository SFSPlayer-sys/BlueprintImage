using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using UnityEngine;
namespace BlueprintImage
{
    public class RenderTask
    {
        public string TaskId { get; set; }
        public string BlueprintJson { get; set; }
        public bool IsInternalView { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Color BackgroundColor { get; set; }
        public RenderStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public byte[] ImageData { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public float Progress { get; set; }
    }

    public enum RenderStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    public class BlueprintServer : MonoBehaviour
    {
        private HttpListener listener;
        private Thread listenerThread;
        private bool isRunning = false;
        private readonly ConcurrentQueue<HttpListenerContext> requestQueue = new ConcurrentQueue<HttpListenerContext>();
        private readonly ConcurrentDictionary<string, RenderTask> renderTasks = new ConcurrentDictionary<string, RenderTask>();
        private static readonly string LogFilePath = Path.Combine(Application.dataPath, "..", "Mods", "BlueprintImage", "Log.txt");
        private static readonly string GameLogFilePath = Path.Combine(Application.dataPath, "..", "Mods", "BlueprintImage", "GameLog.txt");

        //写入日志文件
        private static void WriteLog(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
                File.WriteAllText(LogFilePath, logMessage + Environment.NewLine);
                Debug.Log($"[BlueprintImage] {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] Failed to write log: {ex.Message}");
            }
        }

        //追加日志文件
        private static void AppendLog(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
                Debug.Log($"[BlueprintImage] {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] Failed to append log: {ex.Message}");
            }
        }

        //获取游戏控制台日志
        public static List<string> GetConsoleLog(int maxLines = 150)
        {
            try
            {
                var queueField = typeof(ModLoader.IO.Console).GetField("queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var queue = queueField?.GetValue(ModLoader.IO.Console.main) as Queue<string>;
                if (queue == null)
                    return new List<string> { "No console log available" };
                return queue.Reverse().Take(maxLines).Reverse().ToList();
            }
            catch (Exception ex)
            {
                return new List<string> { $"Failed to get console log: {ex.Message}" };
            }
        }

        //保存游戏日志到文件
        private static void SaveGameLog()
        {
            try
            {
                var consoleLog = GetConsoleLog(1000);
                var logContent = string.Join(Environment.NewLine, consoleLog);
                File.WriteAllText(GameLogFilePath, logContent);
                AppendLog($"Game log saved to {GameLogFilePath}, {consoleLog.Count} lines");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to save game log: {ex.Message}");
            }
        }

        public void StartServer(int port)
        {
            if (isRunning) return;

            WriteLog($"Starting BlueprintImage server on port {port}");
            
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            
            listenerThread = new Thread(() =>
            {
                try
                {
                    listener.Start();
                    isRunning = true;
                    AppendLog($"Server started successfully on port {port}");

                    while (isRunning)
                    {
                        var context = listener.GetContext();
                        requestQueue.Enqueue(context);
                    }
                }
                catch (Exception ex) when (ex is HttpListenerException)
                {
                    AppendLog($"Port {port} may be in use: {ex.Message}");
                }
                catch (Exception ex)
                {
                    AppendLog($"Server error: {ex.Message}");
                }
            });

            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        void Update()
        {
            while (requestQueue.TryDequeue(out var context))
            {
                HandleRequestOnMainThread(context);
            }
        }

        private void HandleRequestOnMainThread(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            string responseString = "";
            int statusCode = 200;
            bool responseHandled = false;

            try
            {
                switch (request.Url.AbsolutePath)
                {
                    case "/render":
                        HandleRenderRequest(context);
                        responseHandled = true;
                        return;
                    case "/version":
                        responseString = JsonConvert.SerializeObject(new { name = "BlueprintImage", version = "v1.1" });
                        break;
                    case "/test":
                        responseString = JsonConvert.SerializeObject(new { 
                            message = "BlueprintImage server is running", 
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            endpoints = new[] { "/render", "/status/{taskId}", "/result/{taskId}", "/gamelog", "/version", "/test" }
                        });
                        break;
                    default:
                        string path = request.Url.AbsolutePath;
                        if (path.StartsWith("/status/"))
                        {
                            HandleStatusRequest(context);
                            responseHandled = true;
                            return;
                        }
                        else if (path.StartsWith("/result/"))
                        {
                            HandleResultRequest(context);
                            responseHandled = true;
                            return;
                        }
                        
                        statusCode = 404;
                        responseString = JsonConvert.SerializeObject(new { 
                            error = "Not Found", 
                            availableEndpoints = new[] { "/render", "/status/{taskId}", "/result/{taskId}", "/version", "/test" }
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = JsonConvert.SerializeObject(new { error = $"Internal Server Error: {ex.Message}" });
                Debug.LogError($"[BlueprintImage] Request handling error: {ex}");
            }
            finally
            {
                if (!responseHandled && response.OutputStream.CanWrite)
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.StatusCode = statusCode;
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
            }
        }

        private void HandleRenderRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                AppendLog($"Handling render request: {request.Url}");
                
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    requestBody = reader.ReadToEnd();
                
                AppendLog($"Request body size: {requestBody.Length} bytes");
                
                var requestData = JsonConvert.DeserializeObject<Dictionary<string, object>>(requestBody);
                if (requestData == null)
                {
                    Debug.LogError("[BlueprintImage] Invalid JSON request body");
                    SendErrorResponse(context, "Invalid JSON request body");
                    return;
                }
                
                string blueprintJson = requestData.ContainsKey("blueprint") ? requestData["blueprint"]?.ToString() : "";
                if (string.IsNullOrEmpty(blueprintJson))
                {
                    Debug.LogError("[BlueprintImage] Missing blueprint parameter");
                    SendErrorResponse(context, "Missing blueprint parameter");
                    return;
                }

                bool isInternalView = requestData.ContainsKey("internal") && 
                    (requestData["internal"]?.ToString().ToLower() == "true" || requestData["internal"]?.ToString() == "1");
                
                int width = 0;
                int height = 0;
                if (requestData.ContainsKey("width") && int.TryParse(requestData["width"]?.ToString(), out int w) && w > 0)
                    width = w;
                if (requestData.ContainsKey("height") && int.TryParse(requestData["height"]?.ToString(), out int h) && h > 0)
                    height = h;

                //解析16进制颜色
                Color backgroundColor = Color.blue;
                if (requestData.ContainsKey("bgcolor"))
                {
                    string colorStr = requestData["bgcolor"]?.ToString() ?? "";
                    if (colorStr.StartsWith("#"))
                        colorStr = colorStr.Substring(1);
                    
                    if (int.TryParse(colorStr, System.Globalization.NumberStyles.HexNumber, null, out int colorValue))
                    {
                        backgroundColor = new Color(
                            ((colorValue >> 16) & 0xFF) / 255f,
                            ((colorValue >> 8) & 0xFF) / 255f,
                            (colorValue & 0xFF) / 255f,
                            1f
                        );
                    }
                }

                //大蓝图使用协程渲染
                bool useCoroutine = blueprintJson.Length > 1024 * 1024;
                
                if (useCoroutine)
                {
                    string taskId = Guid.NewGuid().ToString();
                    var renderTask = new RenderTask
                    {
                        TaskId = taskId,
                        BlueprintJson = blueprintJson,
                        IsInternalView = isInternalView,
                        Width = width,
                        Height = height,
                        BackgroundColor = backgroundColor,
                        Status = RenderStatus.Pending,
                        StartTime = DateTime.Now,
                        Progress = 0f
                    };

                    renderTasks[taskId] = renderTask;

                    StartCoroutine(ProcessRenderTaskCoroutine(renderTask));

                    string responseString = JsonConvert.SerializeObject(new { 
                        taskId = taskId, 
                        status = "processing",
                        message = "Large blueprint rendering started. Use /status/{taskId} to check progress, /result/{taskId} to get image."
                    });
                    
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.StatusCode = 202;
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                    
                    AppendLog($"Started coroutine render task: {taskId} for large blueprint ({blueprintJson.Length} bytes)");
                }
                else
                {
                    byte[] imageData = RenderBlueprint(blueprintJson, isInternalView, width, height, backgroundColor);
                    if (imageData != null && imageData.Length > 0)
                    {
                        response.ContentType = "image/png";
                        response.ContentLength64 = imageData.Length;
                        response.OutputStream.Write(imageData, 0, imageData.Length);
                        response.OutputStream.Close();
                    }
                    else
                    {
                        SendErrorResponse(context, "Failed to render blueprint");
                    }
                }
            }
            catch (Exception ex)
            {
                SendErrorResponse(context, $"Render error: {ex.Message}");
            }
        }

        private byte[] RenderBlueprint(string blueprintJson, bool isInternalView, int width, int height, Color backgroundColor)
        {
            try
            {
                Debug.Log($"[BlueprintImage] Starting blueprint render - Width: {width}, Height: {height}, Internal: {isInternalView}");
                
                var blueprint = JsonConvert.DeserializeObject<Blueprint>(blueprintJson);
                if (blueprint == null || blueprint.parts == null || blueprint.parts.Length == 0)
                {
                    Debug.LogError("[BlueprintImage] Invalid blueprint data");
                    return null;
                }
                
                Debug.Log($"[BlueprintImage] Blueprint loaded with {blueprint.parts.Length} parts");

                //创建临时部件获取边界
                OwnershipState[] ownershipStates;
                Part[] tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
                
                Rect rect;
                Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);

                for (int i = 0; i < tempParts.Length; i++)
                {
                    if (tempParts[i] != null && tempParts[i].gameObject != null)
                    {
                        tempParts[i].gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(tempParts[i].gameObject);
                    }
                }

                if (width <= 0 || height <= 0)
                {
                    //按蓝图边界自动计算尺寸
                    float pixelsPerUnit = 100f;
                    float paddedWidth = rect.width + 2f;
                    float paddedHeight = rect.height + 2f;
                    
                    width = Mathf.Max(256, Mathf.RoundToInt(paddedWidth * pixelsPerUnit));
                    height = Mathf.Max(256, Mathf.RoundToInt(paddedHeight * pixelsPerUnit));
                    
                    const int maxDimension = 7500;
                    if (width > maxDimension || height > maxDimension)
                    {
                        float scale = Mathf.Min((float)maxDimension / width, (float)maxDimension / height);
                        width = Mathf.RoundToInt(width * scale);
                        height = Mathf.RoundToInt(height * scale);
                    }
                }

                RenderTexture iconRT = CreateBlueprintRenderTexture(blueprint, width, height, backgroundColor);
                if (iconRT == null)
                {
                    Debug.LogError("[BlueprintImage] Failed to create render texture");
                    return null;
                }

                byte[] pngData = null;
                try
                {
                    pngData = SFS.UI.ImageTools.RenderTextureToPng(iconRT, width, height);
                    Debug.Log($"[BlueprintImage] Successfully generated PNG data, size: {pngData?.Length ?? 0} bytes");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BlueprintImage] Failed to convert to PNG: {ex.Message}");
                    iconRT.Release();
                    UnityEngine.Object.Destroy(iconRT);
                    return null;
                }
                
                iconRT.Release();
                UnityEngine.Object.Destroy(iconRT);

                return pngData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] Render error: {ex.Message}");
                return null;
            }
        }

        private RenderTexture CreateBlueprintRenderTexture(Blueprint blueprint, int width, int height, Color backgroundColor)
        {
            try
            {
                OwnershipState[] ownershipStates;
                Part[] tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
                
                Rect rect;
                Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);
                
                //添加边距
                Vector2 v = Vector2.one * 2f;
                rect = new Rect(rect.position - v / 2f, rect.size + v);
                
                //反射调用RenderAndDestroy
                var method = typeof(PartIconCreator).GetMethod("RenderAndDestroy", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (method != null)
                {
                    return (RenderTexture)method.Invoke(PartIconCreator.main, new object[] { tempParts, rect, width, height, backgroundColor });
                }
                else
                {
                    return PartIconCreator.main.CreatePartIcon_Sharing(blueprint, width, height);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] CreateBlueprintRenderTexture error: {ex.Message}");
                return null;
            }
        }

        /*
        //旧版无状态码的错误响应，已被下方带statusCode参数版本取代
        private void SendErrorResponse(HttpListenerContext context, string errorMessage)
        {
            try
            {
                if (context?.Response == null)
                {
                    Debug.LogWarning("[BlueprintImage] Cannot send error response: context or response is null");
                    return;
                }

                var response = context.Response;
                response.StatusCode = 500;
                response.ContentType = "application/json";
                string responseString = JsonConvert.SerializeObject(new { error = errorMessage });
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] Error sending response: {ex.Message}");
            }
        }

        //未使用的查询字符串解析
        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrEmpty(pair)) continue;
                var kv = pair.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(kv[0]);
                var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                dict[key] = value;
            }
            return dict;
        }
        */

        public void StopServer()
        {
            if (isRunning)
            {
                isRunning = false;
                listener?.Stop();
                listenerThread?.Join();
                Debug.Log("[BlueprintImage] Server stopped.");
            }
        }

        //状态查询请求
        private void HandleStatusRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath;
                string taskId = path.Substring("/status/".Length);
                
                if (renderTasks.TryGetValue(taskId, out RenderTask task))
                {
                    string responseString = JsonConvert.SerializeObject(new {
                        taskId = task.TaskId,
                        status = task.Status.ToString().ToLower(),
                        progress = task.Progress,
                        startTime = task.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        endTime = task.EndTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                        errorMessage = task.ErrorMessage,
                        duration = task.EndTime.HasValue ? 
                            (task.EndTime.Value - task.StartTime).TotalSeconds : 
                            (DateTime.Now - task.StartTime).TotalSeconds
                    });
                    
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.StatusCode = 200;
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else
                {
                    SendErrorResponse(context, "Task not found", 404);
                }
            }
            catch (Exception ex)
            {
                SendErrorResponse(context, $"Status query error: {ex.Message}");
            }
        }

        //结果获取请求
        private void HandleResultRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath;
                string taskId = path.Substring("/result/".Length);
                
                if (renderTasks.TryGetValue(taskId, out RenderTask task))
                {
                    if (task.Status == RenderStatus.Completed && task.ImageData != null)
                    {
                        response.ContentType = "image/png";
                        response.ContentLength64 = task.ImageData.Length;
                        response.OutputStream.Write(task.ImageData, 0, task.ImageData.Length);
                        response.OutputStream.Close();
                        
                        renderTasks.TryRemove(taskId, out _);
                    }
                    else if (task.Status == RenderStatus.Failed)
                    {
                        SendErrorResponse(context, task.ErrorMessage ?? "Render failed", 500);
                    }
                    else
                    {
                        SendErrorResponse(context, "Task not completed yet", 202);
                    }
                }
                else
                {
                    SendErrorResponse(context, "Task not found", 404);
                }
            }
            catch (Exception ex)
            {
                SendErrorResponse(context, $"Result query error: {ex.Message}");
            }
        }

        //协程渲染任务，分帧处理大蓝图避免卡死
        private System.Collections.IEnumerator ProcessRenderTaskCoroutine(RenderTask task)
        {
            AppendLog($"Starting coroutine render task: {task.TaskId}");
            task.Status = RenderStatus.Processing;
            task.Progress = 0.1f;

            yield return null;
            task.Progress = 0.2f;

            Blueprint blueprint = null;
            try
            {
                AppendLog($"Parsing blueprint JSON for task {task.TaskId}");
                blueprint = JsonConvert.DeserializeObject<Blueprint>(task.BlueprintJson);
                AppendLog($"Blueprint parsed successfully, parts count: {blueprint?.parts?.Length ?? 0}");
                SaveGameLog();
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to parse blueprint JSON for task {task.TaskId}: {ex.Message}");
                SaveGameLog();
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Invalid blueprint JSON format";
                task.EndTime = DateTime.Now;
                yield break;
            }

            if (blueprint == null || blueprint.parts == null || blueprint.parts.Length == 0)
            {
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Invalid blueprint data";
                task.EndTime = DateTime.Now;
                yield break;
            }

            yield return null;
            task.Progress = 0.3f;

            SFS.Parts.Modules.OwnershipState[] ownershipStates = null;
            Part[] tempParts = null;
            
            AppendLog($"Creating parts for task {task.TaskId}, count: {blueprint.parts.Length}");
            
            //大蓝图分批创建部件
            if (blueprint.parts.Length > 5000)
            {
                AppendLog($"Large blueprint detected ({blueprint.parts.Length} parts), creating parts in batches");
                
                int batchSize = 1000;
                int totalBatches = (blueprint.parts.Length + batchSize - 1) / batchSize;
                
                for (int batch = 0; batch < totalBatches; batch++)
                {
                    int startIndex = batch * batchSize;
                    int endIndex = Mathf.Min(startIndex + batchSize, blueprint.parts.Length);
                    int currentBatchSize = endIndex - startIndex;
                    
                    AppendLog($"Creating batch {batch + 1}/{totalBatches} ({currentBatchSize} parts)");
                    
                    try
                    {
                        var batchParts = new SFS.Parts.PartSave[currentBatchSize];
                        Array.Copy(blueprint.parts, startIndex, batchParts, 0, currentBatchSize);
                        
                        var batchOwnershipStates = new SFS.Parts.Modules.OwnershipState[currentBatchSize];
                        var batchTempParts = PartsLoader.CreateParts(batchParts, null, null, OnPartNotOwned.Allow, out batchOwnershipStates);
                        
                        if (tempParts == null)
                        {
                            tempParts = new Part[blueprint.parts.Length];
                            ownershipStates = new SFS.Parts.Modules.OwnershipState[blueprint.parts.Length];
                        }
                        
                        Array.Copy(batchTempParts, 0, tempParts, startIndex, currentBatchSize);
                        Array.Copy(batchOwnershipStates, 0, ownershipStates, startIndex, currentBatchSize);
                        
                        task.Progress = 0.3f + (0.1f * (batch + 1) / totalBatches);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Failed to create batch {batch + 1}: {ex.Message}");
                        task.Status = RenderStatus.Failed;
                        task.ErrorMessage = "Failed to create parts";
                        task.EndTime = DateTime.Now;
                        yield break;
                    }
                    
                    yield return null;
                }
            }
            else
            {
                try
                {
                    tempParts = PartsLoader.CreateParts(blueprint.parts, null, null, OnPartNotOwned.Allow, out ownershipStates);
                }
                catch (Exception ex)
                {
                    AppendLog($"Failed to create parts for task {task.TaskId}: {ex.Message}");
                    SaveGameLog();
                    task.Status = RenderStatus.Failed;
                    task.ErrorMessage = "Failed to create parts";
                    task.EndTime = DateTime.Now;
                    yield break;
                }
            }
            
            AppendLog($"Parts created successfully for task {task.TaskId}");
            SaveGameLog();
            
            yield return null;
            task.Progress = 0.4f;

            Rect rect;
            try
            {
                AppendLog($"Getting framing bounds for task {task.TaskId}");
                Part_Utility.GetFramingBounds_WorldSpace(out rect, tempParts);
                AppendLog($"Bounds calculated: {rect}");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to get framing bounds: {ex.Message}");
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Failed to get framing bounds";
                task.EndTime = DateTime.Now;
                yield break;
            }

            AppendLog($"Cleaning up temporary parts for task {task.TaskId}");
            for (int i = 0; i < tempParts.Length; i++)
            {
                if (tempParts[i] != null && tempParts[i].gameObject != null)
                {
                    tempParts[i].gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(tempParts[i].gameObject);
                }
                
                //每100个部件等待一帧
                if (i % 100 == 0)
                {
                    yield return null;
                }
            }
            
            yield return null;
            task.Progress = 0.4f;

            yield return null;
            task.Progress = 0.5f;

            int width = task.Width;
            int height = task.Height;
            if (width <= 0 || height <= 0)
            {
                float pixelsPerUnit = 100f;
                float paddedWidth = rect.width + 2f;
                float paddedHeight = rect.height + 2f;
                
                width = Mathf.Max(256, Mathf.RoundToInt(paddedWidth * pixelsPerUnit));
                height = Mathf.Max(256, Mathf.RoundToInt(paddedHeight * pixelsPerUnit));
                
                const int maxDimension = 7500;
                if (width > maxDimension || height > maxDimension)
                {
                    float scale = Mathf.Min((float)maxDimension / width, (float)maxDimension / height);
                    width = Mathf.RoundToInt(width * scale);
                    height = Mathf.RoundToInt(height * scale);
                }
            }

            yield return null;
            task.Progress = 0.6f;

            RenderTexture iconRT = null;
            try
            {
                AppendLog($"Creating render texture for blueprint ({blueprint.parts.Length} parts), size: {width}x{height}");
                iconRT = CreateBlueprintRenderTexture(blueprint, width, height, task.BackgroundColor);
                AppendLog($"Render texture created successfully for task {task.TaskId}");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to create render texture: {ex.Message}");
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Failed to create render texture";
                task.EndTime = DateTime.Now;
                yield break;
            }

            if (iconRT == null)
            {
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Failed to create render texture";
                task.EndTime = DateTime.Now;
                yield break;
            }

            yield return null;
            task.Progress = 0.8f;

            byte[] pngData = null;
            try
            {
                AppendLog($"Converting render texture to PNG for task {task.TaskId}");
                pngData = SFS.UI.ImageTools.RenderTextureToPng(iconRT, width, height);
                AppendLog($"PNG conversion completed for task {task.TaskId}, size: {pngData?.Length ?? 0} bytes");
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to convert to PNG: {ex.Message}");
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Failed to convert to PNG";
                task.EndTime = DateTime.Now;
            }
            finally
            {
                if (iconRT != null)
                {
                    iconRT.Release();
                    UnityEngine.Object.Destroy(iconRT);
                }
            }

            yield return null;
            task.Progress = 1.0f;

            if (pngData != null && pngData.Length > 0)
            {
                task.ImageData = pngData;
                task.Status = RenderStatus.Completed;
                task.EndTime = DateTime.Now;
                AppendLog($"Coroutine render task completed: {task.TaskId}, size: {pngData.Length} bytes");
                SaveGameLog();
            }
            else
            {
                task.Status = RenderStatus.Failed;
                task.ErrorMessage = "Failed to render blueprint";
                task.EndTime = DateTime.Now;
                AppendLog($"Coroutine render task failed: {task.TaskId}");
                SaveGameLog();
            }
        }

        //错误响应，statusCode默认500
        private void SendErrorResponse(HttpListenerContext context, string errorMessage, int statusCode = 500)
        {
            try
            {
                if (context?.Response == null)
                {
                    Debug.LogWarning("[BlueprintImage] Cannot send error response: context or response is null");
                    return;
                }

                var response = context.Response;
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                string responseString = JsonConvert.SerializeObject(new { error = errorMessage });
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BlueprintImage] Error sending response: {ex.Message}");
            }
        }

        void OnDestroy()
        {
            StopServer();
        }
    }
}
