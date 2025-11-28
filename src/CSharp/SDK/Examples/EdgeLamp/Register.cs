using Senparc.Ncf.XncfBase;

namespace EdgeLamp;

[XncfRegister]
public class Register : XncfRegisterBase, IXncfRegister
{
    /// <summary>
    /// 设备名称
    /// </summary>
    public override string Name => "LED灯";

    /// <summary>
    /// 唯一的GUID，开发者自行生成GUID
    /// </summary>
    public override string Uid => "475B9077-1A08-5682-4E60-0E4D0EC9BE45";

    /// <summary>
    /// 版本号
    /// </summary>
    public override string Version => "1.0.1";

    public override string MenuName => string.Empty;

    public override string Icon => string.Empty;

    public override string Description => string.Empty;
}
