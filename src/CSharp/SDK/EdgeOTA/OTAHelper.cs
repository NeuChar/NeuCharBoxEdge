using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using EdgeOTA.Entity;
using EdgeOTA.Request;
using EdgeOTA.Response;

namespace EdgeOTA
{
    public class OTAHelper
    {
        public const string DefaultRemoteVersion="初始版本";
        public const string FirmwareType_Backend = "backend";
        public const string FirmwareType_Frontend = "frontend";

        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
        
        static OTAHelper()
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }
        
        private const string VersionFileName = "OTAVersion.json";//OTA记录文件  ，可以记录多个
        private const string VersionDownloadDir = "OTAVersionDownload";//下载文件夹
        private const string ExtractDir = "OTAExtract";//解压文件夹，在VersionDownloadDir下

        /// <summary>
        /// 获取自己设备完整的版本文件路径
        /// </summary>
        /// <returns>版本文件的完整路径</returns>
        public static string GetVersionFilePath()
        {
            // 获取当前应用程序的基础目录
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, VersionFileName);
        }

        /// <summary>
        /// 获取自己设备完整的版本下载文件路径
        /// </summary>
        /// <returns>版本文件的完整路径</returns>
        public static string GetVersionDownloadDir()
        {
            // 获取当前应用程序的基础目录
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, VersionDownloadDir);
        }

        /// <summary>
        /// 获取自己设备完整的解压文件路径
        /// </summary>
        /// <returns>解压文件的完整路径</returns>
        public static string GetExtractDir()
        {
            return Path.Combine(GetVersionDownloadDir(), ExtractDir);
        }


        /// <summary>
        /// 自己 根据UID、DID、FirmwareType获取OTAConfig
        /// Get OTAConfig by UID, DID and FirmwareType
        /// </summary>
        /// <param name="uid">用户ID User ID</param>
        /// <param name="did">设备ID Device ID</param>
        /// <param name="firmwareType">固件类型 Firmware Type</param>
        /// <returns>OTAConfig配置信息 OTA Configuration</returns>
        public static async Task<(OTAConfig,List<OTAConfig>)> GetOTAConfigAsync(string uid, string did, string firmwareType)
        {
            List<OTAConfig> lstOTAConfigs = null;
            string versionFilePath = GetVersionFilePath();
            
            if (File.Exists(versionFilePath))
            {
                string json = await File.ReadAllTextAsync(versionFilePath);
                lstOTAConfigs = JsonSerializer.Deserialize<List<OTAConfig>>(json);
            }
            
            if (lstOTAConfigs == null)
            {
                lstOTAConfigs = new List<OTAConfig>();
            }

            return (lstOTAConfigs.FirstOrDefault(x => 
                x.FirmwareType == firmwareType && 
                x.DID == did && 
                x.UID == uid),lstOTAConfigs);
        }

        /// <summary>
        /// 自己 获取远程服务器版本信息
        /// </summary>
        /// <param name="url">版本检查API地址</param>
        /// <param name="req">OTA请求参数</param>
        /// <returns>OTA响应结果</returns>
        public static async Task<OTABaseResponse<string>> GetRemoteVersionInfoAsync(string url,string api, GetRemoteVersionInfoRequest req,string token)
        {
            try
            {
                url=url.TrimEnd('/');
                api=api.StartsWith('/')?api:"/"+api;

                // 调用API获取最新版本信息
                var queryParams = new Dictionary<string, string>
                {
                    ["did"] = req.DID,
                    ["uid"] = req.UID, 
                    ["firmwareType"] = req.FirmwareType,
                    ["appKey"] = req.AppKey,
                    ["appSecret"] = req.AppSecret
                };
                var requestUrl = $"{url}{api}?{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";
                
                Console.WriteLine("请求更新包："+ requestUrl);
                
                // 创建HTTP请求消息并添加Authorization头
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                
                // 发送HTTP请求
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"远程服务器返回错误状态码: {(int)response.StatusCode} ({response.StatusCode})");
                    return new OTABaseResponse<string>(false, $"服务器返回错误: {(int)response.StatusCode} ({response.StatusCode})");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                var resOTAResponse = JsonSerializer.Deserialize<OTABaseResponse<OTAResponse>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (resOTAResponse == null)
                {
                    Console.WriteLine("无法获取远程版本信息");
                    return new OTABaseResponse<string>(false, "无法获取远程版本信息");
                }
                if(resOTAResponse.Success == false)
                {
                    throw new Exception(resOTAResponse.Message);
                }
                if (resOTAResponse.Data == null)
                {
                    Console.WriteLine("无法获取远程版本信息");
                    throw new Exception("远程固件版本为空");
                }
                
                // 读取本地版本信息
                bool isSaveFile=false;
                
                var (findOTAConfig,lstOTAConfigs) = await GetOTAConfigAsync(req.UID, req.DID, req.FirmwareType);
                if(findOTAConfig == null)
                {
                    findOTAConfig=new OTAConfig(){
                        FirmwareType = req.FirmwareType,
                        DID = req.DID,
                        UID = req.UID,
                        CurrentVersion = DefaultRemoteVersion,
                        RemoteVersion = string.Empty,
                        RemoteFilePath=string.Empty,
                        IgnoreVersion=string.Empty,
                        FilePath=string.Empty
                    };
                    lstOTAConfigs.Add(findOTAConfig);
                    isSaveFile=true;
                }

                if(findOTAConfig.RemoteVersion!=resOTAResponse.Data.FirmwareVersion){
                    findOTAConfig.RemoteVersion = resOTAResponse.Data.FirmwareVersion;
                    findOTAConfig.RemoteFilePath=resOTAResponse.Data.FirmwarePackage;
                    isSaveFile=true;
                }

                if(isSaveFile){
                    // 保存版本信息到本地文件
                    string json = JsonSerializer.Serialize(lstOTAConfigs, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(GetVersionFilePath(), json);
                }

                return new OTABaseResponse<string>(true, string.Empty,findOTAConfig.RemoteVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取远程版本信息失败：{ex.Message}");
                return new OTABaseResponse<string>(false, ex.Message);
            }
        }
        
        /// <summary>
        /// 检查是否需要更新
        /// </summary>
        /// <param name="url">检查更新的URL</param>
        /// <param name="req">请求参数</param>
        /// <returns>检查结果</returns>
        public static async Task<OTABaseResponse<CheckForUpdateResponse>> CheckForUpdateAsync(CheckForUpdateRequest req)
        {
            try
            {
                var (findOTAConfig,lstOTAConfigs) = await GetOTAConfigAsync(req.UID, req.DID, req.FirmwareType);
                if(findOTAConfig == null)
                {
                    return new OTABaseResponse<CheckForUpdateResponse>(false, "版本信息不存在");
                }
                if(string.IsNullOrWhiteSpace(findOTAConfig.RemoteVersion))
                {
                    return new OTABaseResponse<CheckForUpdateResponse>(false, "远程版本号为空");
                }

                if(findOTAConfig.IgnoreVersion != findOTAConfig.RemoteVersion && findOTAConfig.CurrentVersion != findOTAConfig.RemoteVersion){
                    return new OTABaseResponse<CheckForUpdateResponse>(true, "有新版本", new CheckForUpdateResponse(){
                        IsNeedUpdate = true,
                        CurrentVersion = findOTAConfig.CurrentVersion,
                        RemoteVersion = findOTAConfig.RemoteVersion
                    });
                }else{
                    return new OTABaseResponse<CheckForUpdateResponse>(true, "没有新版本", new CheckForUpdateResponse(){
                        IsNeedUpdate = false,
                        CurrentVersion = findOTAConfig.CurrentVersion,
                        RemoteVersion = findOTAConfig.RemoteVersion
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查更新失败：{ex.Message}");
                return new OTABaseResponse<CheckForUpdateResponse>(false, ex.Message);
            }
        }

        /// <summary>
        /// 下载并解压更新包
        /// </summary>
        /// <param name="firmwarePackageUrl">固件包URL</param>
        /// <returns>下载成功返回true，否则返回false</returns>
        public static async Task<OTABaseResponse<string>> DownloadUpdateAsync(DownloadUpdateRequest req ,string token)
        {
            try
            {
                req.BaseUrl=req.BaseUrl.TrimEnd('/');

                var (findOTAConfig,lstOTAConfigs) = await GetOTAConfigAsync(req.UID, req.DID, req.FirmwareType);
                if(findOTAConfig == null)
                {
                    return new OTABaseResponse<string>(false, "版本信息不存在");
                }
                if (string.IsNullOrWhiteSpace(findOTAConfig.RemoteFilePath)) {
                    return new OTABaseResponse<string>(false, "更新包下载地址为空");
                }

                var remotePackUrl=req.BaseUrl+findOTAConfig.RemoteFilePath;

                var downloadPath=Path.Combine(GetVersionDownloadDir(),Path.GetFileName(remotePackUrl));
                Console.WriteLine($"开始下载更新包：{remotePackUrl}");
                
                // 确保下载目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(downloadPath) ?? string.Empty);
                
                // 检查文件是否已存在
                if (File.Exists(downloadPath))
                {
                    Console.WriteLine($"更新包已存在，跳过下载：{downloadPath}");
                }
                else
                {
                    // 下载文件
                    var downloadRequest = new HttpRequestMessage(HttpMethod.Get, remotePackUrl);
                    if (!string.IsNullOrEmpty(token))
                    {
                        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    
                    var downloadResponse = await _httpClient.SendAsync(downloadRequest);
                    downloadResponse.EnsureSuccessStatusCode();
                    
                    byte[] fileBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(downloadPath, fileBytes);
                    Console.WriteLine($"更新包下载完成：{downloadPath}");
                }

                // 如果是zip文件则解压
                if (Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extractPath = GetExtractDir();
                    Console.WriteLine($"解压更新包到：{extractPath}");
                    
                    // 清空解压目录
                    if (Directory.Exists(extractPath))
                    {
                        Directory.Delete(extractPath, true);
                    }
                    Directory.CreateDirectory(extractPath);
                    
                    // 解压文件
                    ZipFile.ExtractToDirectory(downloadPath, extractPath, true);

                    //后端删除一些文件
                    if (req.FirmwareType == FirmwareType_Backend)
                    {
                        //删除App_Data文件夹及以下所有文件
                        var appDataPath = Path.Combine(extractPath, "App_Data");
                        if (Directory.Exists(appDataPath))
                        {
                            Directory.Delete(appDataPath, true);
                        }

                        //删除appsettings.json和OTA文件
                        new List<string>() { "appsettings.json", VersionFileName, VersionFileName_E }.ForEach(file =>
                        {
                            var filePath = Path.Combine(extractPath, file);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                                Console.WriteLine($"删除文件：{filePath}");
                            }
                        });

                        // 删除EdgeOTA相关文件
                        var edgeOTAFiles = Directory.GetFiles(extractPath, $"{nameof(EdgeOTA)}*", SearchOption.AllDirectories);
                        foreach (var file in edgeOTAFiles)
                        {
                            File.Delete(file);
                            Console.WriteLine($"删除文件：{file}");
                        }
                    }
                    
                    // 返回解压后的目录路径
                    return new OTABaseResponse<string>(true, "下载并解压成功", extractPath);
                }
                
                // 返回下载文件路径
                return new OTABaseResponse<string>(true, "下载成功", downloadPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"下载更新包失败：{ex.Message}");
                return new OTABaseResponse<string>(false, ex.Message);
            }
        }


        #region 管理下属设备的版本
        private const string VersionFileName_E = "EdgesOTAVersion.json";//下属边缘设备OTA记录文件，在程序根目录
        private const string VersionDownloadDir_E = "EdgesOTAVersionDownload";//下属边缘设备更新文件文件夹，在wwwroot下
        /// <summary>
        /// 获取完整的版本文件路径
        /// </summary>
        /// <returns>版本文件的完整路径</returns>
        public static string GetVersionFilePath_E()
        {
            // 获取当前应用程序的基础目录
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, VersionFileName_E);
        }

        /// <summary>
        /// 获取完整的wwwroot目录路径
        /// </summary>
        /// <returns>wwwroot目录的完整路径</returns>
        public static string GetWWWRootDir()
        {
            // 获取当前应用程序的基础目录
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir,"wwwroot");
        }

        /// <summary>
        /// 获取完整的版本下载文件路径
        /// </summary>
        /// <returns>版本文件的完整路径</returns>
        public static string GetVersionDownloadDir_E()
        {
            return Path.Combine(GetWWWRootDir(), VersionDownloadDir_E);
        }

        /// <summary>
        /// 根据UID、DID、FirmwareType获取OTAEdgeConfig
        /// </summary>
        /// <param name="uid">用户ID User ID</param>
        /// <param name="did">设备ID Device ID</param>
        /// <param name="firmwareType">固件类型 Firmware Type</param>
        /// <returns>OTAEdgeConfig配置信息 OTA Configuration</returns>
        public static async Task<(OTAEdgeConfig, List<OTAEdgeConfig>)> GetOTAEdgeConfigAsync(string uid, string did, string firmwareType)
        {
            List<OTAEdgeConfig> lstOTAEdgeConfigs = null;
            string versionFilePath = GetVersionFilePath_E();

            if (File.Exists(versionFilePath))
            {
                string json = await File.ReadAllTextAsync(versionFilePath);
                lstOTAEdgeConfigs = JsonSerializer.Deserialize<List<OTAEdgeConfig>>(json);
            }
            if (lstOTAEdgeConfigs == null)
            {
                lstOTAEdgeConfigs = new List<OTAEdgeConfig>();
            }
            return (lstOTAEdgeConfigs.FirstOrDefault(x =>
                x.FirmwareType == firmwareType &&
                x.DID == did &&
                x.UID == uid), lstOTAEdgeConfigs);
        }
        
        /// <summary>
        /// 边缘设备通过GetRemoteVersionInfoAsync请求NCB获取自己信息，NCB通过此方法组成OTAResponse
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="did"></param>
        /// <param name="firmwareType"></param>
        /// <returns></returns>
        public static async Task<OTABaseResponse<OTAResponse>> GetOTAInfoForEdgeRequestAsync(string uid, string did, string firmwareType)
        {
            var (findOTAEdgeConfig,lstOTAEdgeConfigs) = await GetOTAEdgeConfigAsync(uid, did, firmwareType);
            if(findOTAEdgeConfig == null)
            {
                return new OTABaseResponse<OTAResponse>(false, "版本信息不存在",null);
            }

            var OTAResponse=new OTAResponse(){
                FirmwareType=firmwareType,
                FirmwareVersion=findOTAEdgeConfig.RemoteVersion,
                FirmwarePackage=findOTAEdgeConfig.RemoteFilePath
            };

            return new OTABaseResponse<OTAResponse>(true, string.Empty, OTAResponse);
        }
        
        /// <summary>
        /// 获取远程服务器上下属边缘设备版本信息
        /// </summary>
        /// <param name="url">版本检查API地址</param>
        /// <param name="api">API路径</param>
        /// <param name="req">OTA请求参数</param>
        /// <param name="token">认证Token</param>
        /// <returns>OTA响应结果</returns>
        public static async Task<OTABaseResponse<string>> GetRemoteVersionInfoForEdgeAsync(string url, string api, GetRemoteVersionInfoRequest req, string token = null)
        {
            try
            {
                url=url.TrimEnd('/');
                api=api.StartsWith('/')?api:"/"+api;

                // 调用API获取最新版本信息
                var queryParams = new Dictionary<string, string>
                {
                    ["did"] = req.DID,
                    ["uid"] = req.UID,
                    ["firmwareType"] = req.FirmwareType,
                    ["appKey"] = req.AppKey,
                    ["appSecret"] = req.AppSecret
                };
                var requestUrl = $"{url}{api}?{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";
                Console.WriteLine("请求更新包："+ requestUrl);
                
                // 创建HTTP请求消息并添加Authorization头
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                
                // 发送HTTP请求
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"远程服务器返回错误状态码: {(int)response.StatusCode} ({response.StatusCode})");
                    return new OTABaseResponse<string>(false, $"服务器返回错误: {(int)response.StatusCode} ({response.StatusCode})");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                var resOTAResponse = JsonSerializer.Deserialize<OTABaseResponse<OTAResponse>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (resOTAResponse == null)
                {
                    Console.WriteLine("无法获取远程版本信息");
                    return new OTABaseResponse<string>(false, "无法获取远程版本信息");
                }
                Console.WriteLine("请求更新包：" + requestUrl);
                if (resOTAResponse.Success == false)
                {
                    throw new Exception(resOTAResponse.Message);
                }
                if (resOTAResponse.Data == null)
                {
                    Console.WriteLine("无法获取远程版本信息");
                    throw new Exception("远程固件版本为空");
                }

                // 读取本地版本信息
                bool isSaveFile = false;

                var (findOTAEdgeConfig, lstOTAEdgeConfigs) = await GetOTAEdgeConfigAsync(req.UID, req.DID, req.FirmwareType);
                if (findOTAEdgeConfig == null)
                {
                    findOTAEdgeConfig = new OTAEdgeConfig()
                    {
                        FirmwareType = req.FirmwareType,
                        DID = req.DID,
                        UID = req.UID,
                        RemoteVersion = string.Empty,
                        RemoteFilePath = string.Empty,
                    };
                    lstOTAEdgeConfigs.Add(findOTAEdgeConfig);
                    isSaveFile = true;
                }

                if (findOTAEdgeConfig.RemoteVersion != resOTAResponse.Data.FirmwareVersion)
                {
                    #region 下载更新包
                    var remotePackUrl=url + resOTAResponse.Data.FirmwarePackage;

                    var downloadPath=Path.Combine(GetVersionDownloadDir_E(),Path.GetFileName(remotePackUrl));
                    Console.WriteLine($"开始下载更新包：{remotePackUrl}");
                    
                    // 确保下载目录存在
                    Directory.CreateDirectory(Path.GetDirectoryName(downloadPath) ?? string.Empty);
                    
                    // 检查文件是否已存在
                    if (File.Exists(downloadPath))
                    {
                        Console.WriteLine($"更新包已存在，跳过下载：{downloadPath}");
                    }
                    else
                    {
                        // 下载文件
                        var downloadRequest = new HttpRequestMessage(HttpMethod.Get, remotePackUrl);
                        if (!string.IsNullOrEmpty(token))
                        {
                            downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        }
                        
                        var downloadResponse = await _httpClient.SendAsync(downloadRequest);
                        downloadResponse.EnsureSuccessStatusCode();
                        
                        byte[] fileBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(downloadPath, fileBytes);
                        Console.WriteLine($"更新包下载完成：{downloadPath}");
                    }
                    
                    #endregion

                    findOTAEdgeConfig.RemoteVersion = resOTAResponse.Data.FirmwareVersion;
                    findOTAEdgeConfig.RemoteFilePath = "/"+ downloadPath.Replace(GetWWWRootDir(),"").TrimStart('/');
                    isSaveFile = true;
                }
                else
                {
                    Console.WriteLine($"边缘设备无更新--DID：{req.DID},UID:{req.UID}");
                }

                if (isSaveFile)
                {
                    // 保存版本信息到本地文件
                    string json = JsonSerializer.Serialize(lstOTAEdgeConfigs, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(GetVersionFilePath_E(), json);
                }

                return new OTABaseResponse<string>(true, string.Empty, findOTAEdgeConfig.RemoteVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取远程版本信息失败：{ex.Message}");
                return new OTABaseResponse<string>(false, ex.Message);
            }
        }

        #endregion

    }



}
