using System;
using System.Reflection;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Attributes
{
    /// <summary>
    /// EdgeDataPush特性，用于标记需要推送边缘数据的方法
    /// 只允许没有参数的方法标注此特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class EdgeDataPushAttribute : Attribute
    {
        /// <summary>
        /// 数据推送描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 推送间隔（毫秒）
        /// </summary>
        public int IntervalMilliseconds { get; set; } = 90;

        /// <summary>
        /// EdgeDataPush特性构造函数
        /// </summary>
        /// <param name="description">数据推送描述</param>
        /// <param name="intervalMilliseconds">推送间隔（毫秒），默认90毫秒</param>
        public EdgeDataPushAttribute(string description = "", int intervalMilliseconds = 90)
        {
            Description = description;
            IntervalMilliseconds = intervalMilliseconds;
        }

        /// <summary>
        /// 验证方法是否符合要求（无参数）
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public static bool IsValidMethod(MethodInfo method)
        {
            // 检查方法是否有参数，只允许无参数的方法
            return method.GetParameters().Length == 0;
        }
    }
} 