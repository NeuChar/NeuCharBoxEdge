using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services.Crypto
{
    /// <summary>
    /// 静态加密服务类
    /// Static Crypto Service Class
    /// </summary>
    public static class CryptoService
    {
        /// <summary>
        /// RSA加密
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <param name="publicKeyPem">公钥PEM格式</param>
        /// <returns>加密后的Base64字符串</returns>
        public static async Task<string> RsaEncryptAsync(string plainText, string publicKeyPem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var encryptedBytes = rsa.Encrypt(plainBytes, RSAEncryptionPadding.OaepSHA256);
                
                await Task.CompletedTask;
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("RSA加密失败", ex);
            }
        }

        /// <summary>
        /// RSA解密
        /// </summary>
        /// <param name="cipherText">密文Base64字符串</param>
        /// <param name="privateKeyPem">私钥PEM格式</param>
        /// <returns>解密后的明文</returns>
        public static async Task<string> RsaDecryptAsync(string cipherText, string privateKeyPem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                
                var encryptedBytes = Convert.FromBase64String(cipherText);
                var decryptedBytes = rsa.Decrypt(encryptedBytes, RSAEncryptionPadding.OaepSHA256);
                
                await Task.CompletedTask;
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("RSA解密失败", ex);
            }
        }

        /// <summary>
        /// RSA签名
        /// </summary>
        /// <param name="data">待签名数据</param>
        /// <param name="privateKeyPem">私钥PEM格式</param>
        /// <returns>签名Base64字符串</returns>
        public static async Task<string> RsaSignAsync(string data, string privateKeyPem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                
                var dataBytes = Encoding.UTF8.GetBytes(data);
                var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                
                await Task.CompletedTask;
                return Convert.ToBase64String(signatureBytes);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("RSA签名失败", ex);
            }
        }

        /// <summary>
        /// RSA验签
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="signature">签名Base64字符串</param>
        /// <param name="publicKeyPem">公钥PEM格式</param>
        /// <returns>验签结果</returns>
        public static async Task<bool> RsaVerifyAsync(string data, string signature, string publicKeyPem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                
                var dataBytes = Encoding.UTF8.GetBytes(data);
                var signatureBytes = Convert.FromBase64String(signature);
                
                var isValid = rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                
                await Task.CompletedTask;
                return isValid;
            }
            catch (Exception ex)
            {
                // 验签失败时返回false而不是抛出异常
                return false;
            }
        }

        /// <summary>
        /// AES加密
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <param name="key">密钥Base64字符串</param>
        /// <returns>加密结果（加密数据和IV）</returns>
        public static async Task<(string encryptedData, string iv)> AesEncryptAsync(string plainText, string key)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = Convert.FromBase64String(key);
                aes.GenerateIV();
                
                var iv = Convert.ToBase64String(aes.IV);
                
                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using var swEncrypt = new StreamWriter(csEncrypt);
                
                await swEncrypt.WriteAsync(plainText);
                await swEncrypt.FlushAsync();
                csEncrypt.FlushFinalBlock();
                
                var encryptedData = Convert.ToBase64String(msEncrypt.ToArray());
                return (encryptedData, iv);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("AES加密失败", ex);
            }
        }

        /// <summary>
        /// AES解密
        /// </summary>
        /// <param name="encryptedData">加密数据Base64字符串</param>
        /// <param name="key">密钥Base64字符串</param>
        /// <param name="iv">初始化向量Base64字符串</param>
        /// <returns>解密后的明文</returns>
        public static async Task<string> AesDecryptAsync(string encryptedData, string key, string iv)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = Convert.FromBase64String(key);
                aes.IV = Convert.FromBase64String(iv);
                
                using var decryptor = aes.CreateDecryptor();
                using var msDecrypt = new MemoryStream(Convert.FromBase64String(encryptedData));
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                
                var decryptedText = await srDecrypt.ReadToEndAsync();
                return decryptedText;
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("AES解密失败", ex);
            }
        }

        /// <summary>
        /// 生成随机密钥
        /// </summary>
        /// <param name="length">密钥字节长度</param>
        /// <returns>Base64编码的随机密钥</returns>
        public static string GenerateRandomKey(int length = 32)
        {
            try
            {
                using var rng = RandomNumberGenerator.Create();
                var keyBytes = new byte[length];
                rng.GetBytes(keyBytes);
                return Convert.ToBase64String(keyBytes);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("生成随机密钥失败", ex);
            }
        }

        /// <summary>
        /// 从文件加载密钥
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>密钥内容</returns>
        public static async Task<string> LoadKeyFromFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"密钥文件不存在: {filePath}");
                }
                
                var key = await File.ReadAllTextAsync(filePath);
                return key.Trim();
            }
            catch (Exception ex) when (!(ex is FileNotFoundException))
            {
                throw new CryptoOperationException($"从文件加载密钥失败: {filePath}", ex);
            }
        }

        /// <summary>
        /// 保存密钥到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="key">密钥内容</param>
        public static async Task SaveKeyToFileAsync(string filePath, string key)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(filePath, key);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException($"保存密钥到文件失败: {filePath}", ex);
            }
        }

        /// <summary>
        /// 生成RSA密钥对
        /// </summary>
        /// <param name="keySize">密钥大小（位）</param>
        /// <returns>公钥和私钥的PEM格式</returns>
        public static async Task<(string publicKey, string privateKey)> GenerateRsaKeyPairAsync(int keySize = 2048)
        {
            try
            {
                using var rsa = RSA.Create(keySize);
                
                var publicKey = rsa.ExportRSAPublicKeyPem();
                var privateKey = rsa.ExportRSAPrivateKeyPem();
                
                await Task.CompletedTask;
                return (publicKey, privateKey);
            }
            catch (Exception ex)
            {
                throw new CryptoOperationException("生成RSA密钥对失败", ex);
            }
        }
    }

    /// <summary>
    /// 加密操作异常
    /// </summary>
    public class CryptoOperationException : Exception
    {
        public CryptoOperationException(string message) : base(message) { }
        public CryptoOperationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// 加密工具类
    /// </summary>
    public static class CryptoUtils
    {
        /// <summary>
        /// 生成随机字符串
        /// </summary>
        /// <param name="length">字符串长度</param>
        /// <returns>随机字符串</returns>
        public static string GenerateRandomString(int length = 16)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            using var rng = RandomNumberGenerator.Create();
            var result = new char[length];
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[bytes[i] % chars.Length];
            }
            
            return new string(result);
        }

        /// <summary>
        /// 计算SHA256哈希
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <returns>Base64编码的哈希值</returns>
        public static string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 验证时间戳是否在有效范围内
        /// </summary>
        /// <param name="timestamp">Unix时间戳</param>
        /// <param name="validityMinutes">有效时间范围（分钟）</param>
        /// <returns>是否有效</returns>
        public static bool IsTimestampValid(long timestamp, int validityMinutes = 5)
        {
            var timestampDateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
            var now = DateTime.UtcNow;
            var diff = Math.Abs((now - timestampDateTime).TotalMinutes);
            return diff <= validityMinutes;
        }

        /// <summary>
        /// 生成GUID字符串
        /// </summary>
        /// <returns>GUID字符串</returns>
        public static string GenerateGuid()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// 生成当前时间戳
        /// </summary>
        /// <returns>Unix时间戳</returns>
        public static long GenerateTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        /// <summary>
        /// 安全比较两个字符串（防止时序攻击）
        /// </summary>
        /// <param name="a">字符串A</param>
        /// <param name="b">字符串B</param>
        /// <returns>是否相等</returns>
        public static bool SecureEquals(string a, string b)
        {
            if (a?.Length != b?.Length)
                return false;

            var result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }
    }
} 