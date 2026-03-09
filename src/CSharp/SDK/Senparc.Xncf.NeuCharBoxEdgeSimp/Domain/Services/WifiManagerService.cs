using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Senparc.Ncf.Core.Exceptions;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services
{
    /// <summary>
    /// 统一的 WiFi 管理服务
    /// 负责：WiFi 连接、断开、热点管理、配网等功能
    /// </summary>
    public class WifiManagerService
    {
        private readonly ILogger<WifiManagerService> _logger;
        private readonly SenderReceiverSet _senderReceiverSet;

        // 热点状态
        public static bool IsHotspotActive { get; private set; } = false;
        public static string HotspotSSID { get; private set; }

        // 互斥锁，防止蓝牙配网和热点配网同时操作 WiFi
        private static readonly SemaphoreSlim _wifiOperationLock = new SemaphoreSlim(1, 1);

        public WifiManagerService(
            ILogger<WifiManagerService> logger,
            SenderReceiverSet senderReceiverSet)
        {
            _logger = logger;
            _senderReceiverSet = senderReceiverSet;
        }

        /// <summary>
        /// 启动时的初始化清理，防止断电重启后残留的热点配置影响正常 WiFi 连接
        /// </summary>
        public async Task InitialCleanupAsync()
        {
            _logger.LogInformation("[WiFi管理] 正在执行启动初始化清理...");
            try
            {
                // 1. 清理 iptables 规则（强制门户）
                await ExecuteCommandAsync("sudo iptables -t nat -F");
                await ExecuteCommandAsync("sudo iptables -F");

                // 2. 停止可能存在的 dnsmasq 进程
                await ExecuteCommandAsync("sudo pkill dnsmasq || true");
                if (File.Exists("/tmp/dnsmasq-captive.pid")) File.Delete("/tmp/dnsmasq-captive.pid");

                // 3. 查找并断开所有以 NCBEdge_ 开头的活跃连接（热点）
                // 使用 nmcli 查找活跃的 WiFi 热点连接并关闭
                await ExecuteCommandAsync("sudo nmcli -t -f NAME,TYPE connection show --active | grep ':802-11-wireless' | grep 'NCBEdge_' | cut -d: -f1 | xargs -I {} sudo nmcli connection down \"{}\" 2>/dev/null || true");

                // 4. 确保无线网卡处于客户端模式（如果它被卡在 AP 模式）
                await ExecuteCommandAsync($"sudo nmcli device set {WifiBackgroundService.WifiInterfaceName} managed yes 2>/dev/null || true");

                _logger.LogInformation("[WiFi管理] 启动初始化清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WiFi管理] 启动初始化清理过程中出现异常（非致命）");
            }
        }

        /// <summary>
        /// 连接到 WiFi 网络（带互斥锁保护）
        /// </summary>
        public async Task<(bool Success, string Message)> ConnectToWifiAsync(string ssid, string password, string ncbIp, bool onlyChange = false)
        {
            _logger.LogInformation($"[WiFi管理] 开始连接WiFi: {ssid}");
            // 尝试获取锁，最多等待30秒
            if (!await _wifiOperationLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.LogWarning("[WiFi管理] 获取WiFi操作锁超时，可能有其他操作正在进行");
                return (false, "系统繁忙，请稍后重试");
            }

            bool lastIsHotspotActive = IsHotspotActive;

            try
            {
                _logger.LogInformation($"[WiFi管理] 开始连接WiFi: {ssid}");

                // 0. 将所有已有的 WiFi 连接优先级降低，确保后续连接是最高优先级
                // 使用 -t (terse) 和 -f (fields) 来安全地获取所有类型为 802-11-wireless 的连接名称
                await ExecuteCommandAsync("sudo nmcli -t -f NAME,TYPE connection show | grep ':802-11-wireless' | cut -d: -f1 | xargs -I {} sudo nmcli connection modify \"{}\" connection.autoconnect-priority 0 2>/dev/null || true");

                // 1. 如果热点模式激活，先关闭热点
                if (IsHotspotActive)
                {
                    _logger.LogInformation("[WiFi管理] 检测到热点模式激活，正在关闭热点...");
                    await StopHotspotAsync();
                }

                // 2. 验证 NCBIP 格式
                if (string.IsNullOrWhiteSpace(ncbIp))
                {
                    throw new NcfExceptionBase("NCBIP 不能为空");
                }

                if (!System.Net.IPAddress.TryParse(ncbIp, out var ipAddress))
                {
                    throw new NcfExceptionBase($"NCBIP 格式错误: {ncbIp}");
                }

                // 3. 检查 WiFi 功能是否启用
                if (!WifiBackgroundService.IsWifiEnabled)
                {
                    throw new NcfExceptionBase("WiFi功能未启用或未初始化");
                }

                // 4. 检查目标SSID是否在扫描结果中
                //if (!WifiBackgroundService.IsNetworkAvailable(ssid))
                //{
                //    _logger.LogWarning($"WiFi网络 '{ssid}' 未在扫描结果中找到");

                //    // 显示可用网络列表供调试
                //    var availableNetworks = WifiBackgroundService.GetAllAvailableNetworks();
                //    if (availableNetworks.Any())
                //    {
                //        _logger.LogInformation($"当前可用的WiFi网络 ({availableNetworks.Count}个):");
                //        foreach (var network in availableNetworks.Take(10))
                //        {
                //            _logger.LogInformation($"  SSID: {network.SSID}, 信号: {network.Signal}dBm, 安全: {network.Security}");
                //        }
                //    }

                //    throw new NcfExceptionBase($"未找到WiFi网络 '{ssid}'，请检查SSID是否正确或网络是否在范围内");
                //}

                //// 5. 获取网络信息
                //var networkInfo = WifiBackgroundService.GetNetworkInfo(ssid);
                //_logger.LogInformation($"找到目标WiFi网络: {networkInfo.SSID}, 信号强度: {networkInfo.Signal}dBm, 安全类型: {networkInfo.Security}");

                // 6. 处理已存在的连接配置
                bool skipCreation = false;
                if (string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogInformation($"[WiFi管理] 未提供密码，检查是否存在已保存的配置: {ssid}");
                    var checkResult = await ExecuteCommandAsync($"nmcli connection show '{ssid}'");
                    if (checkResult.Success)
                    {
                        _logger.LogInformation($"[WiFi管理] 检测到已存在 WiFi 配置 '{ssid}'，尝试直接激活...");

                        // 先提升该配置的优先级
                        await ExecuteCommandAsync($"sudo nmcli connection modify '{ssid}' connection.autoconnect-priority 100 2>/dev/null || true");

                        var upResult = await ExecuteCommandAsync($"sudo nmcli connection up '{ssid}'");
                        if (upResult.Success)
                        {
                            _logger.LogInformation($"[WiFi管理] 成功激活现有 WiFi 配置: {ssid}");
                            skipCreation = true;
                        }
                        else
                        {
                            if (onlyChange)
                            {
                                throw new NcfExceptionBase($"激活现有 WiFi 配置'{ssid}'失败: {upResult?.Error}");
                            }
                            _logger.LogWarning($"[WiFi管理] 激活现有配置失败: {upResult.Error}，将尝试重新创建（作为开放网络）");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"[WiFi管理] 未找到已保存的配置: {ssid}");
                        if (onlyChange)
                        {
                            throw new NcfExceptionBase($"未查询到已经配置的网络'{ssid}'");
                        }
                    }
                }

                if (!skipCreation)
                {
                    // 7. 删除可能存在的同名连接配置（如果是新密码或需要重建）
                    await ExecuteCommandAsync($"sudo nmcli connection delete '{ssid}' 2>/dev/null || true");

                    // 8. 创建新的WiFi连接（使用connection add方式，支持自动重连）
                    string addConnectionCommand;
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        // 有密码的WiFi网络
                        addConnectionCommand = $"sudo nmcli connection add type wifi con-name '{ssid}' ifname {WifiBackgroundService.WifiInterfaceName} ssid '{ssid}' wifi-sec.key-mgmt wpa-psk wifi-sec.psk '{password}' connection.autoconnect yes connection.autoconnect-priority 100";
                    }
                    else
                    {
                        // 开放WiFi网络
                        addConnectionCommand = $"sudo nmcli connection add type wifi con-name '{ssid}' ifname {WifiBackgroundService.WifiInterfaceName} ssid '{ssid}' connection.autoconnect yes connection.autoconnect-priority 100";
                    }

                    _logger.LogInformation("创建WiFi连接配置...");
                    var addResult = await ExecuteCommandAsync(addConnectionCommand);
                    if (!addResult.Success)
                    {
                        _logger.LogWarning($"创建连接配置失败，尝试直接连接: {addResult.Error}");

                        // 备用方案：直接连接
                        string directConnectCommand;
                        if (!string.IsNullOrWhiteSpace(password))
                        {
                            directConnectCommand = $"sudo nmcli device wifi connect '{ssid}' password '{password}'";
                        }
                        else
                        {
                            directConnectCommand = $"sudo nmcli device wifi connect '{ssid}'";
                        }

                        var connectResult = await ExecuteCommandAsync(directConnectCommand);
                        if (!connectResult.Success)
                        {
                            throw new NcfExceptionBase($"WiFi连接失败: {connectResult.Error}");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("WiFi连接配置创建成功，正在连接...");

                        // 激活连接
                        var upResult = await ExecuteCommandAsync($"sudo nmcli connection up '{ssid}'");
                        if (!upResult.Success)
                        {
                            throw new NcfExceptionBase($"WiFi连接激活失败: {upResult.Error}");
                        }
                    }
                }

                // 8. 等待连接建立
                await Task.Delay(3000);

                // 9. 验证连接状态
                var statusResult = await ExecuteCommandAsync("nmcli -t -f WIFI g");
                if (!statusResult.Success || !statusResult.Output.Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NcfExceptionBase("WiFi连接验证失败，WiFi未启用");
                }

                // 获取连接的WiFi信息
                var wifiInfoResult = await ExecuteCommandAsync("nmcli -t -f active,ssid dev wifi | egrep '^yes' | cut -d: -f2");
                if (wifiInfoResult.Success)
                {
                    if (string.IsNullOrEmpty(wifiInfoResult.Output.Trim()))
                    {
                        _logger.LogInformation($"WiFi连接验证，命令1输出空，使用命令2");
                        var wifiInfoResult2 = await ExecuteCommandAsync("iwgetid -r");
                        if (!wifiInfoResult2.Success || !wifiInfoResult2.Output.Trim().Equals(ssid, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new NcfExceptionBase($"WiFi连接验证失败，当前连接的网络不是 {ssid}");
                        }
                    }
                    else
                    {
                        if (!wifiInfoResult.Output.Trim().Equals(ssid, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new NcfExceptionBase($"WiFi连接验证失败，当前连接的网络不是 {ssid}");
                        }
                    }
                    _logger.LogInformation($"WiFi连接成功: {ssid}");
                }

                // 10. 尝试 ping NCBIP 地址测试连通性（带重试机制）
                bool pingSuccess = await PingNCBIPAsync(ncbIp, maxRetries: 10, retryDelayMs: 1000);

                if (!pingSuccess)
                {
                    throw new NcfExceptionBase($"无法连接到NCBIP地址 {ncbIp}，请检查网络或IP地址");
                }

                // 11. 保存NCBIP到配置文件
                await SaveNCBIPToConfigAsync(ncbIp);

                _logger.LogInformation($"[WiFi管理] WiFi连接并验证成功: {ssid} -> {ncbIp}");
                return (true, "WiFi连接成功");
            }
            catch (NcfExceptionBase ex)
            {
                _logger.LogError(ex, $"[WiFi管理] 连接WiFi失败: {ex.Message}");

                if (lastIsHotspotActive)
                {
                    // 🔴 配网失败，重新启动热点以便用户继续配网
                    _logger.LogInformation("[WiFi管理] 配网失败，正在重新启动热点以便用户继续配网...");
                    try
                    {
                        var (hotspotSuccess, hotspotMessage) = await StartHotspotAsync();
                        if (hotspotSuccess)
                        {
                            _logger.LogInformation($"[WiFi管理] 热点已重新启动: {hotspotMessage}");
                        }
                        else
                        {
                            _logger.LogWarning($"[WiFi管理] 热点重启失败: {hotspotMessage}");
                        }
                    }
                    catch (Exception hotspotEx)
                    {
                        _logger.LogError(hotspotEx, "[WiFi管理] 重启热点时发生异常");
                    }
                }

                return (false, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[WiFi管理] 连接WiFi时发生异常");

                if (lastIsHotspotActive)
                {
                    // 🔴 配网失败，重新启动热点以便用户继续配网
                    _logger.LogInformation("[WiFi管理] 配网异常，正在重新启动热点以便用户继续配网...");
                    try
                    {
                        var (hotspotSuccess, hotspotMessage) = await StartHotspotAsync();
                        if (hotspotSuccess)
                        {
                            _logger.LogInformation($"[WiFi管理] 热点已重新启动: {hotspotMessage}");
                        }
                        else
                        {
                            _logger.LogWarning($"[WiFi管理] 热点重启失败: {hotspotMessage}");
                        }
                    }
                    catch (Exception hotspotEx)
                    {
                        _logger.LogError(hotspotEx, "[WiFi管理] 重启热点时发生异常");
                    }
                }

                return (false, $"连接WiFi失败: {ex.Message}");
            }
            finally
            {
                _wifiOperationLock.Release();
            }
        }

        /// <summary>
        /// Ping NCBIP地址（带重试）
        /// </summary>
        private async Task<bool> PingNCBIPAsync(string ncbIp, int maxRetries = 10, int retryDelayMs = 1000)
        {
            bool pingSuccess = false;
            Exception lastPingException = null;

            if (!System.Net.IPAddress.TryParse(ncbIp, out var ipAddress))
            {
                _logger.LogError($"NCBIP地址格式错误: {ncbIp}");
                return false;
            }

            using (var ping = new Ping())
            {
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        _logger.LogInformation($"第{attempt}次尝试ping NCBIP地址: {ncbIp}");

                        var reply = await ping.SendPingAsync(ipAddress, 2000); // 2秒超时
                        if (reply.Status == IPStatus.Success)
                        {
                            _logger.LogInformation($"NCBIP地址 {ncbIp} 连通性验证成功，响应时间: {reply.RoundtripTime}ms (第{attempt}次尝试)");
                            pingSuccess = true;
                            break;
                        }
                        else
                        {
                            _logger.LogWarning($"第{attempt}次ping失败: {ncbIp}, 状态: {reply.Status}");
                            lastPingException = new Exception($"Ping状态: {reply.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"第{attempt}次ping异常: {ncbIp}, 错误: {ex.Message}");
                        lastPingException = ex;
                    }

                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

            return pingSuccess;
        }

        /// <summary>
        /// 保存NCBIP到配置文件
        /// </summary>
        public async Task SaveNCBIPToConfigAsync(string ncbIp)
        {
            var appsettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(appsettingsPath))
            {
                _logger.LogWarning("appsettings.json文件不存在，无法保存NCBIP配置");
                return;
            }

            var json = await File.ReadAllTextAsync(appsettingsPath);
            var config = JsonConvert.DeserializeObject<dynamic>(json);

            // 确保SenderReceiverSet节点存在
            if (config.SenderReceiverSet == null)
            {
                config.SenderReceiverSet = new Newtonsoft.Json.Linq.JObject();
            }

            // 更新NCBIP值
            config.SenderReceiverSet.NCBIP = ncbIp;

            // 写回配置文件
            var updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
            await File.WriteAllTextAsync(appsettingsPath, updatedJson);

            // 更新内存中的配置对象
            _senderReceiverSet.NCBIP = ncbIp;

            _logger.LogInformation($"[配网成功] 已将NCBIP {ncbIp} 保存到配置文件和内存");

            // 通知 Register.Thread 立即强制重连
            try
            {
                var registerType = typeof(Senparc.Xncf.NeuCharBoxEdgeSimp.Register);
                var forceReconnectField = registerType.GetField("_forceReconnectSignal",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (forceReconnectField != null)
                {
                    forceReconnectField.SetValue(null, true);
                    _logger.LogInformation($"[配网成功] 已发送强制重连信号，SignalR将立即重新连接");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[配网成功] 发送强制重连信号失败，将等待下次循环检测");
            }
        }

        /// <summary>
        /// 启动热点模式（带互斥锁保护）
        /// </summary>
        public async Task<(bool Success, string Message)> StartHotspotAsync(string ssid = null, string password = "12345678")
        {
            // 尝试获取锁，最多等待30秒
            if (!await _wifiOperationLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.LogWarning("[热点] 获取WiFi操作锁超时，可能有其他操作正在进行");
                return (false, "系统繁忙，请稍后重试");
            }

            try
            {
                if (IsHotspotActive)
                {
                    _logger.LogWarning("[热点] 热点已经在运行中");
                    return (true, $"热点已激活: {HotspotSSID}");
                }

                // 生成热点SSID
                if (string.IsNullOrEmpty(ssid))
                {
                    var did = _senderReceiverSet.dId ?? "DEFAULT";
                    var lastDigits = did.Length >= 6 ? did.Substring(did.Length - 6) : did.PadLeft(6, '0');
                    ssid = $"NCBEdge_{lastDigits}";
                }

                // 验证密码（WPA-PSK 要求 8-63 个字符）
                if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 63)
                {
                    _logger.LogWarning($"[热点] 密码长度不符合要求，使用默认密码");
                    password = "12345678"; // 使用默认密码
                }

                _logger.LogInformation($"[热点] 正在启动热点: {ssid}");

                // 1. 停止现有的WiFi连接
                var disconnectResult = await ExecuteCommandAsync("sudo nmcli device disconnect wlan0 2>/dev/null || true");
                await Task.Delay(1000);

                // 2. 删除可能存在的同名热点配置
                await ExecuteCommandAsync($"sudo nmcli connection delete '{ssid}' 2>/dev/null || true");
                await Task.Delay(500);

                // 3. 创建热点配置
                var createHotspotCommand = $"sudo nmcli connection add type wifi ifname {WifiBackgroundService.WifiInterfaceName} con-name '{ssid}' autoconnect no ssid '{ssid}' " +
                    $"802-11-wireless.mode ap 802-11-wireless.band bg ipv4.method shared ipv6.method shared " +
                    $"wifi-sec.key-mgmt wpa-psk wifi-sec.psk '{password}'";

                var createResult = await ExecuteCommandAsync(createHotspotCommand);
                if (!createResult.Success)
                {
                    throw new Exception($"创建热点配置失败: {createResult.Error}");
                }

                _logger.LogInformation("[热点] 热点配置创建成功");
                await Task.Delay(1000);

                // 4. 启动热点
                var upResult = await ExecuteCommandAsync($"sudo nmcli connection up '{ssid}'");
                if (!upResult.Success)
                {
                    throw new Exception($"启动热点失败: {upResult.Error}");
                }

                await Task.Delay(2000);

                // 5. 验证热点状态
                var verifyResult = await ExecuteCommandAsync($"nmcli connection show --active | grep '{ssid}'");
                if (!verifyResult.Success || string.IsNullOrEmpty(verifyResult.Output))
                {
                    throw new Exception("热点启动验证失败");
                }

                IsHotspotActive = true;
                HotspotSSID = ssid;

                // 🔴 6. 配置 Captive Portal (强制门户) - 自动跳转到配网页面
                await SetupCaptivePortalAsync();

                _logger.LogInformation($"[热点] 热点启动成功: {ssid}, 密码: {password}");
                _logger.LogInformation($"[热点] 配网地址: http://10.42.0.1:5000/provision (简化路由)");
                _logger.LogInformation($"[热点] Captive Portal 已配置，用户连接后将自动跳转");

                return (true, $"热点启动成功: {ssid}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[热点] 启动热点失败");
                IsHotspotActive = false;
                return (false, $"启动热点失败: {ex.Message}");
            }
            finally
            {
                _wifiOperationLock.Release();
            }
        }

        /// <summary>
        /// 停止热点模式（带互斥锁保护）
        /// </summary>
        public async Task<(bool Success, string Message)> StopHotspotAsync()
        {
            // 尝试获取锁，最多等待30秒
            if (!await _wifiOperationLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger.LogWarning("[热点] 获取WiFi操作锁超时，可能有其他操作正在进行");
                return (false, "系统繁忙，请稍后重试");
            }

            try
            {
                if (!IsHotspotActive)
                {
                    _logger.LogInformation("[热点] 热点未激活，无需停止");
                    return (true, "热点未激活");
                }

                _logger.LogInformation($"[热点] 正在停止热点: {HotspotSSID}");

                // 🔴 清理 Captive Portal 配置
                await CleanupCaptivePortalAsync();

                // 停止热点连接
                var downResult = await ExecuteCommandAsync($"sudo nmcli connection down '{HotspotSSID}' 2>/dev/null || true");
                await Task.Delay(1000);

                // 删除热点配置
                await ExecuteCommandAsync($"sudo nmcli connection delete '{HotspotSSID}' 2>/dev/null || true");

                IsHotspotActive = false;
                var oldSSID = HotspotSSID;
                HotspotSSID = null;

                _logger.LogInformation($"[热点] 热点已停止: {oldSSID}");
                return (true, "热点已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[热点] 停止热点失败");
                return (false, $"停止热点失败: {ex.Message}");
            }
            finally
            {
                _wifiOperationLock.Release();
            }
        }

        /// <summary>
        /// 配置 Captive Portal (强制门户) - 自动跳转配网页面
        /// </summary>
        private async Task SetupCaptivePortalAsync()
        {
            try
            {
                _logger.LogInformation("[Captive Portal] 开始配置强制门户...");

                // 1. 安装 iptables (如果未安装)
                await ExecuteCommandAsync("which iptables || sudo apt-get install -y iptables");

                // 2. 清理可能存在的旧规则
                await ExecuteCommandAsync("sudo iptables -t nat -F");
                await ExecuteCommandAsync("sudo iptables -F");

                // 3. 设置 iptables 规则 - 重定向所有 HTTP 请求到配网页面
                // 允许访问本地服务器
                await ExecuteCommandAsync("sudo iptables -A INPUT -p tcp --dport 5000 -j ACCEPT");

                // 重定向所有 HTTP (80端口) 请求到本地 5000 端口
                await ExecuteCommandAsync("sudo iptables -t nat -A PREROUTING -p tcp --dport 80 -j REDIRECT --to-port 5000");

                // 重定向所有 HTTPS (443端口) 请求到本地 5000 端口
                await ExecuteCommandAsync("sudo iptables -t nat -A PREROUTING -p tcp --dport 443 -j REDIRECT --to-port 5000");

                // 允许 DNS 查询
                await ExecuteCommandAsync("sudo iptables -A INPUT -p udp --dport 53 -j ACCEPT");
                await ExecuteCommandAsync("sudo iptables -A INPUT -p tcp --dport 53 -j ACCEPT");

                _logger.LogInformation("[Captive Portal] iptables 规则配置成功");

                // 4. 创建 DNS 劫持配置 (可选，增强兼容性)
                await SetupDnsRedirectAsync();

                _logger.LogInformation("[Captive Portal] 强制门户配置完成");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Captive Portal] 配置强制门户失败，但不影响热点功能");
            }
        }

        /// <summary>
        /// 配置 DNS 重定向 (增强 Captive Portal 兼容性)
        /// </summary>
        private async Task SetupDnsRedirectAsync()
        {
            try
            {
                _logger.LogDebug("[DNS Redirect] 配置 DNS 重定向...");

                // 检查 dnsmasq 是否安装
                var dnsmasqCheck = await ExecuteCommandAsync("which dnsmasq");
                if (!dnsmasqCheck.Success)
                {
                    _logger.LogDebug("[DNS Redirect] dnsmasq 未安装，跳过 DNS 重定向配置");
                    return;
                }

                // 创建 dnsmasq 配置文件
                var dnsmasqConfig = @"
# Captive Portal DNS Configuration
interface=wlan0
dhcp-range=192.168.42.50,192.168.42.150,12h
address=/#/192.168.42.1
";

                var configPath = "/tmp/dnsmasq-captive.conf";
                await File.WriteAllTextAsync(configPath, dnsmasqConfig);

                // 启动 dnsmasq
                await ExecuteCommandAsync($"sudo dnsmasq -C {configPath} --pid-file=/tmp/dnsmasq-captive.pid 2>/dev/null || true");

                _logger.LogDebug("[DNS Redirect] DNS 重定向配置成功");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[DNS Redirect] DNS 重定向配置失败（非关键功能）");
            }
        }

        /// <summary>
        /// 清理 Captive Portal 配置
        /// </summary>
        private async Task CleanupCaptivePortalAsync()
        {
            try
            {
                _logger.LogInformation("[Captive Portal] 清理强制门户配置...");

                // 1. 清理 iptables 规则
                await ExecuteCommandAsync("sudo iptables -t nat -F");
                await ExecuteCommandAsync("sudo iptables -F");

                _logger.LogDebug("[Captive Portal] iptables 规则已清理");

                // 2. 停止 dnsmasq (如果运行)
                var pidFile = "/tmp/dnsmasq-captive.pid";
                if (File.Exists(pidFile))
                {
                    var pid = await File.ReadAllTextAsync(pidFile);
                    await ExecuteCommandAsync($"sudo kill {pid.Trim()} 2>/dev/null || true");
                    File.Delete(pidFile);
                    _logger.LogDebug("[Captive Portal] dnsmasq 已停止");
                }

                // 3. 删除配置文件
                var configPath = "/tmp/dnsmasq-captive.conf";
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }

                _logger.LogInformation("[Captive Portal] 强制门户配置已清理");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Captive Portal] 清理强制门户配置失败");
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
                _logger.LogError(ex, $"执行命令失败: {command}");
                return new CommandResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// 命令执行结果
        /// </summary>
        private class CommandResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public int ExitCode { get; set; }
        }
    }
}

