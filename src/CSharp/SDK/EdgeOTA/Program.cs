using System.Text.Json;
using EdgeOTA.Entity;

namespace EdgeOTA
{
    public class Program
    {
        private static string logFilePath;
        
        static async Task Main(string[] args)
        {
            // 创建日志目录和日志文件
            SetupLogger();
            
            LogMessage("=============================================================");
            LogMessage("更新文件程序启动");
            

            if (args.Length == 0)
            {
                Console.WriteLine("请提供要终止的DLL文件名或进程名");
                LogMessage("未提供参数，程序退出");
                return;
            }

            string processName = args[0];
            bool byName = false;
            bool byPid = false;
            int processId = -1;
            string dllFileName = "";  // 用于存储正确的DLL文件名
            string entryAssemblyName = ""; // 存储传入的程序集名称
            
            // 必传参数
            string did = "";
            string uid = "";
            string firmwareType = "";
            string frontPath = "";  // 前端路径参数
            
            // 检查是否提供了必传参数
            bool hasRequiredParams = false;
            
            // 检查是否通过-n参数指定按进程名查找
            if (args.Length > 1)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-n" && i + 1 < args.Length)
                    {
                        byName = true;
                        processName = args[i + 1];
                        
                        // 检查是否提供的是完整的DLL名称
                        if (processName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            // 直接使用提供的DLL名称
                            entryAssemblyName = processName;
                            dllFileName = processName;
                            LogMessage($"使用指定的DLL文件: {dllFileName}");
                        }
                        else
                        {
                            // 对于按名称查找的情况，假设DLL名称与进程名相同，但添加.dll后缀
                            dllFileName = processName + ".dll";
                            LogMessage($"按进程名查找: {processName}, 对应DLL文件: {dllFileName}");
                        }
                    }
                    else if (args[i] == "-pid" && i + 1 < args.Length)
                    {
                        byPid = true;
                        if (int.TryParse(args[i + 1], out processId))
                        {
                            LogMessage($"按进程ID查找: {processId}");
                        }
                        else
                        {
                            LogMessage($"提供的进程ID无效: {args[i + 1]}");
                            processId = -1;
                        }
                    }
                    else if (args[i] == "-did" && i + 1 < args.Length)
                    {
                        did = args[i + 1];
                        LogMessage($"设备ID: {did}");
                    }
                    else if (args[i] == "-uid" && i + 1 < args.Length)
                    {
                        uid = args[i + 1];
                        LogMessage($"用户ID: {uid}");
                    }
                    else if (args[i] == "-firmwareType" && i + 1 < args.Length)
                    {
                        firmwareType = args[i + 1];
                        LogMessage($"固件类型: {firmwareType}");
                    }
                    else if (args[i] == "-frontpath" && i + 1 < args.Length)
                    {
                        frontPath = args[i + 1];
                        LogMessage($"前端路径: {frontPath}");
                    }
                }
            }
            
            // 检查必传参数是否都已提供
            if (string.IsNullOrEmpty(did) || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(firmwareType))
            {
                Console.WriteLine("错误: 必须提供 DID、UID 和 FirmwareType 参数");
                Console.WriteLine("用法: EdgeOTA [DLL文件名或进程名] -did [设备ID] -uid [用户ID] -firmwareType [固件类型] [-frontpath [前端路径]] [-n [进程名] | -pid [进程ID]]");
                LogMessage("缺少必要参数，程序退出");
                return;
            }

            if(OTAHelper.FirmwareType_Frontend.Equals(firmwareType, StringComparison.OrdinalIgnoreCase))
            {
                // 前端类型固件的特殊处理
                if (string.IsNullOrEmpty(frontPath))
                {
                    LogMessage("前端固件类型，但未提供前端路径参数");
                    LogMessage("缺少必要参数，程序退出");
                    return;
                }
            }

            if (OTAHelper.FirmwareType_Backend.Equals(firmwareType, StringComparison.OrdinalIgnoreCase))
            {
                // 后端类型固件的特殊处理


                if (!byName && !byPid)
                {
                    // 如果直接提供了DLL文件名，直接使用
                    dllFileName = processName;
                    // 确保DLL文件名有.dll后缀
                    if (!dllFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        dllFileName += ".dll";
                    }
                    LogMessage($"按DLL文件查找: {dllFileName}");
                }

                try
                {
                    bool found = false;

                    if (byPid && processId > 0)
                    {
                        // 通过进程ID查找
                        try
                        {
                            var process = System.Diagnostics.Process.GetProcessById(processId);

                            // 如果没有指定DLL名称，则使用进程名推测
                            if (string.IsNullOrEmpty(entryAssemblyName))
                            {
                                dllFileName = process.ProcessName + ".dll";
                            }
                            else
                            {
                                // 使用传入的DLL名称
                                dllFileName = entryAssemblyName;
                            }

                            LogMessage($"找到进程ID {processId}，进程名: {process.ProcessName}，将使用DLL: {dllFileName}");

                            process.Kill();
                            string message = $"已终止进程ID: {processId}, 进程名: {process.ProcessName}";
                            Console.WriteLine(message);
                            LogMessage(message);
                            found = true;
                        }
                        catch (ArgumentException)
                        {
                            string message = $"未找到ID为 {processId} 的进程";
                            Console.WriteLine(message);
                            LogMessage(message);
                        }
                        catch (Exception ex)
                        {
                            string message = $"终止进程ID {processId} 时发生错误: {ex.Message}";
                            Console.WriteLine(message);
                            LogMessage(message);
                        }
                    }
                    else if (byName)
                    {
                        // 直接通过进程名查找
                        var processesByName = System.Diagnostics.Process.GetProcessesByName(processName);
                        LogMessage($"通过进程名查找到的进程数量: {processesByName.Length}");

                        if (processesByName.Length > 0)
                        {
                            found = true;
                            foreach (var process in processesByName)
                            {
                                try
                                {
                                    process.Kill();
                                    string message = $"已终止进程: {process.ProcessName}";
                                    Console.WriteLine(message);
                                    LogMessage(message);
                                }
                                catch (Exception ex)
                                {
                                    string message = $"终止进程 {process.ProcessName} 失败: {ex.Message}";
                                    Console.WriteLine(message);
                                    LogMessage(message);
                                }
                            }
                        }
                    }
                    else
                    {
                        // 原有的按DLL文件名查找逻辑
                        var processes = System.Diagnostics.Process.GetProcesses();
                        LogMessage($"开始搜索使用 {dllFileName} 的进程");

                        foreach (var process in processes)
                        {
                            try
                            {
                                if (process.MainModule?.FileName?.EndsWith(dllFileName, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    process.Kill();
                                    string message = $"已终止进程: {process.ProcessName}";
                                    Console.WriteLine(message);
                                    LogMessage(message);
                                    found = true;
                                }
                            }
                            catch (Exception)
                            {
                                // 忽略无法访问的进程
                                continue;
                            }
                        }
                    }

                    if (!found)
                    {
                        string message;
                        if (byName)
                            message = $"未找到名为 {processName} 的进程";
                        else if (byPid)
                            message = $"未找到ID为 {processId} 的进程";
                        else
                            message = $"未找到使用 {dllFileName} 的进程";

                        Console.WriteLine(message);
                        LogMessage(message);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    string message = $"终止进程时发生错误: {ex.Message}";
                    Console.WriteLine(message);
                    LogMessage(message);
                }

                // 等待一段时间开始
                System.Threading.Thread.Sleep(5000);
                LogMessage("更新文件程序开始执行文件替换。。。");


                #region 更新文件
                // 读取本地版本信息
                List<OTAConfig> lstOTAConfigs = null;
                string versionFilePath = OTAHelper.GetVersionFilePath();
                if (File.Exists(versionFilePath))
                {
                    string json = await File.ReadAllTextAsync(versionFilePath);
                    lstOTAConfigs = JsonSerializer.Deserialize<List<OTAConfig>>(json);
                }
                if (lstOTAConfigs == null)
                {
                    lstOTAConfigs = new List<OTAConfig>();
                }
                var findOTAConfig = lstOTAConfigs.FirstOrDefault(x => x.FirmwareType == firmwareType && x.DID == did && x.UID == uid);
                if (findOTAConfig == null)
                {
                    string message = $"未找到OTA配置信息";
                    Console.WriteLine(message);
                    LogMessage(message);
                    return;
                }
                // 获取解压目录
                var extractPath = OTAHelper.GetExtractDir();
                // 将解压目录中的文件复制到程序根目录
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                LogMessage($"开始将文件从 {extractPath} 复制到 {baseDir}");
                try
                {
                    // 获取解压目录中的所有文件
                    var files = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // 计算目标路径
                        string relativePath = Path.GetRelativePath(extractPath, file);
                        string targetPath = Path.Combine(baseDir, relativePath);

                        // 确保目标目录存在
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                        // 添加重试逻辑，处理文件可能被占用的情况
                        int retryCount = 0;
                        bool fileCopied = false;
                        while (!fileCopied && retryCount < 3)
                        {
                            try
                            {
                                // 复制文件,如果存在则覆盖
                                File.Copy(file, targetPath, true);
                                fileCopied = true;
                                //LogMessage($"已复制文件: {relativePath}");
                            }
                            catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
                            {
                                retryCount++;
                                LogMessage($"文件 {relativePath} 被占用，等待重试 ({retryCount}/3)");
                                // 等待一段时间后重试
                                System.Threading.Thread.Sleep(2000);
                            }
                            catch (Exception copyEx)
                            {
                                // 其他类型的错误直接抛出
                                LogMessage($"复制文件 {relativePath} 失败: {copyEx.Message}");
                                throw;
                            }
                        }

                        if (!fileCopied)
                        {
                            LogMessage($"警告: 无法复制文件 {relativePath}，已达到最大重试次数");
                            return;
                        }
                    }
                    LogMessage("文件复制完成");
                }
                catch (Exception ex)
                {
                    string message = $"复制文件时发生错误: {ex.Message}";
                    Console.WriteLine(message);
                    LogMessage(message);
                    return;
                }

                // 保存当前参数到OTA配置中
                findOTAConfig.CurrentVersion = findOTAConfig.RemoteVersion;
                // 保存版本信息到本地文件
                await File.WriteAllTextAsync(versionFilePath, JsonSerializer.Serialize(lstOTAConfigs, new JsonSerializerOptions { WriteIndented = true }));
                #endregion

                try
                {
                    // 使用dotnet命令重启dll
                    LogMessage($"准备重启程序，使用DLL: {dllFileName}");

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = dllFileName,  // 使用正确的DLL文件名（包含.dll后缀）
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    var process = System.Diagnostics.Process.Start(startInfo);
                    if (process != null)
                    {
                        string message = $"已重启进程: {dllFileName}";
                        Console.WriteLine(message);
                        LogMessage(message);
                    }
                    else
                    {
                        string message = $"重启进程失败: {dllFileName}";
                        Console.WriteLine(message);
                        LogMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    string message = $"重启进程时发生错误: {ex.Message}";
                    Console.WriteLine(message);
                    LogMessage(message);
                }

                LogMessage("程序结束");
                LogMessage("=============================================================");
            } else if (OTAHelper.FirmwareType_Frontend.Equals(firmwareType, StringComparison.OrdinalIgnoreCase))
            {
                // 前端类型固件的特殊处理
                #region 更新文件
                // 读取本地版本信息
                List<OTAConfig> lstOTAConfigs = null;
                string versionFilePath = OTAHelper.GetVersionFilePath();
                if (File.Exists(versionFilePath))
                {
                    string json = await File.ReadAllTextAsync(versionFilePath);
                    lstOTAConfigs = JsonSerializer.Deserialize<List<OTAConfig>>(json);
                }
                if (lstOTAConfigs == null)
                {
                    lstOTAConfigs = new List<OTAConfig>();
                }
                var findOTAConfig = lstOTAConfigs.FirstOrDefault(x => x.FirmwareType == firmwareType && x.DID == did && x.UID == uid);
                if (findOTAConfig == null)
                {
                    string message = $"未找到OTA配置信息";
                    Console.WriteLine(message);
                    LogMessage(message);
                    return;
                }
                // 获取解压目录
                var extractPath = OTAHelper.GetExtractDir();
                // 将解压目录中的文件复制到前端路径
                string baseDir = frontPath;
                LogMessage($"开始将文件从 {extractPath} 复制到 {baseDir}");
                try
                {
                    // 获取解压目录中的所有文件
                    var files = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // 计算目标路径
                        string relativePath = Path.GetRelativePath(extractPath, file);
                        string targetPath = Path.Combine(baseDir, relativePath);

                        // 确保目标目录存在
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                        // 添加重试逻辑，处理文件可能被占用的情况
                        int retryCount = 0;
                        bool fileCopied = false;
                        while (!fileCopied && retryCount < 3)
                        {
                            try
                            {
                                // 复制文件,如果存在则覆盖
                                File.Copy(file, targetPath, true);
                                fileCopied = true;
                                //LogMessage($"已复制文件: {relativePath}");
                            }
                            catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
                            {
                                retryCount++;
                                LogMessage($"文件 {relativePath} 被占用，等待重试 ({retryCount}/3)");
                                // 等待一段时间后重试
                                System.Threading.Thread.Sleep(2000);
                            }
                            catch (Exception copyEx)
                            {
                                // 其他类型的错误直接抛出
                                LogMessage($"复制文件 {relativePath} 失败: {copyEx.Message}");
                                throw;
                            }
                        }

                        if (!fileCopied)
                        {
                            LogMessage($"警告: 无法复制文件 {relativePath}，已达到最大重试次数");
                            return;
                        }
                    }
                    LogMessage("文件复制完成");
                }
                catch (Exception ex)
                {
                    string message = $"复制文件时发生错误: {ex.Message}";
                    Console.WriteLine(message);
                    LogMessage(message);
                    return;
                }

                // 保存当前参数到OTA配置中
                findOTAConfig.CurrentVersion = findOTAConfig.RemoteVersion;
                // 保存版本信息到本地文件
                await File.WriteAllTextAsync(versionFilePath, JsonSerializer.Serialize(lstOTAConfigs, new JsonSerializerOptions { WriteIndented = true }));
                #endregion

                LogMessage("程序结束");
                LogMessage("=============================================================");
            }
        }
        
        private static void SetupLogger()
        {
            try
            {
                // 获取程序当前目录
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                
                // 创建OTALogs文件夹
                string logDirectory = Path.Combine(currentDirectory, "OTALogs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // 按日期创建日志文件名
                string dateString = DateTime.Now.ToString("yyyy-MM-dd");
                logFilePath = Path.Combine(logDirectory, $"OTA_Log_{dateString}.txt");
                
                // 记录创建日志的初始信息
                string setupMessage = $"日志系统初始化完成，日志文件: {logFilePath}";
                Console.WriteLine(setupMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置日志系统出错: {ex.Message}");
            }
        }
        
        private static void LogMessage(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(logFilePath))
                    return;
                
                string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                
                // 追加到日志文件
                File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志出错: {ex.Message}");
            }
        }
    }
}
