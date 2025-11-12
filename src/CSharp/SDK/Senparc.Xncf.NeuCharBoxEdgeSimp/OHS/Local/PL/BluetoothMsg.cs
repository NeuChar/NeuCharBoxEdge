using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL
{
    public class BluetoothMsg
    {
        /// <summary>
        /// 消息Id
        /// </summary>
        public string MsgId { get; set; }
        /// <summary>
        /// 消息时间
        /// </summary>
        public DateTime Time { get; set; }
        /// <summary>
        /// 类型 0获取边缘设备信息
        /// </summary>
        public int Type { get; set; }
        /// <summary>
        /// 数据
        /// </summary>
        public string Data { get; set; }
        /// <summary>
        /// 签名
        /// </summary>
        public string Sign { get; set; }
    }
    public class BluetoothMsgRsp
    {
        /// <summary>
        /// 消息Id
        /// </summary>
        public string MsgId { get; set; }
        /// <summary>
        /// 消息时间
        /// </summary>
        public DateTime Time { get; set; }
        /// <summary>
        /// 类型 0获取边缘设备信息
        /// </summary>
        public int Type { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// 数据
        /// </summary>
        public string Data { get; set; }
        /// <summary>
        /// 签名
        /// </summary>
        public string Sign { get; set; }
    }
    public class WifiConfigMsg
    {
        /// <summary>
        /// 
        /// </summary>
        public string SSID { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string Password { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string NCBIP { get; set; }
    }
}
