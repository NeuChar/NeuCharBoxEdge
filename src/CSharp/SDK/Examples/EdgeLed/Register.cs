using Senparc.Ncf.XncfBase;

namespace EdgeLed
{
    [XncfRegister]
    public class Register : XncfRegisterBase, IXncfRegister
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        public override string Name => "数字管";

        /// <summary>
        /// 唯一的GUID，开发者自行生成GUID
        /// </summary>
        public override string Uid => "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX";

        /// <summary>
        /// 版本号
        /// </summary>
        public override string Version => "1.0.1";

        public override string MenuName => string.Empty;

        public override string Icon => string.Empty;

        public override string Description => string.Empty;
    }
}
