using EdgeOTA;
using Microsoft.Extensions.Options;
using Senparc.Ncf.Core.AppServices;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Helper;
using Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices
{
    /// <summary>
    /// 边缘设备请求主设备
    /// </summary>
    [Obsolete]
    public class EdgeBackgroundService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly SenderReceiverSet _senderReceiverSet;
        private readonly IServiceProvider _serviceProvider;

        public EdgeBackgroundService(SenderReceiverSet senderReceiverSet, IServiceProvider serviceProvider)
        {
            _senderReceiverSet = senderReceiverSet;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine($"开始请求主设备");
                    // 执行你的代码
                    await KeepAlive();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("异常:" + ex.Message);
                }

                // 等待 
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                //await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        private async Task KeepAlive()
        {
            //// 获取网关地址 | Get gateway address
            //var gatewayAddress = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            //    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            //    .Where(n => n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            //    .SelectMany(n => n.GetIPProperties()?.GatewayAddresses)
            //    .Select(g => g?.Address)
            //    .FirstOrDefault(a => a != null)?.ToString();

            var gatewayAddress = IpHelper.GetGatewayAddress(_senderReceiverSet);

            if (string.IsNullOrWhiteSpace(gatewayAddress))
            {
                Console.WriteLine($"未获取到当前网关地址");
                return;
            }

            // 请求主机
            try
            {
                var url = $"{CenterDefinition.CenterHttp}://{gatewayAddress}:{CenterDefinition.CenterPort}/{_senderReceiverSet.KeepAliveApi.TrimStart('/')}";
                Console.WriteLine($"请求主设备地址: {url}");

                var (findOTAConfig, lstOTAConfigs) = await OTAHelper.GetOTAConfigAsync(_senderReceiverSet.uId, _senderReceiverSet.dId, OTAHelper.FirmwareType_Backend);

                var postData = new KeepAliveRequest() { did = _senderReceiverSet.dId, uid = _senderReceiverSet.uId, deciveName = _senderReceiverSet.deciveName, version = findOTAConfig?.CurrentVersion };
                var json = JsonSerializer.Serialize(postData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                //using (var httpClient = new HttpClient())
                //{
                    try
                    {
                        var response = await HttpClientHelper.httpClient.PostAsync(url, content);
                        response.EnsureSuccessStatusCode(); // 确保HTTP响应成功

                        var responseString = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<StringAppResponse>(responseString);

                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"HTTP请求失败: {ex.Message}");
                        throw;
                    }
                //}
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

    }
}
