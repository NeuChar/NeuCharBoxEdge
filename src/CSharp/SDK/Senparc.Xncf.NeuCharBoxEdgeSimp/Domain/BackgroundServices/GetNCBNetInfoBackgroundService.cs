using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices;

/// <summary>
/// 边缘设备直接请求neuchar获取NCB网络信息
/// </summary>
public class GetNCBNetInfoBackgroundService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SenderReceiverSet _senderReceiverSet;

    public static long CheckNoConnectNum = 0;

    public GetNCBNetInfoBackgroundService(SenderReceiverSet senderReceiverSet, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _senderReceiverSet = senderReceiverSet;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 等待20秒
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

                await DoWork();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取NCB网络信息出错: {ex.Message}");
            }
            finally
            {
                CheckNoConnectNum++;
            }
        }
    }

    private async Task DoWork()
    {
        // string ip = IpHelper.GetGatewayAddress(_senderReceiverSet);
        // if (string.IsNullOrWhiteSpace(ip))
        // {
        //     Console.WriteLine("未获取到当前网关地址");
        //     return;
        // }
        // 获取 NCBConnection 状态（从 Register.Thread）
        var ncbConnection = Register.NCBConnection;
        bool isConnected = ncbConnection != null && ncbConnection.State == HubConnectionState.Connected;
        if (isConnected)
        {
            CheckNoConnectNum = 0;
            return;
        }

        string url = $"{_senderReceiverSet.NeuCharCom.TrimEnd('/')}/User/NcxBox/GetNCBNetInfo";
        // 检查更新
        var request = new NcxBox_DeviceVD
        {
            DID = _senderReceiverSet.dId.Trim(),
            UID = _senderReceiverSet.uId.Trim(),
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        string signStr = request.DID + request.UID + request.Time;
        Console.WriteLine("加密字符串：" + signStr);
        request.sign = CertHepler.RsaEncryptWithPrivateKey(signStr);

        var json = JsonConvert.SerializeObject(request);
        Console.WriteLine("获取NCB网络信息请求URL：" + url);
        Console.WriteLine("获取NCB网络信息请求报文：" + json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var response = await HttpClientHelper.httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode(); // 确保HTTP响应成功

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<BaseResponseVD<string>>(responseString);
            if (result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.Data))
                {
                    var jsonMsg = CertHepler.RsaDecryptWithPrivateKey(result.Data);
                    Console.WriteLine($"获取NCB网络信息：" + jsonMsg);
                    var wifiMsg = JsonConvert.DeserializeObject<WifiInfo>(jsonMsg);

                    if (wifiMsg != null && !string.IsNullOrWhiteSpace(wifiMsg.wifiName) && !string.IsNullOrWhiteSpace(wifiMsg.ipAddress))
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var wifiManagerService = scope.ServiceProvider.GetRequiredService<WifiManagerService>();

                            // 1. 判断 WiFi 名称是否一致，不一致则切换
                            var currentSsid = await GetCurrentSsidAsync();

                            if (!string.Equals(currentSsid, wifiMsg.wifiName, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"检测到 WiFi 变更: {currentSsid} -> {wifiMsg.wifiName}，准备切换...");
                                // 尝试连接新 WiFi。注意：此处由于接口未返回密码，传入 null，nmcli 将尝试使用已保存的凭据
                                await wifiManagerService.ConnectToWifiAsync(wifiMsg.wifiName, null, wifiMsg.ipAddress, true);
                            }
                            else
                            {
                                Console.WriteLine($"未检测到 WiFi 变更: {currentSsid}");

                                // 2. 判断 NCBIP 是否一致，不一致则更新
                                if (!string.Equals(_senderReceiverSet.NCBIP, wifiMsg.ipAddress, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine($"检测到 NCBIP 变更: {_senderReceiverSet.NCBIP} -> {wifiMsg.ipAddress}，正在保存新配置...");
                                    await wifiManagerService.SaveNCBIPToConfigAsync(wifiMsg.ipAddress);
                                }
                                else
                                {
                                    Console.WriteLine($"未检测到 NCBIP 变更: {_senderReceiverSet.NCBIP}");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"获取NCB网络信息失败: {result.Message}");
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP请求失败: {ex.Message}");
            throw;
        }

    }

    /// <summary>
    /// 获取当前连接的 SSID
    /// </summary>
    private async Task<string> GetCurrentSsidAsync()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"iwgetid -r\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取当前 SSID 失败: {ex.Message}");
            return string.Empty;
        }
    }
}

public class NcxBox_DeviceVD
{
    /// <summary>
    /// 设备UID
    /// </summary>
    public string UID { get; set; }
    /// <summary>
    /// 设备ID
    /// </summary>
    public string DID { get; set; }
    /// <summary>
    /// 时间
    /// </summary>
    public long Time { get; set; }
    /// <summary>
    /// 签名
    /// </summary>
    public string sign { get; set; }
}
public class BaseResponseVD<T>
{
    public bool Success { get; set; } = true;
    public string Message { get; set; }
    public T Data { get; set; }
}

public class WifiInfo
{
    public string wifiName { get; set; }
    public string ipAddress { get; set; }
}