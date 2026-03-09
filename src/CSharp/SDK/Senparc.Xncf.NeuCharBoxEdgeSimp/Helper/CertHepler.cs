using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Helper
{
    public class CertHepler
    {
        public const string HELLO_DEVICE = "HELLO NCB_EDGE_DEVICE";
        private const string DEFAULT_PRIVATE_KEY_SUFFIX = "_private_key.pem";
        private const string CERT_FOLDER_NAME = "Cert";

        private static SenderReceiverSet _senderReceiverSet;

        /// <summary>
        /// 设置SenderReceiverSet对象（用于获取DID）
        /// </summary>
        /// <param name="senderReceiverSet">SenderReceiverSet对象</param>
        public static void SetSenderReceiverSet(SenderReceiverSet senderReceiverSet)
        {
            _senderReceiverSet = senderReceiverSet;
        }

        /// <summary>
        /// 获取私钥文件名（基于DID构建：{DID}_private_key.pem）
        /// </summary>
        /// <returns>私钥文件名</returns>
        private static string GetPrivateKeyFileName()
        {
            string did = _senderReceiverSet?.dId ?? "DID";
            return $"{did}{DEFAULT_PRIVATE_KEY_SUFFIX}";
        }

        /// <summary>
        /// 检查当前程序根目录下Cert文件夹下是否存在DID_private_key.pem私钥文件
        /// </summary>
        /// <returns>如果私钥文件存在返回true，否则返回false</returns>
        public static bool CheckPrivateKeyExists()
        {
            try
            {
                string appRootPath = AppDomain.CurrentDomain.BaseDirectory;
                string certFolderPath = Path.Combine(appRootPath, CERT_FOLDER_NAME);
                string privateKeyPath = Path.Combine(certFolderPath, GetPrivateKeyFileName());

                return File.Exists(privateKeyPath);
            }
            catch (Exception ex)
            {
                // 记录异常信息（可以根据项目需要使用相应的日志框架）
                Console.WriteLine($"检查私钥文件时发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取私钥文件的完整路径
        /// </summary>
        /// <returns>私钥文件的完整路径</returns>
        private static string GetPrivateKeyPath()
        {
            string appRootPath = AppDomain.CurrentDomain.BaseDirectory;
            string certFolderPath = Path.Combine(appRootPath, CERT_FOLDER_NAME);
            return Path.Combine(certFolderPath, GetPrivateKeyFileName());
        }

        /// <summary>
        /// 使用私钥对指定字符串进行RSA签名（签名操作）
        /// 注意：这不是真正的加密，而是数字签名的一种变体用法
        /// </summary>
        /// <param name="plainText">要"加密"的明文字符串</param>
        /// <returns>"加密"后的Base64字符串</returns>
        public static string RsaEncryptWithPrivateKey(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                throw new ArgumentException("明文字符串不能为空", nameof(plainText));
            }

            try
            {
                string privateKeyPath = GetPrivateKeyPath();
                if (!File.Exists(privateKeyPath))
                {
                    throw new FileNotFoundException($"私钥文件不存在: {privateKeyPath}");
                }

                string pemContent = File.ReadAllText(privateKeyPath);
                // 调用 RSAEncryptionUtility 进行私钥签名
                return Senparc.Utility.RSAEncryptionUtility.EncryptWithPrivateKey(plainText, pemContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"RSA私钥签名失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用私钥对指定字符串进行RSA解密
        /// </summary>
        /// <param name="encryptedText">要解密的Base64编码密文字符串（通常是公钥加密的数据）</param>
        /// <returns>解密后的明文字符串</returns>
        public static string RsaDecryptWithPrivateKey(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
            {
                throw new ArgumentException("密文字符串不能为空", nameof(encryptedText));
            }

            try
            {
                string privateKeyPath = GetPrivateKeyPath();
                if (!File.Exists(privateKeyPath))
                {
                    throw new FileNotFoundException($"私钥文件不存在: {privateKeyPath}");
                }

                string pemContent = File.ReadAllText(privateKeyPath);
                // 调用 RSAEncryptionUtility 进行私钥解密
                return Senparc.Utility.RSAEncryptionUtility.DecryptWithPrivateKey(encryptedText, pemContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"RSA私钥解密失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用公钥对指定字符串进行RSA加密
        /// </summary>
        /// <param name="plainText">要加密的明文字符串</param>
        /// <param name="publicKeyPem">PEM格式的公钥字符串</param>
        /// <returns>加密后的Base64字符串</returns>
        public static string RsaEncryptWithPublicKey(string plainText, string publicKeyPem)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                throw new ArgumentException("明文字符串不能为空", nameof(plainText));
            }

            if (string.IsNullOrEmpty(publicKeyPem))
            {
                throw new ArgumentException("公钥不能为空", nameof(publicKeyPem));
            }

            try
            {
                // 调用 RSAEncryptionUtility 进行公钥加密
                return Senparc.Utility.RSAEncryptionUtility.EncryptWithPublicKey(plainText, publicKeyPem);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"RSA公钥加密失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 使用公钥验证数字签名
        /// </summary>
        /// <param name="originalData">原始数据字符串</param>
        /// <param name="signature">Base64编码的签名字符串</param>
        /// <param name="publicKeyPem">PEM格式的公钥字符串</param>
        /// <returns>验签结果，true表示验签成功</returns>
        public static bool VerifySignatureWithPublicKey(string originalData, string signature, string publicKeyPem)
        {
            if (string.IsNullOrEmpty(originalData))
            {
                throw new ArgumentException("原始数据不能为空", nameof(originalData));
            }

            if (string.IsNullOrEmpty(signature))
            {
                throw new ArgumentException("签名字符串不能为空", nameof(signature));
            }

            if (string.IsNullOrEmpty(publicKeyPem))
            {
                throw new ArgumentException("公钥不能为空", nameof(publicKeyPem));
            }

            try
            {
                // 调用 RSAEncryptionUtility 进行公钥验签
                return Senparc.Utility.RSAEncryptionUtility.DecryptWithPublicKey(originalData, signature, publicKeyPem);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"验证签名失败: {ex.Message}", ex);
            }
        }
    }
}
