using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models
{
    public class SenderReceiverSet
    {
        public const string SenderReceiverSetKey = "SenderReceiverSet";
        public string dId { get; set; }
        public string uId { get; set; }
        public string XncfUId { get; set; }
        public string deciveName { get; set; }
        public string KeepAliveApi { get; set; } = "/api/Senparc.Xncf.NeuCharBoxCenter/CenterAppService/Xncf.NeuCharBoxCenter_CenterAppService.KeepAlive";
        public string NeuCharCom { get; set; } = "https://www.neuchar.com";
        public string DevelopSocketUrl { get; set; } = "https://www.neuchar.com/DeviceHub";

        public string Edge_HttpOrHttps { get; set; } = "http";
        public string Edge_Port { get; set; } = "5000";
        public string NCBIP { get; set; } = "";
        /// <summary>
        /// 是否开启AP
        /// </summary>
        public bool? IsOpenAP { get; set; } = false;
    }
}
