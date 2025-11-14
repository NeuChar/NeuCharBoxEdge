using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp
{
    public class CenterDefinition
    {
        public static GetTokenResult token = null;

        public const string CenterHttp = "http";
        public const string CenterPort = "5000";
        //public const string CenterHttp = "https";
        //public const string CenterPort = "5001";
    }
    public class GetTokenResult
    {
        public string Token { get; set; }
        public DateTime ExpireTime { get; set; }
    }
}
