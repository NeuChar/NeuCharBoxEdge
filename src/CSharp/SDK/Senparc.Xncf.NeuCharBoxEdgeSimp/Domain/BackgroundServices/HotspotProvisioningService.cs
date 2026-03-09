using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices
{
    /// <summary>
    /// 热点配网后台服务
    /// 功能：程序启动2分钟后检查NCB连接状态，如果未连接则开启热点模式进行配网
    /// </summary>
    public class HotspotProvisioningService : BackgroundService
    {
        private readonly ILogger<HotspotProvisioningService> _logger;
        private readonly WifiManagerService _wifiManagerService;
        private readonly SenderReceiverSet _senderReceiverSet;

        // 检查间隔（首次1.5分钟，后续每1.5分钟）
        private const int INITIAL_DELAY_SECONDS = 90; // 1.5分钟
        private const int CHECK_INTERVAL_SECONDS = 90; // 1.5分钟

        public HotspotProvisioningService(
            ILogger<HotspotProvisioningService> logger,
            WifiManagerService wifiManagerService,
            SenderReceiverSet senderReceiverSet)
        {
            _logger = logger;
            _wifiManagerService = wifiManagerService;
            _senderReceiverSet = senderReceiverSet;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 程序启动时先执行一次清理，确保系统处于纯净的 WiFi 客户端模式
            // 防止断电重启后 OS 层面残留的热点状态干扰正常 WiFi 连接
            await _wifiManagerService.InitialCleanupAsync();

            if (!(_senderReceiverSet.IsOpenAP ?? false))
            {
                return;
            }

            _logger.LogInformation("[热点配网] 热点配网服务启动中...");
            _logger.LogInformation($"[热点配网] 将在 {INITIAL_DELAY_SECONDS} 秒后首次检查 NCB 连接状态");

            try
            {
                // 首次等待2分钟
                await Task.Delay(TimeSpan.FromSeconds(INITIAL_DELAY_SECONDS), stoppingToken);

                // 首次检查
                await CheckAndStartHotspotIfNeededAsync();

                // 后续定期检查（每5分钟）
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS), stoppingToken);
                    await CheckAndStartHotspotIfNeededAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[热点配网] 热点配网服务已被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[热点配网] 热点配网服务执行失败");
            }
        }

        /// <summary>
        /// 检查 NCB 连接状态，如果未连接则启动热点
        /// </summary>
        private async Task CheckAndStartHotspotIfNeededAsync()
        {
            try
            {
                if (GetNCBNetInfoBackgroundService.CheckNoConnectNum <= 12)
                {
                    return;
                }

                _logger.LogInformation("[热点配网] 开始检查 NCB 连接状态...");

                // 获取 NCBConnection 状态（从 Register.Thread）
                var ncbConnection = Register.NCBConnection;
                bool isConnected = ncbConnection != null && ncbConnection.State == HubConnectionState.Connected;

                _logger.LogInformation($"[热点配网] NCB 连接状态: {(isConnected ? "已连接" : "未连接")}");

                if (!isConnected)
                {
                    _logger.LogWarning("[热点配网] 连续 2 次未连接，准备启动热点模式进行配网");

                    // 检查热点是否已经激活
                    if (WifiManagerService.IsHotspotActive)
                    {
                        _logger.LogInformation($"[热点配网] 热点已激活: {WifiManagerService.HotspotSSID}，无需重复启动");
                        return;
                    }

                    // 启动热点
                    var (success, message) = await _wifiManagerService.StartHotspotAsync();

                    if (success)
                    {
                        _logger.LogInformation($"[热点配网] 热点启动成功: {message}");
                        _logger.LogInformation($"[热点配网] 用户可连接热点 '{WifiManagerService.HotspotSSID}' (密码: 123456)");
                        _logger.LogInformation($"[热点配网] 配网页面: http://10.42.0.1:5000/provision");
                    }
                    else
                    {
                        _logger.LogError($"[热点配网] 热点启动失败: {message}");
                    }
                }
                else
                {
                    _logger.LogInformation("[热点配网] NCB 已连接，检查是否需要关闭热点");

                    // 如果已连接且热点激活，则关闭热点
                    if (WifiManagerService.IsHotspotActive)
                    {
                        _logger.LogInformation("[热点配网] NCB 已连接，正在关闭热点...");

                        var (success, message) = await _wifiManagerService.StopHotspotAsync();

                        if (success)
                        {
                            _logger.LogInformation($"[热点配网] 热点已关闭: {message}");
                        }
                        else
                        {
                            _logger.LogWarning($"[热点配网] 关闭热点失败: {message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[热点配网] 检查和启动热点时发生错误");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[热点配网] 热点配网服务正在停止...");

            // 停止热点（如果激活）
            if (WifiManagerService.IsHotspotActive)
            {
                _logger.LogInformation("[热点配网] 正在关闭热点...");
                await _wifiManagerService.StopHotspotAsync();
            }

            await base.StopAsync(cancellationToken);
        }
    }
}

