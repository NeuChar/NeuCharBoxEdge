using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Helper
{
    public class IpHelper
    {
        public static string LocalIp = "127.0.0.1";
        //public const string PORT = "5000";

        /// <summary>
        /// 更新当前设备IP
        /// </summary>
        /// <returns></returns>
        public static string UpdateLocalIp()
        {
            LocalIp = GetLocalIPv4();
            return LocalIp;
        }

        /// <summary>
        /// 查找可用的端口
        /// </summary>
        public static int FindAvailablePort(int startPort, int endPort)
        {
            for (int port = startPort; port <= endPort; port++)
            {
                bool isAvailable = true;
                System.Net.IPEndPoint[] endPoints = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                foreach (var endPoint in endPoints)
                {
                    if (endPoint.Port == port)
                    {
                        isAvailable = false;
                        break;
                    }
                }

                if (isAvailable)
                    return port;
            }

            throw new Exception($"在{startPort}-{endPort}范围内没有可用端口");
        }
        public static string GetLocalIPv4()
        {
            List<string> lstIp = new List<string>();

            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus == OperationalStatus.Up &&
                    netInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    foreach (UnicastIPAddressInformation ip in netInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipAddress = ip.Address.ToString();

                            // 只选取局域网 IP（排除 169.254.X.X 和 172.16-31.X.X）
                            if (ipAddress.StartsWith("192.168.") || ipAddress.StartsWith("10.") ||
                                (ipAddress.StartsWith("172.") && IsPrivate172(ipAddress)))
                            {
                                lstIp.Add(ipAddress);
                            }
                        }
                    }
                }
            }

            if (lstIp.Count == 0)
            {
                return "未找到局域网 IP";
            }
            else
            {
                if (lstIp.Any(t => t.StartsWith("192.168.")))
                {
                    return lstIp.FirstOrDefault(t => t.StartsWith("192.168."));
                }
                if (lstIp.Any(t => t.StartsWith("172.")))
                {
                    return lstIp.FirstOrDefault(t => t.StartsWith("172."));
                }
                if (lstIp.Any(t => t.StartsWith("10.")))
                {
                    return lstIp.FirstOrDefault(t => t.StartsWith("10."));
                }
                return lstIp.FirstOrDefault();
            }
        }
        static bool IsPrivate172(string ipAddress)
        {
            string[] parts = ipAddress.Split('.');
            if (parts.Length == 4 && int.TryParse(parts[1], out int secondOctet))
            {
                return secondOctet >= 16 && secondOctet <= 31;
            }
            return false;
        }


        /// <summary>
        /// 获取网关地址
        /// </summary>
        /// <returns></returns>
        public static string GetGatewayAddress(SenderReceiverSet senderReceiverSet)
        {
            // 获取网关地址 | Get gateway address
            var gatewayAddress = "";
            try {
                if(senderReceiverSet!=null && !string.IsNullOrWhiteSpace(senderReceiverSet.NCBIP)){

                    // // 检查是否连接到WiFi
                    // try
                    // {
                    //     var process = new System.Diagnostics.Process
                    //     {
                    //         StartInfo = new System.Diagnostics.ProcessStartInfo
                    //         {
                    //             FileName = "iwgetid",
                    //             Arguments = "-r",
                    //             RedirectStandardOutput = true,
                    //             UseShellExecute = false,
                    //             CreateNoWindow = true
                    //         }
                    //     };
                        
                    //     process.Start();
                    //     string wifiName = process.StandardOutput.ReadToEnd().Trim();
                    //     process.WaitForExit();
                        
                    //     if (string.IsNullOrWhiteSpace(wifiName))
                    //     {
                    //         Console.WriteLine("未连接到WiFi，无法使用预设的NCB IP地址");
                    //         return null;
                    //     }
                    //     else
                    //     {
                    //         //Console.WriteLine($"已连接到WiFi: {wifiName}，使用预设的NCB IP地址");
                    //     }
                    // }
                    // catch (Exception ex)
                    // {
                    //     Console.WriteLine($"检查WiFi连接失败: {ex.Message}");
                    // }
                    return senderReceiverSet.NCBIP;
                }

                gatewayAddress = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties()?.GatewayAddresses)
                .Select(g => g?.Address)
                .FirstOrDefault(a => a != null)?.ToString();
            }catch (Exception ex)
            {
                Console.WriteLine("获取网关失败"+ex.Message+ex.StackTrace);
            }

            return gatewayAddress;
        }
    }
}
