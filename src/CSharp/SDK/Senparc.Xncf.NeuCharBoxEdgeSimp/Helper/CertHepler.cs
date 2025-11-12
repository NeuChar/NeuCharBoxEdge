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
        /// 从PEM文件中读取RSA私钥
        /// </summary>
        /// <returns>RSA私钥对象</returns>
        private static RSA LoadPrivateKeyFromPem()
        {
            string privateKeyPath = GetPrivateKeyPath();
            
            if (!File.Exists(privateKeyPath))
            {
                throw new FileNotFoundException($"私钥文件不存在: {privateKeyPath}");
            }

            string pemContent = File.ReadAllText(privateKeyPath);
            
            // 移除PEM头尾和换行符，获取纯Base64内容
            string base64Key = pemContent
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
                .Replace("-----END RSA PRIVATE KEY-----", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            byte[] keyBytes = Convert.FromBase64String(base64Key);
            
            RSA rsa = RSA.Create();
            
            try
            {
                // 尝试作为PKCS#8格式导入
                rsa.ImportPkcs8PrivateKey(keyBytes, out _);
            }
            catch
            {
                try
                {
                    // 如果PKCS#8失败，尝试作为RSA私钥格式导入
                    rsa.ImportRSAPrivateKey(keyBytes, out _);
                }
                catch (Exception ex)
                {
                    rsa.Dispose();
                    throw new InvalidOperationException($"无法解析私钥文件: {ex.Message}");
                }
            }
            
            return rsa;
        }

        /// <summary>
        /// 从PEM格式字符串中加载RSA公钥
        /// </summary>
        /// <param name="pemContent">PEM格式的公钥字符串</param>
        /// <returns>RSA公钥对象</returns>
        private static RSA LoadPublicKeyFromPemString(string pemContent)
        {
            if (string.IsNullOrEmpty(pemContent))
            {
                throw new ArgumentException("PEM公钥内容不能为空", nameof(pemContent));
            }

            // 移除PEM头尾和换行符，获取纯Base64内容
            string base64Key = pemContent
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("-----BEGIN RSA PUBLIC KEY-----", "")
                .Replace("-----END RSA PUBLIC KEY-----", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            byte[] keyBytes = Convert.FromBase64String(base64Key);
            
            RSA rsa = RSA.Create();
            
            try
            {
                // 尝试作为SPKI格式导入
                rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }
            catch
            {
                try
                {
                    // 如果SPKI失败，尝试作为RSA公钥格式导入
                    rsa.ImportRSAPublicKey(keyBytes, out _);
                }
                catch (Exception ex)
                {
                    rsa.Dispose();
                    throw new InvalidOperationException($"无法解析公钥字符串: {ex.Message}");
                }
            }
            
            return rsa;
        }

        /// <summary>
        /// 使用私钥对指定字符串进行RSA加密（签名操作）
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
                using (RSA rsa = LoadPrivateKeyFromPem())
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    
                    // 使用私钥进行签名（相当于私钥"加密"）
                    // 使用SHA256哈希算法和PSS填充进行签名
                    byte[] signatureBytes = rsa.SignData(plainBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                    
                    return Convert.ToBase64String(signatureBytes);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"RSA私钥加密失败: {ex.Message}", ex);
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
                using (RSA rsa = LoadPrivateKeyFromPem())
                {
                    byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                    
                    // 使用私钥进行解密（解密公钥加密的数据）
                    // 使用OAEP SHA256填充进行解密
                    byte[] decryptedBytes = rsa.Decrypt(encryptedBytes, RSAEncryptionPadding.OaepSHA256);
                    
                    return Encoding.UTF8.GetString(decryptedBytes);
                }
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
                using (RSA rsa = LoadPublicKeyFromPemString(publicKeyPem))
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    
                    // 使用公钥进行加密
                    // 使用OAEP SHA256填充进行加密
                    byte[] encryptedBytes = rsa.Encrypt(plainBytes, RSAEncryptionPadding.OaepSHA256);
                    
                    return Convert.ToBase64String(encryptedBytes);
                }
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
                using (RSA rsa = LoadPublicKeyFromPemString(publicKeyPem))
                {
                    byte[] dataBytes = Encoding.UTF8.GetBytes(originalData);
                    byte[] signatureBytes = Convert.FromBase64String(signature);
                    
                    // 使用公钥验证签名
                    // 使用SHA256哈希算法和PSS填充进行验签
                    return rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"验证签名失败: {ex.Message}", ex);
            }
        }
    }
}
