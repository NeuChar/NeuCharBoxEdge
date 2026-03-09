using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using Senparc.CO2NET.Trace;
using Senparc.Ncf.Core.AppServices;
using Senparc.Ncf.XncfBase;
using Senparc.Ncf.XncfBase.FunctionRenders;
using Senparc.Ncf.XncfBase.Functions;
using Senparc.Ncf.XncfBase.Threads;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models.Objects;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using EdgeOTA;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Helper;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp
{
        public partial class Register : IXncfThread
    {

        private readonly IServiceProvider _serviceProvider;
        public static HubConnection HubConnection { get; set; }
        public static HubConnection NCBConnection { get; set; }
        
        // 用于EdgeDataPush定时控制
        private static List<(MethodInfo method, EdgeDataPushAttribute attribute, Type declaringType)> _edgeDataPushMethods = new List<(MethodInfo, EdgeDataPushAttribute, Type)>();
        private static DateTime _lastEdgeDataPushTime = DateTime.MinValue;
        private static readonly int _edgeDataPushIntervalMilliseconds = 100; // 50毫秒执行一次，高频实时监控
        
        // 优化：添加实例缓存，避免频繁创建
        private static readonly Dictionary<Type, object> _instanceCache = new Dictionary<Type, object>();
        private static readonly object _instanceCacheLock = new object();
        
        // 优化：添加错误重试机制
        private static readonly Dictionary<string, int> _methodErrorCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, DateTime> _methodLastErrorTime = new Dictionary<string, DateTime>();
        private static readonly int _maxErrorCount = 5;
        private static readonly int _errorCooldownSeconds = 1; // 改为1秒重试
        
        // 优化：添加背压控制
        private static bool _isExecutingEdgeDataPush = false;
        private static int NoGattIdx = 0;
        
        // 保存上次连接的gatewayAddress，用于检测变化
        private static string _lastConnectedGatewayAddress = null;
        
        // 用于通知连接线程立即重连的信号
        private static volatile bool _forceReconnectSignal = false;

        public void ThreadConfig(XncfThreadBuilder xncfThreadBuilder)
        {
            //开发阶段，边缘设备连接neuchar
            xncfThreadBuilder.AddThreadInfo(new ThreadInfo("Edge", TimeSpan.FromMilliseconds(1000), async (app, thread) =>
            {
                //WebScoket 连接
                if (HubConnection == null || HubConnection.State == HubConnectionState.Disconnected)
                {
                    SenderReceiverSet setting = null;
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var serviceProvider = scope.ServiceProvider;
                        //setting = serviceProvider.GetService<IOptions<SenderReceiverSet>>().Value;
                        setting = serviceProvider.GetService<SenderReceiverSet>();
                    }
                    if (!string.IsNullOrWhiteSpace(setting.DevelopSocketUrl))
                    {
                        //HubConnection = new HubConnectionBuilder().WithUrl("http://localhost:11945/DeviceHub?DID=DID1&UID=1891743416786751488").Build();
                        HubConnection = new HubConnectionBuilder().WithUrl($"{setting.DevelopSocketUrl}?DID={setting.dId}&UID={setting.uId}").Build();


                        var XncfRegisterList = new List<XncfRegisterItem>();
                        //所有Register
                        var xncfRegisterList = Senparc.Ncf.XncfBase.XncfRegisterManager.RegisterList
                                                 .OrderByDescending(z => (z.GetType().GetCustomAttributes(typeof(XncfOrderAttribute), true).FirstOrDefault() as XncfOrderAttribute)?.Order)
                                                 .ToList();
                        xncfRegisterList = xncfRegisterList.Where(t => t.Uid == setting.XncfUId).ToList();
                        var xncfRegister = xncfRegisterList.FirstOrDefault() ?? throw new Exception("UID 未找到");
                        var functionTools = new List<Tool>();//设置阶段AI对话使用
                        var functionToolsForChat = new List<Tool>();//MCP使用

                        if (Senparc.Ncf.XncfBase.Register.FunctionRenderCollection.TryGetValue(xncfRegister.GetType(), out var functionGroup))
                        {
                            //遍历某个 Register 下所有的方法      TODO：未来可添加分组
                            foreach (var functionBag in functionGroup.Values)
                            {
                                try
                                {
                                    {
                                        var functionTool = new Tool();
                                        functionTools.Add(functionTool);

                                        //获取方法和参数结果
                                        var result = await FunctionHelper.GetFunctionParameterInfoAsync(this._serviceProvider, functionBag, true);

                                        //设置 functionTool- 当前接口整体信息
                                        functionTool.type = functionBag.MethodInfo.ReturnType.FullName;//返回类型，TODO：需要判断一下是否为泛型，需要剥离 Task、AppServiceBase
                                        functionTool.returnDescription = functionBag.FunctionRenderAttribute.Description;//自定义新加字段
                                                                                                                         //functionTool.function.description = functionBag.FunctionRenderAttribute.Description;
                                        functionTool.function.description = functionBag.FunctionRenderAttribute.Name;
                                        //functionTool.function.name = functionBag.FunctionRenderAttribute.Name;
                                        functionTool.function.name = functionBag.Key;
                                        functionTool.function.strict = true;

                                        //设置当前接口的参数信息
                                        functionTool.function.parameters = new Parameters()
                                        {
                                            additionalProperties = false,
                                            required = result.Where(z => z.IsRequired).Select(p => p.Name).ToArray(),
                                            properties = new Dictionary<string, Properties>(),
                                            type = functionBag.MethodInfo.GetType().FullName
                                        };

                                        //设置每一个参数的详细配置
                                        foreach (var paraInfo in result)
                                        {
                                            var prop = new Properties()
                                            {
                                                //description = paraInfo.Description,
                                                description = paraInfo.Title,
                                                type = paraInfo.ParameterType switch
                                                {
                                                    ParameterType.Text => "input",
                                                    ParameterType.Password => "input",
                                                    ParameterType.DropDownList => "select",
                                                    ParameterType.CheckBoxList => "checkboxGroup",
                                                    _ => "input"
                                                }
                                            };


                                            //设置枚举
                                            if (paraInfo.SelectionList?.Items.Count > 0)
                                            {
                                                prop.Enum = paraInfo.SelectionList.Items.Select(z => z.Value).ToArray();
                                            }

                                            functionTool.function.parameters.properties[paraInfo.Name] = prop;
                                        }
                                    }

                                    {
                                        var functionToolForChat = new Tool();
                                        functionToolsForChat.Add(functionToolForChat);

                                        //获取方法和参数结果
                                        var result = await FunctionHelper.GetFunctionParameterInfoAsync(this._serviceProvider, functionBag, true);

                                        //设置 functionTool- 当前接口整体信息
                                        functionToolForChat.type = functionBag.MethodInfo.ReturnType.FullName;//返回类型
                                        functionToolForChat.function.description = functionBag.FunctionRenderAttribute.Description;
                                        functionToolForChat.function.name = functionBag.FunctionRenderAttribute.Name;
                                        functionToolForChat.function.strict = true;

                                        //设置当前接口的参数信息
                                        functionToolForChat.function.parameters = new Parameters()
                                        {
                                            additionalProperties = false,
                                            required = result.Where(z => z.IsRequired).Select(p => p.Name).ToArray(),
                                            properties = new Dictionary<string, Properties>(),
                                            type = functionBag.MethodInfo.GetType().FullName
                                        };

                                        //设置每一个参数的详细配置
                                        foreach (var paraInfo in result)
                                        {
                                            var prop = new Properties()
                                            {
                                                description = paraInfo.Description,
                                                type = paraInfo.ParameterType switch
                                                {
                                                    ParameterType.Text => "string",
                                                    ParameterType.Password => "string",
                                                    ParameterType.DropDownList => "enum",
                                                    ParameterType.CheckBoxList => "enum",
                                                    _ => "string"
                                                }
                                            };


                                            //设置枚举
                                            if (paraInfo.SelectionList?.Items.Count > 0)
                                            {
                                                prop.Enum = paraInfo.SelectionList.Items.Select(z => z.Value).ToArray();
                                            }

                                            functionToolForChat.function.parameters.properties[paraInfo.Name] = prop;
                                        }
                                    }

                                }
                                catch (Exception ex)
                                {
                                    SenparcTrace.BaseExceptionLog(ex);
                                    throw new Exception($"载入 {functionBag.Key} 时出错，请查看日志！如果刚添加数据库迁移，请先完成模块升级！");
                                }
                            }
                        }

                        var postData = new DevicePoolFunction_DeviceSyncVD()
                        {
                            UID = setting.uId,
                            DID = setting.dId,
                            FunctionTools = JsonConvert.SerializeObject(functionTools),
                            FunctionToolsForChat = JsonConvert.SerializeObject(functionToolsForChat),
                            DevicePoolFunctionInterfaces = new List<DevicePoolFunctionInterface_VD>(),
                            DevicePoolFunctionListeners = new List<DevicePoolFunctionListener_VD>()
                        };
                        foreach (var tool in functionTools)
                        {
                            var devicePoolFunctionInterface_VD = new DevicePoolFunctionInterface_VD()
                            {
                                InterfaceName = tool.function.name,
                                InterfaceDescription = tool.function.description,
                                ReturnMessageType = tool.type,
                                DevicePoolFunctionInterfaceParameters = new List<DevicePoolFunctionInterfaceParameter_VD>()
                            };
                            foreach (var param in tool.function.parameters.properties)
                            {
                                devicePoolFunctionInterface_VD.DevicePoolFunctionInterfaceParameters.Add(new DevicePoolFunctionInterfaceParameter_VD()
                                {
                                    ParameterName = param.Key,
                                    ParameterDescription = param.Value.description,
                                    ParameterType = param.Value.type,
                                });
                            }

                            postData.DevicePoolFunctionInterfaces.Add(devicePoolFunctionInterface_VD);
                        }


















                        HubConnection.On<string>("ReceiveMessage", message =>
                        {
                            Console.WriteLine("Receive message:" + message);
                        });

                        //线上通知需要同步接口
                        HubConnection.On<string>("SyncDeviceFunctions", async message =>
                        {
                            Console.WriteLine("SyncDeviceFunctions:" + message);

                            var XncfRegisterList = new List<XncfRegisterItem>();
                            //所有Register
                            var xncfRegisterList = Senparc.Ncf.XncfBase.XncfRegisterManager.RegisterList
                                                     .OrderByDescending(z => (z.GetType().GetCustomAttributes(typeof(XncfOrderAttribute), true).FirstOrDefault() as XncfOrderAttribute)?.Order)
                                                     .ToList();
                            xncfRegisterList = xncfRegisterList.Where(t => t.Uid == setting.XncfUId).ToList();

                            var xncfRegister = xncfRegisterList.FirstOrDefault() ?? throw new Exception("UID 未找到");

                            List<DevicePoolFunctionInterface_VD> lstInterface = new List<DevicePoolFunctionInterface_VD>();//接口
                            List<DevicePoolFunctionListener_VD> lstListener = new List<DevicePoolFunctionListener_VD>();//监听

                            var functionTools = new List<Tool>();//设置阶段AI对话使用
                            var functionToolsForChat = new List<Tool>();//MCP使用

                            if (Senparc.Ncf.XncfBase.Register.FunctionRenderCollection.TryGetValue(xncfRegister.GetType(), out var functionGroup))
                            {
                                //遍历某个 Register 下所有的方法      TODO：未来可添加分组
                                foreach (var functionBag in functionGroup.Values)
                                {
                                    try
                                    {
                                        {
                                            var functionTool = new Tool();
                                            functionTools.Add(functionTool);

                                            //获取方法和参数结果
                                            var result = await FunctionHelper.GetFunctionParameterInfoAsync(this._serviceProvider, functionBag, true);

                                            //设置 functionTool- 当前接口整体信息
                                            functionTool.type = functionBag.MethodInfo.ReturnType.FullName;//返回类型，TODO：需要判断一下是否为泛型，需要剥离 Task、AppServiceBase
                                            functionTool.returnDescription = functionBag.FunctionRenderAttribute.Description;//自定义新加字段
                                                                                                                             //functionTool.function.description = functionBag.FunctionRenderAttribute.Description;
                                            functionTool.function.description = functionBag.FunctionRenderAttribute.Name;
                                            //functionTool.function.name = functionBag.FunctionRenderAttribute.Name;
                                            functionTool.function.name = functionBag.Key;
                                            functionTool.function.strict = true;

                                            //设置当前接口的参数信息
                                            functionTool.function.parameters = new Parameters()
                                            {
                                                additionalProperties = false,
                                                required = result.Where(z => z.IsRequired).Select(p => p.Name).ToArray(),
                                                properties = new Dictionary<string, Properties>(),
                                                type = functionBag.MethodInfo.GetType().FullName
                                            };

                                            //设置每一个参数的详细配置
                                            foreach (var paraInfo in result)
                                            {
                                                var prop = new Properties()
                                                {
                                                    //description = paraInfo.Description,
                                                    description = paraInfo.Title,
                                                    type = paraInfo.ParameterType switch
                                                    {
                                                        ParameterType.Text => "input",
                                                        ParameterType.Password => "input",
                                                        ParameterType.DropDownList => "select",
                                                        ParameterType.CheckBoxList => "checkboxGroup",
                                                        _ => "input"
                                                    }
                                                };


                                                //设置枚举
                                                if (paraInfo.SelectionList?.Items.Count > 0)
                                                {
                                                    prop.Enum = paraInfo.SelectionList.Items.Select(z => z.Value).ToArray();
                                                }

                                                functionTool.function.parameters.properties[paraInfo.Name] = prop;
                                            }
                                        }

                                        {
                                            var functionToolForChat = new Tool();
                                            functionToolsForChat.Add(functionToolForChat);

                                            //获取方法和参数结果
                                            var result = await FunctionHelper.GetFunctionParameterInfoAsync(this._serviceProvider, functionBag, true);

                                            //设置 functionTool- 当前接口整体信息
                                            functionToolForChat.type = functionBag.MethodInfo.ReturnType.FullName;//返回类型
                                            functionToolForChat.function.description = functionBag.FunctionRenderAttribute.Description;
                                            functionToolForChat.function.name = functionBag.Key;
                                            functionToolForChat.function.strict = true;

                                            //设置当前接口的参数信息
                                            functionToolForChat.function.parameters = new Parameters()
                                            {
                                                additionalProperties = false,
                                                required = result.Where(z => z.IsRequired).Select(p => p.Name).ToArray(),
                                                properties = new Dictionary<string, Properties>(),
                                                type = functionBag.MethodInfo.GetType().FullName
                                            };

                                            //设置每一个参数的详细配置
                                            foreach (var paraInfo in result)
                                            {
                                                var prop = new Properties()
                                                {
                                                    //description = paraInfo.Description,
                                                    description = paraInfo.Title,
                                                    type= paraInfo.SystemType,
                                                    //type = paraInfo.ParameterType switch
                                                    //{
                                                    //    ParameterType.Text => "string",
                                                    //    ParameterType.Password => "string",
                                                    //    ParameterType.DropDownList => "enum",
                                                    //    ParameterType.CheckBoxList => "enum",
                                                    //    _ => "string"
                                                    //}
                                                };


                                                //设置枚举
                                                if (paraInfo.SelectionList?.Items.Count > 0)
                                                {
                                                    prop.Enum = paraInfo.SelectionList.Items.Select(z => z.Value).ToArray();
                                                }

                                                functionToolForChat.function.parameters.properties[paraInfo.Name] = prop;
                                            }
                                        }

                                        {
                                            var itemInterface = new DevicePoolFunctionInterface_VD();
                                            itemInterface.InterfaceName= functionBag.Key;
                                            itemInterface.InterfaceDescription = functionBag.FunctionRenderAttribute.Name;
                                            var retType = functionBag.MethodInfo.ReturnType;
                                            while (retType.GenericTypeArguments.Length > 0)
                                            {
                                                retType= retType.GenericTypeArguments[0];
                                            }
                                            itemInterface.ReturnMessageType = retType.FullName;
                                            itemInterface.DevicePoolFunctionInterfaceParameters = new List<DevicePoolFunctionInterfaceParameter_VD>();

                                            //获取方法和参数结果
                                            var result = await FunctionHelper.GetFunctionParameterInfoAsync(this._serviceProvider, functionBag, true);
                                            //设置每一个参数的详细配置
                                            foreach (var paraInfo in result)
                                            {
                                                itemInterface.DevicePoolFunctionInterfaceParameters.Add(new DevicePoolFunctionInterfaceParameter_VD()
                                                {
                                                    ParameterName = paraInfo.Name,
                                                    ParameterDescription = paraInfo.Title,
                                                    ParameterType = paraInfo.ParameterType switch
                                                    {
                                                        ParameterType.Text => "input",
                                                        ParameterType.Password => "input",
                                                        ParameterType.DropDownList => "select",
                                                        ParameterType.CheckBoxList => "checkboxGroup",
                                                        _ => "input"
                                                    }
                                                }); 
                                            }
                                            lstInterface.Add(itemInterface);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        SenparcTrace.BaseExceptionLog(ex);
                                        throw new Exception($"载入 {functionBag.Key} 时出错，请查看日志！如果刚添加数据库迁移，请先完成模块升级！");
                                    }
                                }
                            }


                            #region 查找监听方法
                            {
                                var a = xncfRegister.GetType();
                                var assembly = a.Assembly;
                                // 遍历程序集中的所有类型，查找EdgeDataPush方法
                                foreach (var type in assembly.GetTypes())
                                {
                                    // 获取类型中的所有公共方法
                                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                                    
                                    foreach (var method in methods)
                                    {
                                        // 检查方法是否有EdgeDataPush特性
                                        var edgeDataPushAttribute = method.GetCustomAttribute<EdgeDataPushAttribute>();
                                        if (edgeDataPushAttribute != null)
                                        {
                                            // 验证方法是否符合要求（无参数）
                                            if (EdgeDataPushAttribute.IsValidMethod(method))
                                            {
                                                var retType = method.ReturnType;
                                                while (retType.GenericTypeArguments.Length > 0)
                                                {
                                                    retType = retType.GenericTypeArguments[0];
                                                }
                                                lstListener.Add(new DevicePoolFunctionListener_VD()
                                                {
                                                    ReturnParameter = $"{type.FullName}-{method.Name}",
                                                    ReturnParameterDataType = retType.FullName,//method.ReturnType.FullName,
                                                    Description = edgeDataPushAttribute.Description
                                                });
                                                //Console.WriteLine($"发现EdgeDataPush方法: {type.FullName}.{method.Name}, 描述: {edgeDataPushAttribute.Description}");
                                            }
                                        }
                                    }
                                }
                            }
                            #endregion


                            var postData = new DevicePoolFunction_DeviceSyncVD()
                            {
                                UID = setting.uId,
                                DID = setting.dId,
                                FunctionTools = JsonConvert.SerializeObject(functionTools),
                                FunctionToolsForChat = JsonConvert.SerializeObject(functionToolsForChat),
                                DevicePoolFunctionInterfaces =lstInterface,
                                DevicePoolFunctionListeners = lstListener
                            };
                            //////////foreach (var tool in functionTools)
                            //////////{
                            //////////    var devicePoolFunctionInterface_VD = new DevicePoolFunctionInterface_VD()
                            //////////    {
                            //////////        InterfaceName = tool.function.name,
                            //////////        InterfaceDescription = tool.function.description,
                            //////////        ReturnMessageType = tool.type,
                            //////////        DevicePoolFunctionInterfaceParameters = new List<DevicePoolFunctionInterfaceParameter_VD>()
                            //////////    };
                            //////////    foreach (var param in tool.function.parameters.properties)
                            //////////    {
                            //////////        devicePoolFunctionInterface_VD.DevicePoolFunctionInterfaceParameters.Add(new DevicePoolFunctionInterfaceParameter_VD()
                            //////////        {
                            //////////            ParameterName = param.Key,
                            //////////            ParameterDescription = param.Value.description,
                            //////////            ParameterType = param.Value.type,
                            //////////        });
                            //////////    }

                            //////////    postData.DevicePoolFunctionInterfaces.Add(devicePoolFunctionInterface_VD);
                            //////////}

                            await HubConnection.InvokeAsync("PushSyncDeviceFunctions", postData);
                            /*
{
	"DID": "1897864873199669248-10",
	"UID": "1897864677141123072",
	"FunctionTools": "[{\"type\":\"System.Threading.Tasks.Task`1[[Senparc.Ncf.Core.AppServices.AppResponseBase`1[[System.String, System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]], Senparc.Ncf.Core, Version=0.23.10.0, Culture=neutral, PublicKeyToken=null]]\",\"function\":{\"name\":\"Senparc.Xncf.NeuCharBoxEdge_LED.OHS.Local.AppService.EdgeAppService-DisplayNumber\",\"description\":\"显示数字\",\"parameters\":{\"type\":\"System.Reflection.RuntimeMethodInfo\",\"properties\":{\"Number\":{\"type\":\"input\",\"description\":\"最大长度为4位的数字字符串\",\"Enum\":null}},\"required\":[\"Number\"],\"additionalProperties\":false},\"strict\":true}}]",
	"DevicePoolFunctionInterfaces": [{
		"Id": null,
		"InterfaceName": "Senparc.Xncf.NeuCharBoxEdge_LED.OHS.Local.AppService.EdgeAppService-DisplayNumber",
		"InterfaceDescription": "显示数字",
		"ReturnMessageType": "System.Threading.Tasks.Task`1[[Senparc.Ncf.Core.AppServices.AppResponseBase`1[[System.String, System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]], Senparc.Ncf.Core, Version=0.23.10.0, Culture=neutral, PublicKeyToken=null]]",
		"DevicePoolFunctionInterfaceParameters": [{
			"Id": null,
			"ParameterName": "Number",
			"ParameterDescription": "最大长度为4位的数字字符串",
			"ParameterType": "input"
		}]
	}],
	"DevicePoolFunctionListeners": []
}
                             */

                        });


                        //线上UI界面通知执行方法
                        HubConnection.On<DevicePoolFunction_SendDeviceFunctionTriggerVD>("SendDeviceFunctionTrigger", async data =>
                        {
                            Console.WriteLine("SendDeviceFunctionTrigger:" + JsonConvert.SerializeObject(data));
                            /*
{
	"DID": "1897864873199669248-10",
	"UID": "1897864677141123072",
	"InterfaceId": "1897882522176589826",
	"InterfaceName": "Senparc.Xncf.NeuCharBoxEdge_LED.OHS.Local.AppService.EdgeAppService-DisplayNumber",
	"ParameterData": [{
		"ParameterName": "Number",
		"ParameterValue": "1234"
	}]
}
                             */


                            #region 方法
                            try
                            {
                                // 解析完整的类型名称和方法名
                                var parts = data.InterfaceName.Split('-');
                                if (parts.Length != 2)
                                {
                                    throw new ArgumentException($"Invalid InterfaceName format: {data.InterfaceName}");
                                }

                                string fullTypeName = parts[0];
                                string methodName = parts[1];

                                // 获取类型
                                Type type = Type.GetType(fullTypeName);
                                if (type == null)
                                {
                                    // 如果找不到类型，尝试加载所有程序集中的类型
                                    type = AppDomain.CurrentDomain.GetAssemblies()
                                        .SelectMany(a => a.GetTypes())
                                        .FirstOrDefault(t => t.FullName == fullTypeName);
                                }

                                if (type == null)
                                {
                                    throw new Exception($"Type not found: {fullTypeName}");
                                }

                                // 创建实例（使用依赖注入）
                                object instance;
                                using (var scope = app.ApplicationServices.CreateScope())
                                {
                                    instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, type);


                                    // 获取方法 - 修复重载方法的处理
                                    // 获取所有同名方法
                                    var methods = type.GetMethods().Where(m => m.Name == methodName).ToList();

                                    if (methods.Count == 0)
                                    {
                                        throw new Exception($"Method not found: {methodName}");
                                    }

                                    // 优先选择带有 FunctionRender 特性的方法
                                    var functionRenderMethod = methods.FirstOrDefault(m => 
                                        m.GetCustomAttributes<FunctionRenderAttribute>(false).Any()
                                    );
                                    
                                    if (functionRenderMethod != null)
                                    {
                                        // 如果找到带 FunctionRender 特性的方法，直接使用它
                                        methods = new List<MethodInfo> { functionRenderMethod };
                                    }

                                    // 解析 ParameterData 到字典中
                                    Dictionary<string, string> parameterDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    if (data.ParameterData != null)
                                    {
                                        foreach (var p in data.ParameterData)
                                        {
                                            parameterDict[p.ParameterName] = p.ParameterValue;
                                        }
                                    }

                                    // 选择最合适的方法重载
                                    MethodInfo method = null;

                                    // 尝试找到参数名匹配的方法
                                    if (parameterDict.Count > 0)
                                    {
                                        // 首先尝试匹配具有复杂参数类型的方法（如 DisplayNumberRequest）
                                        foreach (var m in methods)
                                        {
                                            var parameters = m.GetParameters();
                                            if (parameters.Length == 1 &&
                                                !parameters[0].ParameterType.IsPrimitive &&
                                                parameters[0].ParameterType != typeof(string) &&
                                                parameters[0].ParameterType != typeof(decimal))
                                            {
                                                // 找到了一个带有复杂参数类型的方法
                                                method = m;
                                                break;
                                            }
                                        }

                                        // 如果没有找到带复杂参数的方法，尝试匹配参数名
                                        if (method == null)
                                        {
                                            foreach (var m in methods)
                                            {
                                                var parameters = m.GetParameters();
                                                // 检查第一个参数名是否与参数字典中的键匹配
                                                if (parameters.Length > 0 &&
                                                    parameterDict.Keys.Any(key => string.Equals(key, parameters[0].Name, StringComparison.OrdinalIgnoreCase)))
                                                {
                                                    method = m;
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    // 如果仍未找到方法，使用参数数量匹配
                                    if (method == null)
                                    {
                                        if (parameterDict.Count > 0)
                                        {
                                            // 尝试匹配参数数量
                                            var methodsWithMatchingParamCount = methods.Where(m => m.GetParameters().Length == parameterDict.Count).ToList();
                                            if (methodsWithMatchingParamCount.Count > 0)
                                            {
                                                method = methodsWithMatchingParamCount[0];
                                            }
                                            else
                                            {
                                                // 如果没有完全匹配数量的方法，选择参数最接近的方法
                                                method = methods.OrderBy(m => Math.Abs(m.GetParameters().Length - parameterDict.Count)).First();
                                            }
                                        }
                                        else
                                        {
                                            // 没有参数，尝试找无参方法
                                            var methodsWithNoParams = methods.Where(m => m.GetParameters().Length == 0).ToList();
                                            if (methodsWithNoParams.Count > 0)
                                            {
                                                method = methodsWithNoParams[0];
                                            }
                                            else
                                            {
                                                // 如果没有无参方法，选择参数最少的方法
                                                method = methods.OrderBy(m => m.GetParameters().Length).First();
                                            }
                                        }
                                    }

                                    if (method == null)
                                    {
                                        throw new Exception($"无法找到匹配的方法: {methodName}");
                                    }

                                    // 获取方法的参数信息
                                    var methodParameters = method.GetParameters();
                                    var parameterValues = new object[methodParameters.Length];

                                    // 遍历方法的每个参数
                                    for (int i = 0; i < methodParameters.Length; i++)
                                    {
                                        var methodParam = methodParameters[i];
                                        var paramType = methodParam.ParameterType;

                                        // 如果参数是简单类型（基本类型、字符串等）
                                        if (paramType.IsPrimitive || paramType == typeof(string) || paramType == typeof(decimal))
                                        {
                                            // 查找匹配的参数数据
                                            if (parameterDict.TryGetValue(methodParam.Name, out string paramValue))
                                            {
                                                // 转换参数值到正确的类型
                                                parameterValues[i] = Convert.ChangeType(paramValue, paramType);
                                            }
                                            else
                                            {
                                                // 如果找不到参数值且参数有默认值，使用默认值
                                                parameterValues[i] = methodParam.HasDefaultValue ? methodParam.DefaultValue : null;
                                            }
                                        }
                                        // 如果参数是复杂类型（类或结构体）
                                        else
                                        {
                                            // 创建参数对象实例
                                            var paramInstance = Activator.CreateInstance(paramType);

                                            // 获取参数类型的所有属性
                                            var properties = paramType.GetProperties();

                                            // 遍历属性并设置值
                                            foreach (var property in properties)
                                            {
                                                if (parameterDict.TryGetValue(property.Name, out string propValue))
                                                {
                                                    try
                                                    {
                                                        // 转换并设置属性值
                                                        var convertedValue = Convert.ChangeType(propValue, property.PropertyType);
                                                        property.SetValue(paramInstance, convertedValue);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        // 忽略转换失败的属性
                                                        Console.WriteLine($"Error converting value for property {property.Name}: {ex.Message}");
                                                    }
                                                }
                                            }

                                            parameterValues[i] = paramInstance;
                                        }
                                    }

                                    // 执行方法
                                    var result = method.Invoke(instance, parameterValues);

                                    // 如果是异步方法，等待结果
                                    if (result is Task task)
                                    {
                                        await task;
                                        // 获取Task的实际结果（如果有）
                                        if (task.GetType().IsGenericType)
                                        {
                                            result = ((dynamic)task).Result;
                                        }
                                    }

                                    // 更新返回数据
                                    data.ParameterData = new List<DevicePoolFunction_ParameterDataVD>
                                    {
                                        new DevicePoolFunction_ParameterDataVD
                                        {
                                            ParameterName = "result",
                                            ParameterValue = JsonConvert.DeserializeObject<AppResponseBase>( JsonConvert.SerializeObject(result)).Data?.ToString()??"OK"
                                        }
                                    };
                                }
                            }
                            catch (Exception ex)
                            {
                                // 处理错误情况
                                data.ParameterData = new List<DevicePoolFunction_ParameterDataVD>
                                {
                                    new DevicePoolFunction_ParameterDataVD
                                    {
                                        ParameterName = "error",
                                        ParameterValue = ex.Message
                                    }
                                };
                                Console.WriteLine($"Error executing method: {ex}");
                            }
                            #endregion

                            //回传
                            await HubConnection.InvokeAsync("ReturnDeviceFunctionTrigger", setting.dId, setting.uId, data);

                        });
                         


                        HubConnection.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                }

            }));


            //连接NCB的SignalR
            xncfThreadBuilder.AddThreadInfo(new ThreadInfo("NCBConnection", TimeSpan.FromMilliseconds(_edgeDataPushIntervalMilliseconds), async (app, thread) =>
            {

                
                
                try { 
                    SenderReceiverSet setting = null;
                    using (var scope = app.ApplicationServices.CreateScope())
                    {
                        var serviceProvider = scope.ServiceProvider;
                        setting = serviceProvider.GetService<SenderReceiverSet>();
                    }

                    var gatewayAddress = Helper.IpHelper.GetGatewayAddress(setting);
                    
                    // 检查是否收到强制重连信号
                    if (_forceReconnectSignal)
                    {
                        _forceReconnectSignal = false; // 重置信号
                        
                        // 强制断开现有连接
                        if (NCBConnection != null)
                        {
                            try
                            {
                                await NCBConnection.StopAsync();
                                await NCBConnection.DisposeAsync();
                            }
                            catch { }
                            NCBConnection = null;
                            _lastConnectedGatewayAddress = null;
                        }
                    }
                    
                    // 检测gatewayAddress是否变化，如果变化则强制断开连接
                    bool needReconnect = false;
                    
                    // 扩展检测：无论连接状态如何，只要地址不为空且与上次不同，就需要重新连接
                    if (!string.IsNullOrWhiteSpace(gatewayAddress) && 
                        !string.IsNullOrWhiteSpace(_lastConnectedGatewayAddress) && 
                        _lastConnectedGatewayAddress != gatewayAddress)
                    {
                        needReconnect = true;
                    }
                    
                    // 如果连接已断开，清除上次连接的地址记录
                    if (NCBConnection != null && NCBConnection.State == HubConnectionState.Disconnected)
                    {
                        _lastConnectedGatewayAddress = null;
                    }
                    
                    // 如果连接为空、已断开或需要重新连接，则重新建立连接
                    if (NCBConnection == null || NCBConnection.State == HubConnectionState.Disconnected || needReconnect)
                    {
                        // 如果需要重新连接，先断开现有连接
                        if (needReconnect && NCBConnection != null)
                        {
                            try
                            {
                                await NCBConnection.StopAsync();
                                await NCBConnection.DisposeAsync();
                                NCBConnection = null;
                                _lastConnectedGatewayAddress = null;
                            }
                            catch (Exception)
                            {
                                NCBConnection = null;
                                _lastConnectedGatewayAddress = null;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(gatewayAddress))
                        {
                            NoGattIdx++;
                            if(NoGattIdx>100){
                                NoGattIdx=0;
                            }
                        }
                        else
                        {
                            var connectStartTime = DateTime.Now;
                            
                            //保活放到这里
                            var otaQueryStart = DateTime.Now;
                            var (findOTAConfig, lstOTAConfigs) = await OTAHelper.GetOTAConfigAsync(setting.uId, setting.dId, OTAHelper.FirmwareType_Backend);
                            var otaQueryTime = (DateTime.Now - otaQueryStart).TotalMilliseconds;

                            var url = $"{CenterDefinition.CenterHttp}://{gatewayAddress}:{CenterDefinition.CenterPort}/edgedatahub?did={setting.dId}&uid={setting.uId}&deciveName={setting.deciveName}&version={findOTAConfig?.CurrentVersion}&edgeHttpOrHttps={setting.Edge_HttpOrHttps}&edgePort={setting.Edge_Port}";
                            
                            // 配置 SignalR 连接：减少超时时间，添加自动重连
                            NCBConnection = new HubConnectionBuilder()
                                .WithUrl(url, options =>
                                {
                                    // 配置 HttpClient 超时为 15 秒（而不是默认的 120 秒）
                                    options.HttpMessageHandlerFactory = (handler) =>
                                    {
                                        if (handler is HttpClientHandler clientHandler)
                                        {
                                            // 忽略 SSL 证书错误（如果需要）
                                            clientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                                        }
                                        return handler;
                                    };
                                    
                                    // 通过 WebSocketConfiguration 配置超时
                                    options.WebSocketConfiguration = wsOptions =>
                                    {
                                        // WebSocket 配置（如果使用 WebSocket 传输）
                                    };
                                    
                                    // 配置传输类型和超时
                                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | 
                                                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                                })
                                .WithAutomaticReconnect(new[] { 
                                    TimeSpan.Zero,           // 立即重试
                                    TimeSpan.FromSeconds(2), // 2秒后重试
                                    TimeSpan.FromSeconds(5), // 5秒后重试
                                    TimeSpan.FromSeconds(10) // 10秒后重试
                                })
                                .Build();
                            
                            // 配置 HubConnection 的超时（15秒）
                            NCBConnection.ServerTimeout = TimeSpan.FromSeconds(15);
                            NCBConnection.HandshakeTimeout = TimeSpan.FromSeconds(15);

                            // 启动NCBConnection（先连接，连接成功后再扫描方法）
                            var connectionStart = DateTime.Now;
                            
                            try
                            {
                                // 使用 CancellationTokenSource 实现真正的 15 秒超时
                                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                                await NCBConnection.StartAsync(cts.Token);
                                
                                var connectionTime = (DateTime.Now - connectionStart).TotalMilliseconds;
                                var totalTime = (DateTime.Now - connectStartTime).TotalMilliseconds;
                            }
                            catch (OperationCanceledException)
                            {
                                var connectionTime = (DateTime.Now - connectionStart).TotalMilliseconds;
                                Console.WriteLine($"[错误] NCBConnection连接超时（15秒）！实际耗时: {connectionTime}ms，目标地址: {gatewayAddress}");
                                
                                // 清理连接对象
                                try
                                {
                                    await NCBConnection.StopAsync();
                                    await NCBConnection.DisposeAsync();
                                }
                                catch { }
                                NCBConnection = null;
                                _lastConnectedGatewayAddress = null;
                                
                                // 不抛出异常，让下次循环重试
                                return;
                            }
                            catch (Exception ex)
                            {
                                var connectionTime = (DateTime.Now - connectionStart).TotalMilliseconds;
                                //Console.WriteLine($"[错误] NCBConnection连接失败！耗时: {connectionTime}ms，错误: {ex.Message}");
                                
                                // 清理连接对象
                                try
                                {
                                    await NCBConnection.StopAsync();
                                    await NCBConnection.DisposeAsync();
                                }
                                catch { }
                                NCBConnection = null;
                                _lastConnectedGatewayAddress = null;
                                
                                // 不抛出异常，让下次循环重试
                                return;
                            }
                            
                            // 连接成功后，异步扫描EdgeDataPush方法（不阻塞连接）
                            if (_edgeDataPushMethods.Count == 0)
                            {
                                var scanStart = DateTime.Now;
                                
                                var XncfRegisterList = new List<XncfRegisterItem>();
                                //所有Register
                                var xncfRegisterList = Senparc.Ncf.XncfBase.XncfRegisterManager.RegisterList
                                                         .OrderByDescending(z => (z.GetType().GetCustomAttributes(typeof(XncfOrderAttribute), true).FirstOrDefault() as XncfOrderAttribute)?.Order)
                                                         .ToList();
                                xncfRegisterList = xncfRegisterList.Where(t => t.Uid == setting.XncfUId).ToList();
                                var xncfRegister = xncfRegisterList.FirstOrDefault() ?? throw new Exception("UID 未找到");

                                var a = xncfRegister.GetType();

                                // 获取程序集并查找EdgeDataPush方法（只在连接建立时执行一次）
                                var assembly = a.Assembly;
                                _edgeDataPushMethods.Clear(); // 清空之前的记录

                                // 遍历程序集中的所有类型，查找EdgeDataPush方法
                                foreach (var type in assembly.GetTypes())
                                {
                                    // 获取类型中的所有公共方法
                                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

                                    foreach (var method in methods)
                                    {
                                        // 检查方法是否有EdgeDataPush特性
                                        var edgeDataPushAttribute = method.GetCustomAttribute<EdgeDataPushAttribute>();
                                        if (edgeDataPushAttribute != null)
                                        {
                                            // 验证方法是否符合要求（无参数）
                                            if (EdgeDataPushAttribute.IsValidMethod(method))
                                            {
                                                _edgeDataPushMethods.Add((method, edgeDataPushAttribute, type));
                                            }
                                        }
                                    }
                                }
                                
                                var scanTime = (DateTime.Now - scanStart).TotalMilliseconds;
                            }
                            
                            // 保存当前连接的gatewayAddress
                            _lastConnectedGatewayAddress = gatewayAddress;


                            #region 监听方法
                            if (NCBConnection != null && NCBConnection.State == HubConnectionState.Connected)
                            {
                                //NCB通知执行方法
                                NCBConnection.On<string, InvokeRequest>("CenterPushData", async (msgId, data) =>
                                {
                                    #region 
                                    InvokeResponse invokeResponse = new InvokeResponse() { Success = true };
                                    try
                                    {
                                        // 解析完整的类型名称和方法名
                                        var parts = data.functionName.Split('-');
                                        if (parts.Length != 2)
                                        {
                                            throw new ArgumentException($"Invalid InterfaceName format: {data.functionName}");
                                        }

                                        string fullTypeName = parts[0];
                                        string methodName = parts[1];

                                        // 获取类型
                                        Type type = Type.GetType(fullTypeName);
                                        if (type == null)
                                        {
                                            // 如果找不到类型，尝试加载所有程序集中的类型
                                            type = AppDomain.CurrentDomain.GetAssemblies()
                                                .SelectMany(a => a.GetTypes())
                                                .FirstOrDefault(t => t.FullName == fullTypeName);
                                        }

                                        if (type == null)
                                        {
                                            throw new Exception($"Type not found: {fullTypeName}");
                                        }

                                        // 创建实例（使用依赖注入）
                                        // 修复：每次调用时创建一个新的 scope，并确保在使用完后正确释放
                                        using (var scope = app.ApplicationServices.CreateScope())
                                        {
                                            var serviceProvider = scope.ServiceProvider;
                                            var instance = ActivatorUtilities.CreateInstance(serviceProvider, type);

                                            // 获取方法 - 修复重载方法的处理
                                            // 获取所有同名方法
                                            var methods = type.GetMethods().Where(m => m.Name == methodName).ToList();

                                            if (methods.Count == 0)
                                            {
                                                throw new Exception($"Method not found: {methodName}");
                                            }

                                            // 解析postData JSON字符串
                                            Dictionary<string, string> parameterDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                            Type complexParamType = null;

                                            if (!string.IsNullOrWhiteSpace(data.postData) && data.postData != "{}")
                                            {
                                                try
                                                {
                                                    var jsonObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(data.postData);
                                                    if (jsonObj != null)
                                                    {
                                                        foreach (var kvp in jsonObj)
                                                        {
                                                            parameterDict[kvp.Key] = kvp.Value?.ToString();
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine($"[错误] 解析postData时出错: {ex.Message}");
                                                }
                                            }

                                            // 选择最合适的方法重载
                                            MethodInfo method = null;

                                            // 尝试找到参数名匹配的方法
                                            if (parameterDict.Count > 0)
                                            {
                                                // 首先尝试匹配具有复杂参数类型的方法（如DisplayNumberRequest）
                                                foreach (var m in methods)
                                                {
                                                    var parameters = m.GetParameters();
                                                    if (parameters.Length == 1 &&
                                                        !parameters[0].ParameterType.IsPrimitive &&
                                                        parameters[0].ParameterType != typeof(string) &&
                                                        parameters[0].ParameterType != typeof(decimal))
                                                    {
                                                        // 找到了一个带有复杂参数类型的方法
                                                        method = m;
                                                        complexParamType = parameters[0].ParameterType;
                                                        break;
                                                    }
                                                }

                                                // 如果没有找到带复杂参数的方法，尝试匹配参数名
                                                if (method == null)
                                                {
                                                    foreach (var m in methods)
                                                    {
                                                        var parameters = m.GetParameters();
                                                        // 检查第一个参数名是否与参数字典中的键匹配
                                                        if (parameters.Length > 0 &&
                                                            parameterDict.Keys.Any(key => string.Equals(key, parameters[0].Name, StringComparison.OrdinalIgnoreCase)))
                                                        {
                                                            method = m;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                            // 如果仍未找到方法，使用参数数量匹配
                                            if (method == null)
                                            {
                                                if (parameterDict.Count > 0)
                                                {
                                                    // 尝试匹配参数数量
                                                    var methodsWithMatchingParamCount = methods.Where(m => m.GetParameters().Length == parameterDict.Count).ToList();
                                                    if (methodsWithMatchingParamCount.Count == 1)
                                                    {
                                                        method = methodsWithMatchingParamCount[0];
                                                    }
                                                    else if (methodsWithMatchingParamCount.Count > 1)
                                                    {
                                                        // 如果有多个匹配的方法，选择第一个
                                                        method = methodsWithMatchingParamCount[0];
                                                    }
                                                }
                                                else
                                                {
                                                    // 没有参数，尝试找无参方法
                                                    var methodsWithNoParams = methods.Where(m => m.GetParameters().Length == 0).ToList();
                                                    if (methodsWithNoParams.Count > 0)
                                                    {
                                                        method = methodsWithNoParams[0];
                                                    }
                                                    else
                                                    {
                                                        // 如果没有无参方法，选择参数最少的方法
                                                        method = methods.OrderBy(m => m.GetParameters().Length).First();
                                                    }
                                                }
                                            }

                                            if (method == null)
                                            {
                                                throw new Exception($"无法找到匹配的方法: {methodName}，参数数量: {parameterDict.Count}");
                                            }

                                            // 获取方法的参数信息
                                            var methodParameters = method.GetParameters();
                                            var parameterValues = new object[methodParameters.Length];

                                            // 遍历方法的每个参数
                                            for (int i = 0; i < methodParameters.Length; i++)
                                            {
                                                var methodParam = methodParameters[i];
                                                var paramType = methodParam.ParameterType;

                                                // 如果参数是简单类型（基本类型、字符串等）
                                                if (paramType.IsPrimitive || paramType == typeof(string) || paramType == typeof(decimal))
                                                {
                                                    // 查找匹配的参数数据
                                                    if (parameterDict.TryGetValue(methodParam.Name, out string paramValue))
                                                    {
                                                        try
                                                        {
                                                            // 转换参数值到正确的类型
                                                            parameterValues[i] = Convert.ChangeType(paramValue, paramType);
                                                        }
                                                        catch
                                                        {
                                                            // 如果转换失败且参数有默认值，使用默认值
                                                            parameterValues[i] = methodParam.HasDefaultValue ? methodParam.DefaultValue : null;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // 如果找不到参数值且参数有默认值，使用默认值
                                                        parameterValues[i] = methodParam.HasDefaultValue ? methodParam.DefaultValue : null;
                                                    }
                                                }
                                                // 如果参数是复杂类型（类或结构体）
                                                else
                                                {
                                                    try
                                                    {
                                                        // 尝试直接将整个JSON反序列化为复杂对象
                                                        if (!string.IsNullOrWhiteSpace(data.postData) && data.postData != "{}")
                                                        {
                                                            parameterValues[i] = JsonConvert.DeserializeObject(data.postData, paramType);
                                                        }
                                                        else
                                                        {
                                                            // 创建参数对象实例
                                                            var paramInstance = Activator.CreateInstance(paramType);

                                                            // 获取参数类型的所有属性
                                                            var properties = paramType.GetProperties();

                                                            // 遍历属性并设置值
                                                            foreach (var property in properties)
                                                            {
                                                                if (parameterDict.TryGetValue(property.Name, out string propValue))
                                                                {
                                                                    try
                                                                    {
                                                                        // 转换并设置属性值
                                                                        var convertedValue = Convert.ChangeType(propValue, property.PropertyType);
                                                                        property.SetValue(paramInstance, convertedValue);
                                                                    }
                                                                    catch (Exception)
                                                                    {
                                                                    }
                                                                }
                                                            }

                                                            parameterValues[i] = paramInstance;
                                                        }
                                                    }
                                                    catch (Exception)
                                                    {
                                                        // 创建一个空实例
                                                        parameterValues[i] = Activator.CreateInstance(paramType);
                                                    }
                                                }
                                            }

                                            // 执行方法
                                            var result = method.Invoke(instance, parameterValues);

                                            // 如果是异步方法，等待结果
                                            if (result is Task task)
                                            {
                                                await task;
                                                // 获取Task的实际结果（如果有）
                                                if (task.GetType().IsGenericType)
                                                {
                                                    result = ((dynamic)task).Result;
                                                }
                                            }

                                            // 处理返回结果
                                            if (result != null)
                                            {
                                                var appResponse = JsonConvert.DeserializeObject<AppResponseBase>(JsonConvert.SerializeObject(result));
                                                invokeResponse.Success = appResponse.Success ?? true;
                                                invokeResponse.ErrorMessage = appResponse.ErrorMessage;
                                                invokeResponse.Data = appResponse.Data?.ToString() ?? "OK";
                                            }
                                            else
                                            {
                                                invokeResponse.Data = "OK";
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // 处理错误情况
                                        Console.WriteLine($"[错误] 执行方法时出错: {ex.Message}");
                                        invokeResponse = new InvokeResponse()
                                        {
                                            Success = false,
                                            ErrorMessage = ex.Message,
                                            Data = null
                                        };
                                    }
                                    #endregion

                                    //回传
                                    await NCBConnection.InvokeAsync("InvokeEdgeApiResult", msgId, invokeResponse);
                                });
                            }
                            #endregion
                        }
                    }
                    
                    // EdgeDataPush方法定时执行逻辑（每次线程执行都会检查）
                    if (NCBConnection != null && NCBConnection.State == HubConnectionState.Connected && _edgeDataPushMethods.Count > 0)
                    {
                        var now = DateTime.Now;
                        
                        // 优化：恢复时间检查逻辑，避免过于频繁执行
                        if ((now - _lastEdgeDataPushTime).TotalMilliseconds >= _edgeDataPushIntervalMilliseconds)
                        {
                            // 优化：背压控制，避免重复执行
                            if (_isExecutingEdgeDataPush)
                            {
                                return;
                            }
                            
                            _isExecutingEdgeDataPush = true;
                            
                            
                            try
                            {
                                // 优化：并行执行多个方法，提高效率
                                var tasks = new List<Task>();
                                
                                foreach (var (method, attribute, declaringType) in _edgeDataPushMethods)
                                {
                                    var methodKey = $"{declaringType.FullName}-{method.Name}";
                                    
                                                                         // 优化：检查错误重试机制
                                     if (_methodErrorCounts.ContainsKey(methodKey) && 
                                         _methodErrorCounts[methodKey] >= _maxErrorCount)
                                     {
                                         // 检查是否过了冷却时间
                                         if (_methodLastErrorTime.ContainsKey(methodKey) && 
                                             (now - _methodLastErrorTime[methodKey]).TotalSeconds < _errorCooldownSeconds)
                                         {
                                             continue; // 跳过错误过多的方法
                                         }
                                         else
                                         {
                                             // 重置错误计数
                                             _methodErrorCounts[methodKey] = 0;
                                         }
                                     }

                                     if((DateTime.Now- _lastEdgeDataPushTime).TotalMilliseconds< attribute.IntervalMilliseconds){
                                        continue;
                                     }
                                    
                                    // 创建异步任务
                                    var task = ExecuteEdgeDataPushMethod(app, method, attribute, declaringType, methodKey);
                                    tasks.Add(task);
                                }
                                
                                // 等待所有任务完成
                                if (tasks.Count > 0)
                                {
                                    await Task.WhenAll(tasks);
                                }
                            }
                            finally
                            {
                                _isExecutingEdgeDataPush = false;
                            }


                            _lastEdgeDataPushTime = now;
                        }
                    }
                    
                    //获取Token
                    if(NCBConnection!=null && NCBConnection.State==HubConnectionState.Connected){
                        try
                        {
                            if (CenterDefinition.token == null || CenterDefinition.token.ExpireTime < DateTime.Now.AddMinutes(5))
                            {
                                // 传递当前的token作为oldToken参数
                                //var currentToken = CenterDefinition.token?.Token ?? "";
                                var token = await NCBConnection.InvokeAsync<GetTokenResult>("GetToken");
                                if (token != null)
                                {
                                    CenterDefinition.token = token;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 获取Token失败: {ex.Message}");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] NCBConnection出现异常: {ex.Message}");
                    SenparcTrace.SendCustomLog("NCBConnection", $"NCBConnection出现异常：{ex.Message}");
                }
            }));


        }

        /// <summary>
        /// 优化：执行单个EdgeDataPush方法的异步任务
        /// </summary>
        private async Task ExecuteEdgeDataPushMethod(Microsoft.AspNetCore.Builder.IApplicationBuilder app, MethodInfo method, EdgeDataPushAttribute attribute, Type declaringType, string methodKey)
        {
            try
            {
                object instance = null;
                object result = null;

                // 如果是实例方法，需要创建实例
                if (!method.IsStatic)
                {
                    // 优化：使用实例缓存
                    lock (_instanceCacheLock)
                    {
                        if (!_instanceCache.TryGetValue(declaringType, out instance))
                        {
                            try
                            {
                                // 尝试通过依赖注入创建实例
                                var scope = app.ApplicationServices.CreateScope();
                                //using (var scope = app.ApplicationServices.CreateScope())
                                //{
                                    instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, declaringType);
                                    _instanceCache[declaringType] = instance;
                                //}
                            }
                            catch
                            {
                                // 如果依赖注入失败，尝试使用默认构造函数
                                instance = Activator.CreateInstance(declaringType);
                                _instanceCache[declaringType] = instance;
                            }
                        }
                    }
                }

                // 执行方法
                if (method.IsStatic)
                {
                    result = method.Invoke(null, null);
                }
                else
                {
                    result = method.Invoke(instance, null);
                }

                // 如果是异步方法，等待结果
                if (result is Task task)
                {
                    await task;
                    // 获取Task的实际结果（如果有）
                    if (task.GetType().IsGenericType)
                    {
                        result = ((dynamic)task).Result;
                    }
                    else
                    {
                        result = "OK"; // 对于Task（无返回值）
                    }
                }

                // 优化：减少JSON序列化开销
                string resultData = "OK";
                if (result != null)
                {
                    try
                    {
                        var appResponse = JsonConvert.DeserializeObject<AppResponseBase>(JsonConvert.SerializeObject(result));
                        resultData = appResponse?.Data?.ToString() ?? "OK";
                    }
                    catch
                    {
                        resultData = result.ToString();
                    }
                }

                // 准备推送的数据
                var pushData = new
                {
                    MethodName = method.Name,
                    ClassName = declaringType.Name,
                    FullMethodName = methodKey,
                    Description = attribute.Description,
                    Result = resultData,
                    Timestamp = DateTime.Now,
                    IntervalMilliseconds = _edgeDataPushIntervalMilliseconds
                };

                // 通过NCBConnection推送数据
                await NCBConnection.InvokeAsync("PushEdgeRealData", pushData);
                
                // 优化：成功执行后重置错误计数
                if (_methodErrorCounts.ContainsKey(methodKey))
                {
                    _methodErrorCounts[methodKey] = 0;
                }
                
                //Console.WriteLine($"EdgeDataPush推送成功: {pushData.FullMethodName}, 结果: {pushData?.Result ?? "null"}");
            }
            catch (Exception ex)
            {
                // 优化：记录错误次数
                if (!_methodErrorCounts.ContainsKey(methodKey))
                {
                    _methodErrorCounts[methodKey] = 0;
                }
                _methodErrorCounts[methodKey]++;
                _methodLastErrorTime[methodKey] = DateTime.Now;
                
                Console.WriteLine($"[错误] 执行EdgeDataPush方法 {method.Name} 时出错 (错误次数: {_methodErrorCounts[methodKey]}): {ex.Message}");
                SenparcTrace.SendCustomLog("EdgeDataPush", $"执行方法 {declaringType.FullName}.{method.Name} 时出错 (错误次数: {_methodErrorCounts[methodKey]}): {ex.Message}");
            }
        }
    }




    public class XncfRegisterItem
    {
        public IXncfRegister XncfRegister { get; set; }
        public List<RegisterFunctionInfo> RegisterFunctionInfoList { get; set; }


        public class RegisterFunctionInfo
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public FunctionRenderBag FunctionRenderBag { get; set; }
            public List<FunctionParameterInfo> FunctionParameterInfoList { get; set; }
        }
    }


    #region 同步接口至neuchar.com
    public class DevicePoolFunction_DeviceSyncVD
    {
        public string DID { get; set; }
        public string UID { get; set; }

        public string FunctionTools { get; set; }
        public string FunctionToolsForChat { get; set; }
        /// <summary>
        /// 接口列表
        /// </summary>
        public List<DevicePoolFunctionInterface_VD> DevicePoolFunctionInterfaces { get; set; }

        /// <summary>
        /// 监听器列表
        /// </summary>
        public List<DevicePoolFunctionListener_VD> DevicePoolFunctionListeners { get; set; }
    }
    /// <summary>
    /// 设备池功能接口的请求参数
    /// </summary>
    public class DevicePoolFunctionInterface_VD
    {
        /// <summary>
        /// Id，更新时必填
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 接口名称
        /// </summary>
        public string InterfaceName { get; set; }

        /// <summary>
        /// 接口描述
        /// </summary>
        public string InterfaceDescription { get; set; }

        /// <summary>
        /// 返回消息类型
        /// </summary>
        public string ReturnMessageType { get; set; }

        /// <summary>
        /// 参数列表
        /// </summary>
        public List<DevicePoolFunctionInterfaceParameter_VD> DevicePoolFunctionInterfaceParameters { get; set; }
    }

    /// <summary>
    /// 设备池功能接口参数的请求参数
    /// </summary>
    public class DevicePoolFunctionInterfaceParameter_VD
    {
        /// <summary>
        /// Id，更新时必填
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 参数名称
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// 参数描述
        /// </summary>
        public string ParameterDescription { get; set; }

        /// <summary>
        /// 参数类型
        /// </summary>
        public string ParameterType { get; set; }
    }

    /// <summary>
    /// 设备池功能监听器的请求参数
    /// </summary>
    public class DevicePoolFunctionListener_VD
    {
        /// <summary>
        /// Id，更新时必填
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 返回参数
        /// </summary>
        public string ReturnParameter { get; set; }

        /// <summary>
        /// 返回参数数据类型
        /// </summary>
        public string ReturnParameterDataType { get; set; }

        /// <summary>
        /// 描述文字
        /// </summary>
        public string Description { get; set; }
    }
    #endregion

    #region 接收UI的通知
    public class DevicePoolFunction_SendDeviceFunctionTriggerVD
    {
        // 接受
        // {
        //     DID: 'xxx',
        //     UID: 'xxx',
        //     interfaceId: 'start',
        //     interfaceName: 'start',
        //     parameterData: [
        //         {
        //             parameterName: "mode",
        //             parameterValue: "auto"
        //         },
        //         {
        //             parameterName: "initialTemp",
        //             parameterValue: 20
        //         }
        //     ]
        // }
        // 返回
        // {
        //     DID: 'xxx',
        //     UID: 'xxx',
        //     interfaceId: 'start',
        //     interfaceName: 'start',
        //     parameterData: [
        //         {
        //             parameterName: "result",
        //             parameterValue: "执行完成"
        //         }
        //     ]
        // }
        public string DID { get; set; }
        public string UID { get; set; }
        public string InterfaceId { get; set; }
        public string InterfaceName { get; set; }
        public List<DevicePoolFunction_ParameterDataVD> ParameterData { get; set; }
    }
    public class DevicePoolFunction_ParameterDataVD
    {
        public string ParameterName { get; set; }
        public string ParameterValue { get; set; }
    }
    #endregion

    #region 接受NCB推送消息
    public class InvokeRequest
    {
        /// <summary>
        /// 设备did
        /// </summary>
        public string did { get; set; }
        /// <summary>
        /// 设备uid
        /// </summary>
        public string uid { get; set; }
        /// <summary>
        /// 设备接口方法名
        /// </summary>
        public string functionName { get; set; }
        /// <summary>
        /// 设备接口入参的JSON字符串
        /// </summary>
        public string postData { get; set; }
    }
    public class InvokeResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
        /// <summary>
        /// 返回结果
        /// </summary>
        public string Data { get; set; }
    }
    #endregion

}
