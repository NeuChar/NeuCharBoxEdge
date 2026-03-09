using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices
{
    /// <summary>
    /// WiFi网络信息
    /// </summary>
    public class WifiNetworkInfo
    {
        public string SSID { get; set; }
        public string BSSID { get; set; }
        public int Signal { get; set; }
        public string Security { get; set; }
        public string Frequency { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>
    /// WiFi后台服务 - 专门用于WiFi状态管理和网络扫描
    /// </summary>
    public class WifiBackgroundService : BackgroundService
    {
        private readonly ILogger<WifiBackgroundService> _logger;
        
        // 静态变量存储WiFi扫描结果
        public static readonly ConcurrentDictionary<string, WifiNetworkInfo> AvailableNetworks = new();
        public static bool IsWifiEnabled { get; private set; } = false;
        public static DateTime LastScanTime { get; private set; } = DateTime.MinValue;
        public static string WifiInterfaceName { get; private set; } = "wlan0";
        
        // 扫描间隔设置
        private const int SCAN_INTERVAL_SECONDS = 30; // 每30秒扫描一次
        private const int INIT_RETRY_INTERVAL_SECONDS = 10; // 初始化重试间隔

        public WifiBackgroundService(ILogger<WifiBackgroundService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WiFi后台服务启动中...");

            try
            {
                // 直接开始周期性扫描，初始化会在扫描循环中自动处理
                await StartPeriodicScanAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("WiFi后台服务已被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WiFi后台服务执行失败");
            }
        }

        /// <summary>
        /// 初始化WiFi接口
        /// </summary>
        private async Task InitializeWifiInterfaceAsync()
        {
            int retryCount = 0;
            const int maxRetries = 5;

            while (retryCount < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"第{retryCount + 1}次尝试初始化WiFi接口...");

                    // 检查并停止dhcpcd服务（如果运行）
                    await EnsureDhcpcdStoppedAsync();

                    // 检查NetworkManager服务状态
                    await EnsureNetworkManagerRunningAsync();

                    // 检查WiFi硬件接口
                    await DetectWifiInterfaceAsync();

                    // 启用WiFi功能
                    await EnableWifiAsync();

                    IsWifiEnabled = true;
                    _logger.LogInformation("WiFi接口初始化成功");
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, $"WiFi接口初始化失败 (尝试 {retryCount}/{maxRetries})");
                    
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(INIT_RETRY_INTERVAL_SECONDS * 1000);
                    }
                }
            }

            _logger.LogError("WiFi接口初始化失败，已达到最大重试次数");
            IsWifiEnabled = false;
        }

        /// <summary>
        /// 确保dhcpcd服务已停止并禁用
        /// </summary>
        private async Task EnsureDhcpcdStoppedAsync()
        {
            // 检查dhcpcd服务是否运行
            var serviceResult = await ExecuteCommandAsync("systemctl is-active dhcpcd");
            if (serviceResult.Success && "active".Equals(serviceResult.Output))
            {
                _logger.LogInformation("检测到dhcpcd服务正在运行，正在停止并禁用...");
                
                // 停止dhcpcd服务
                var stopResult = await ExecuteCommandAsync("sudo systemctl stop dhcpcd");
                if (!stopResult.Success)
                {
                    _logger.LogWarning($"停止dhcpcd服务失败: {stopResult.Error}");
                }
                else
                {
                    _logger.LogInformation("dhcpcd服务已停止");
                }
                
                // 等待服务停止
                await Task.Delay(1000);
                
                // 禁用dhcpcd服务（防止开机自启）
                var disableResult = await ExecuteCommandAsync("sudo systemctl disable dhcpcd");
                if (!disableResult.Success)
                {
                    _logger.LogWarning($"禁用dhcpcd服务失败: {disableResult.Error}");
                }
                else
                {
                    _logger.LogInformation("dhcpcd服务已禁用");
                }
            }
            else
            {
                _logger.LogDebug("dhcpcd服务未运行或不存在");
            }
        }

        /// <summary>
        /// 确保NetworkManager服务运行
        /// </summary>
        private async Task EnsureNetworkManagerRunningAsync()
        {
            var serviceResult = await ExecuteCommandAsync("systemctl is-active NetworkManager");
            if (!serviceResult.Success || !"active".Equals(serviceResult.Output))
            {
                _logger.LogWarning("NetworkManager服务未运行，尝试启动...");
                
                var startResult = await ExecuteCommandAsync("sudo systemctl start NetworkManager");
                if (!startResult.Success)
                {
                    throw new InvalidOperationException($"NetworkManager启动失败: {startResult.Error}");
                }
                
                //// 等待服务启动
                //await Task.Delay(3000);
                
                //// 验证服务状态
                //var verifyResult = await ExecuteCommandAsync("systemctl is-active NetworkManager");
                //if (!verifyResult.Success || !verifyResult.Output.Contains("active"))
                //{
                //    throw new InvalidOperationException("NetworkManager无法正常启动");
                //}
                
                _logger.LogInformation("NetworkManager服务已成功启动");
            }
            
            // 确保NetworkManager已启用（开机自启）
            var enabledResult = await ExecuteCommandAsync("systemctl is-enabled NetworkManager");
            if (!enabledResult.Success || !enabledResult.Output.Contains("enabled"))
            {
                _logger.LogInformation("NetworkManager服务未启用，正在启用...");
                
                var enableResult = await ExecuteCommandAsync("sudo systemctl enable NetworkManager");
                if (!enableResult.Success)
                {
                    _logger.LogWarning($"启用NetworkManager服务失败: {enableResult.Error}");
                }
                else
                {
                    _logger.LogInformation("NetworkManager服务已启用");
                }
            }
        }

        /// <summary>
        /// 获取WiFi接口名称
        /// </summary>
        private async Task<string> GetWifiInterfaceName()
        {
            // 使用nmcli获取WiFi设备状态
            var deviceResult = await ExecuteCommandAsync("nmcli device status | grep wifi");
            if (deviceResult.Success && !string.IsNullOrEmpty(deviceResult.Output))
            {
                var lines = deviceResult.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            WifiInterfaceName = parts[0].Trim();
                            _logger.LogInformation($"通过nmcli检测到WiFi接口: {WifiInterfaceName}");
                            break;
                        }
                    }
                }
            }
            
            // 如果nmcli未找到，回退到原有方法
            if (string.IsNullOrEmpty(WifiInterfaceName))
            {
                _logger.LogWarning("nmcli未检测到WiFi网口名");
            }
            return WifiInterfaceName;
        }

        /// <summary>
        /// 检测WiFi接口
        /// </summary>
        private async Task DetectWifiInterfaceAsync()
        {
            string wifiInterfaceName = await GetWifiInterfaceName();
            if (!string.IsNullOrEmpty(wifiInterfaceName))
            {
                WifiInterfaceName = wifiInterfaceName;
            }else{
                // 检查物理网络接口
                var interfaceResult = await ExecuteCommandAsync("ip link show | grep -E 'wlan|wlp|wlP1p1s'");
                if (!interfaceResult.Success || string.IsNullOrEmpty(interfaceResult.Output))
                {
                    // 检查无线硬件
                    var hwResult = await ExecuteCommandAsync("lsusb | grep -i wireless || iwconfig 2>/dev/null | grep -v 'no wireless extensions'");
                    if (hwResult.Success && !string.IsNullOrEmpty(hwResult.Output))
                    {
                        _logger.LogInformation($"检测到无线硬件: {hwResult.Output}");
                    }
                    
                    throw new InvalidOperationException("未检测到WiFi网络接口");
                }

                // 提取接口名称
                var lines = interfaceResult.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("wlan") || line.Contains("wlp") || line.Contains("wlP1p1s"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            WifiInterfaceName = parts[1].Trim().Split(' ')[0];
                            _logger.LogInformation($"检测到WiFi接口: {WifiInterfaceName}");
                            break;
                        }
                    }
                }
            }
            // 启动网络接口
            await ExecuteCommandAsync($"sudo ifconfig {WifiInterfaceName} up");
        }

        /// <summary>
        /// 启用WiFi功能
        /// </summary>
        private async Task EnableWifiAsync()
        {
            // 重启NetworkManager以重新检测接口
            await ExecuteCommandAsync("sudo systemctl restart NetworkManager");
            await Task.Delay(5000);

            // 启用WiFi无线电
            var radioResult = await ExecuteCommandAsync("sudo nmcli radio wifi on");
            if (!radioResult.Success)
            {
                _logger.LogWarning($"启用WiFi无线电失败: {radioResult.Error}");
            }

            await Task.Delay(2000);

            // 验证WiFi接口状态
            var statusResult = await ExecuteCommandAsync("sudo nmcli device status | grep wifi");
            if (!statusResult.Success || string.IsNullOrEmpty(statusResult.Output))
            {
                throw new InvalidOperationException("NetworkManager无法识别WiFi接口");
            }

            _logger.LogInformation($"WiFi接口状态: {statusResult.Output}");
        }

        /// <summary>
        /// 开始周期性扫描
        /// </summary>
        private async Task StartPeriodicScanAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"开始周期性WiFi扫描，间隔: {SCAN_INTERVAL_SECONDS}秒");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (IsWifiEnabled)
                    {
                        // 检查热点是否激活，如果激活则停止扫描以确保热点稳定
                        if (WifiManagerService.IsHotspotActive)
                        {
                            _logger.LogInformation("[WiFi后台] 热点模式已激活，暂停后台扫描以保持连接稳定");
                        }
                        else
                        {
                            await ScanWifiNetworksAsync();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("WiFi未启用，跳过扫描");
                        // 尝试重新初始化
                        await InitializeWifiInterfaceAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WiFi扫描过程中发生错误");
                    IsWifiEnabled = false;
                }

                // 等待下次扫描
                await Task.Delay(SCAN_INTERVAL_SECONDS * 1000, stoppingToken);
            }
        }

        /// <summary>
        /// 扫描WiFi网络
        /// </summary>
        private async Task ScanWifiNetworksAsync()
        {
            try
            {
                _logger.LogDebug("开始扫描WiFi网络...");

                // 执行扫描命令
                var scanResult = await ExecuteCommandAsync("sudo nmcli device wifi rescan");
                if (!scanResult.Success)
                {
                    _logger.LogWarning($"WiFi扫描命令失败: {scanResult.Error}");
                }

                // 等待扫描完成
                await Task.Delay(3000);

                // 直接获取SSID列表
                var ssidResult = await ExecuteCommandAsync("sudo nmcli -t -f SSID dev wifi list");
                if (!ssidResult.Success || string.IsNullOrEmpty(ssidResult.Output))
                {
                    _logger.LogWarning("无法获取WiFi SSID列表");
                    return;
                }

                // 解析SSID列表
                var networks = new List<WifiNetworkInfo>();
                var lines = ssidResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    try
                    {
                        var ssid = line.Trim();
                        
                        // 过滤有效的SSID
                        if (!string.IsNullOrEmpty(ssid) && ssid != "--" && !string.IsNullOrWhiteSpace(ssid))
                        {
                            // 避免重复添加相同的SSID
                            if (!networks.Any(n => n.SSID == ssid))
                            {
                                networks.Add(new WifiNetworkInfo
                                {
                                    SSID = ssid,
                                    BSSID = "",
                                    Signal = 0,
                                    Security = "Unknown",
                                    Frequency = "",
                                    LastSeen = DateTime.Now
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, $"解析WiFi SSID失败: {line}");
                    }
                }
                
                // 更新静态变量 - 增量更新而不是全量重建
                var currentSSIDs = networks.Select(n => n.SSID).ToHashSet();
                
                // 1. 添加或更新存在的网络
                foreach (var network in networks)
                {
                    AvailableNetworks.AddOrUpdate(network.SSID, network, (key, oldValue) => network);
                }
                
                // 2. 删除不再存在的网络
                var keysToRemove = AvailableNetworks.Keys.Where(ssid => !currentSSIDs.Contains(ssid)).ToList();
                foreach (var key in keysToRemove)
                {
                    AvailableNetworks.TryRemove(key, out _);
                }
                
                _logger.LogDebug($"WiFi网络更新：添加/更新 {networks.Count} 个，删除 {keysToRemove.Count} 个");

                LastScanTime = DateTime.Now;
                _logger.LogInformation($"WiFi扫描完成，发现 {networks.Count} 个网络");
                
                // 输出调试信息
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    foreach (var network in networks.Take(5)) // 只显示前5个
                    {
                        _logger.LogDebug($"  SSID: {network.SSID}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WiFi网络扫描失败");
            }
        }



        /// <summary>
        /// 执行系统命令
        /// </summary>
        private async Task<CommandResult> ExecuteCommandAsync(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                processInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
                processInfo.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "/root";

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return new CommandResult { Success = false, Error = "无法启动进程" };
                }

                await process.WaitForExitAsync();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                return new CommandResult
                {
                    Success = process.ExitCode == 0,
                    Output = output?.Trim(),
                    Error = error?.Trim(),
                    ExitCode = process.ExitCode
                };
            }
            catch (Exception ex)
            {
                return new CommandResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// 检查指定SSID是否可用
        /// </summary>
        public static bool IsNetworkAvailable(string ssid)
        {
            if (!IsWifiEnabled || string.IsNullOrEmpty(ssid))
                return false;

            return AvailableNetworks.ContainsKey(ssid);
        }

        /// <summary>
        /// 获取指定SSID的网络信息
        /// </summary>
        public static WifiNetworkInfo GetNetworkInfo(string ssid)
        {
            if (!IsWifiEnabled || string.IsNullOrEmpty(ssid))
                return null;

            AvailableNetworks.TryGetValue(ssid, out var networkInfo);
            return networkInfo;
        }

        /// <summary>
        /// 获取所有可用网络
        /// </summary>
        public static List<WifiNetworkInfo> GetAllAvailableNetworks()
        {
            return AvailableNetworks.Values.OrderByDescending(n => n.Signal).ToList();
        }

        /// <summary>
        /// 命令执行结果
        /// </summary>
        public class CommandResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public int ExitCode { get; set; }
        }
    }
} 