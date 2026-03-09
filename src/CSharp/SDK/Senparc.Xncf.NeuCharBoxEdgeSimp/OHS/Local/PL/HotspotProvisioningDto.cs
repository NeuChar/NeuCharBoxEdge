using System.ComponentModel.DataAnnotations;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL
{
    /// <summary>
    /// WiFi 网络信息 DTO
    /// </summary>
    public class WifiNetworkDto
    {
        /// <summary>
        /// WiFi 网络名称（SSID）
        /// </summary>
        public string SSID { get; set; }

        /// <summary>
        /// 信号强度（dBm）
        /// </summary>
        public int Signal { get; set; }

        /// <summary>
        /// 安全类型
        /// </summary>
        public string Security { get; set; }

        /// <summary>
        /// 频率
        /// </summary>
        public string Frequency { get; set; }
    }

    /// <summary>
    /// 连接 WiFi 请求参数
    /// </summary>
    public class ConnectWifiRequest
    {
        /// <summary>
        /// WiFi 网络名称（SSID）
        /// </summary>
        [Required(ErrorMessage = "SSID不能为空")]
        public string SSID { get; set; }

        /// <summary>
        /// WiFi 密码（可选，开放网络不需要）
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// NCB 服务器 IP 地址
        /// </summary>
        [Required(ErrorMessage = "NCBIP不能为空")]
        public string NCBIP { get; set; }
    }

    /// <summary>
    /// 热点状态 DTO
    /// </summary>
    public class HotspotStatusDto
    {
        /// <summary>
        /// 热点是否激活
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 热点 SSID
        /// </summary>
        public string SSID { get; set; }

        /// <summary>
        /// 热点密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 配网页面 URL
        /// </summary>
        public string ConfigUrl { get; set; }
    }
}

