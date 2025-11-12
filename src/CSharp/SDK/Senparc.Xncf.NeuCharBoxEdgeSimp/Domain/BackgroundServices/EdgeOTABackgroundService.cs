using EdgeOTA;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices
{
    /// <summary>
    /// 下属边缘设备检查OTA
    /// </summary>
    public class EdgeOTABackgroundService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SenderReceiverSet _senderReceiverSet;

        public EdgeOTABackgroundService(SenderReceiverSet senderReceiverSet, IServiceProvider serviceProvider)
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
                    Console.WriteLine($"开始请求主设备获取OTA升级信息");
                    await DoWork();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"检查更新出错: {ex.Message}");
                }

                // 等待60秒
                await Task.Delay(60000, stoppingToken);
            }
        }


        private async Task DoWork()
        {
            string ip = IpHelper.GetGatewayAddress(_senderReceiverSet);
            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("未获取到当前网关地址");
                return;
            }
            string url = $"{CenterDefinition.CenterHttp}://{ip}:{CenterDefinition.CenterPort}";
            string api = "/api/Senparc.Xncf.NeuCharBoxCenter/CenterAppService/Xncf.NeuCharBoxCenter_CenterAppService.GetOTAInfo";

            // 检查更新
            var request = new EdgeOTA.Request.GetRemoteVersionInfoRequest
            {
                FirmwareType = OTAHelper.FirmwareType_Backend,
                DID = _senderReceiverSet.dId,
                UID = _senderReceiverSet.uId,
                AppKey = _senderReceiverSet.dId,
                AppSecret = _senderReceiverSet.uId,
            };

            var getRemoteVersionInfoResult = await EdgeOTA.OTAHelper.GetRemoteVersionInfoAsync(
                url,
                api,
                request,
                CenterDefinition.token?.Token
            );
            if (getRemoteVersionInfoResult.Success)
            {
                Console.WriteLine($"获取远程版本信息成功: {getRemoteVersionInfoResult.Data}");
                var checkResult = await EdgeOTA.OTAHelper.CheckForUpdateAsync(
                    new EdgeOTA.Request.CheckForUpdateRequest()
                    {
                        DID = request.DID,
                        UID = request.UID,
                        FirmwareType = request.FirmwareType
                    }
                );
                if (checkResult.Success)
                {
                    Console.WriteLine($"检查更新结果: {JsonConvert.SerializeObject(checkResult.Data)}");
                    if (checkResult.Data.IsNeedUpdate)
                    {
                        var downloadResult = await EdgeOTA.OTAHelper.DownloadUpdateAsync(
                           new EdgeOTA.Request.DownloadUpdateRequest()
                           {
                               DID = request.DID,
                               UID = request.UID,
                               FirmwareType = request.FirmwareType,
                               BaseUrl = url
                           },
                           CenterDefinition.token?.Token
                        );
                        if (downloadResult.Success)
                        {
                            Console.WriteLine($"下载更新包成功: {downloadResult.Data}");
                            #region 获取当前进程名称和ID
                            // 获取当前进程名称和ID
                            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                            var processId = currentProcess.Id;
                            var processName = currentProcess.ProcessName;

                            // 获取实际运行的程序集名称
                            var entryAssembly = Assembly.GetEntryAssembly();
                            var entryAssemblyName = entryAssembly.GetName().Name + ".dll";

                            Console.WriteLine($"当前进程名称: {processName}，ID: {processId}");
                            Console.WriteLine($"实际程序集名称: {entryAssemblyName}");

                            // 启动EdgeOTA.dll进行更新
                            try
                            {
                                // 获取当前程序的工作目录，确保EdgeOTA在相同目录下执行
                                string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                                string edgeOTAPath = System.IO.Path.Combine(workingDirectory, $"{nameof(EdgeOTA)}.dll");
                                
                                // 获取 dotnet 可执行文件路径
                                string dotnetPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "dotnet";
                                Console.WriteLine($"Dotnet路径: {dotnetPath}");
                                
                                Console.WriteLine($"EdgeOTA工作目录: {workingDirectory}");
                                Console.WriteLine($"EdgeOTA完整路径: {edgeOTAPath}");
                                
                                // 使用 nohup 和 bash 来创建独立进程，避免父进程终止时影响子进程
                                // 将输出重定向到日志文件以便调试
                                string logFile = System.IO.Path.Combine(workingDirectory, "EdgeOTA_startup.log");
                                var startInfo = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "/bin/bash",
                                    Arguments = $"-c \"nohup '{dotnetPath}' '{edgeOTAPath}' -pid {processId} -n {entryAssemblyName} -did {request.DID} -uid {request.UID} -firmwareType {request.FirmwareType} >> '{logFile}' 2>&1 &\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WorkingDirectory = workingDirectory
                                };

                                Console.WriteLine($"启动命令: {startInfo.FileName} {startInfo.Arguments}");
                                
                                var process = System.Diagnostics.Process.Start(startInfo);
                                if (process != null)
                                    Console.WriteLine($"已启动{nameof(EdgeOTA)}进行更新");
                                else
                                {
                                    Console.WriteLine($"启动{nameof(EdgeOTA)}失败");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"启动{nameof(EdgeOTA)}时发生错误: {ex.Message}");
                            }
                            #endregion
                        }
                    }
                }
                else
                {
                    Console.WriteLine("检查更新失败:" + checkResult.Message);
                }
            }
            else
            {
                Console.WriteLine("获取远程版本信息失败:" + getRemoteVersionInfoResult.Message);
            }
        }

    }
}
