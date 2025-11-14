using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Response
{
    public class CheckForUpdateResponse
    {
        public bool IsNeedUpdate { get; set; }
        public string CurrentVersion { get; set; }
        public string RemoteVersion { get; set; }
    }
}
