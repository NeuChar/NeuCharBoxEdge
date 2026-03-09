using System;
using System.Security.Cryptography;
using System.Text;

namespace Senparc.Utility
{
    /// <summary>
    /// RSA非对称加解密工具类
    /// 提供密钥生成、加密解密、数字签名和验签等功能
    /// </summary>
    public class RSAEncryptionUtility
    {
        /// <summary>
        /// RSA密钥对结构
        /// </summary>
        public class RSAKeyPair
        {
            /// <summary>
            /// 公钥 (PEM格式)
            /// </summary>
            public string PublicKey { get; set; }

            /// <summary>
            /// 私钥 (PEM格式)
            /// </summary>
            public string PrivateKey { get; set; }

            /// <summary>
            /// 公钥 (XML格式) - 向后兼容
            /// </summary>
            public string PublicKeyXml { get; set; }

            /// <summary>
            /// 私钥 (XML格式) - 向后兼容
            /// </summary>
            public string PrivateKeyXml { get; set; }
        }

        #region 密钥生成

        /// <summary>
        /// 生成RSA密钥对
        /// </summary>
        /// <param name="keySize">密钥长度，默认2048位，可选1024、2048、4096</param>
        /// <returns>RSA密钥对</returns>
        public static RSAKeyPair GenerateKeyPair(int keySize = 2048)
        {
            using (var rsa = RSA.Create(keySize))
            {
                return new RSAKeyPair
                {
                    PublicKey = ExportPublicKeyToPem(rsa),
                    PrivateKey = ExportPrivateKeyToPem(rsa),
                    PublicKeyXml = rsa.ToXmlString(false),
                    PrivateKeyXml = rsa.ToXmlString(true)
                };
            }
        }

        /// <summary>
        /// 将RSA公钥导出为PEM格式
        /// </summary>
        /// <param name="rsa">RSA实例</param>
        /// <returns>PEM格式公钥</returns>
        private static string ExportPublicKeyToPem(RSA rsa)
        {
            var publicKeyBytes = rsa.ExportRSAPublicKey();
            return ConvertDerToPem(publicKeyBytes, "RSA PUBLIC KEY");
        }

        /// <summary>
        /// 将RSA私钥导出为PEM格式
        /// </summary>
        /// <param name="rsa">RSA实例</param>
        /// <returns>PEM格式私钥</returns>
        private static string ExportPrivateKeyToPem(RSA rsa)
        {
            var privateKeyBytes = rsa.ExportRSAPrivateKey();
            return ConvertDerToPem(privateKeyBytes, "RSA PRIVATE KEY");
        }

        /// <summary>
        /// 将DER格式字节数组转换为PEM格式字符串
        /// </summary>
        /// <param name="derBytes">DER格式字节数组</param>
        /// <param name="label">PEM标签</param>
        /// <returns>PEM格式字符串</returns>
        private static string ConvertDerToPem(byte[] derBytes, string label)
        {
            var base64 = Convert.ToBase64String(derBytes);
            var sb = new StringBuilder();
            
            sb.AppendLine($"-----BEGIN {label}-----");
            
            // 每64个字符换行
            for (int i = 0; i < base64.Length; i += 64)
            {
                var length = Math.Min(64, base64.Length - i);
                sb.AppendLine(base64.Substring(i, length));
            }
            
            sb.AppendLine($"-----END {label}-----");
            
            return sb.ToString();
        }

        #endregion

        #region 公钥加密、私钥解密

        /// <summary>
        /// 使用公钥加密数据
        /// </summary>
        /// <param name="data">待加密的数据</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>加密后的Base64字符串</returns>
        public static string EncryptWithPublicKey(string data, string publicKey, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = CreateRsaFromPemPublicKey(publicKey))
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] encryptedBytes = rsa.Encrypt(dataBytes, padding);
                return Convert.ToBase64String(encryptedBytes);
            }
        }

        /// <summary>
        /// 使用公钥加密字节数组
        /// </summary>
        /// <param name="data">待加密的字节数组</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>加密后的字节数组</returns>
        public static byte[] EncryptWithPublicKey(byte[] data, string publicKey, RSAEncryptionPadding padding = null)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = CreateRsaFromPemPublicKey(publicKey))
            {
                return rsa.Encrypt(data, padding);
            }
        }

        /// <summary>
        /// 使用私钥解密数据
        /// </summary>
        /// <param name="encryptedData">加密后的Base64字符串</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>解密后的原始数据</returns>
        public static string DecryptWithPrivateKey(string encryptedData, string privateKey, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(encryptedData))
                throw new ArgumentException("加密数据不能为空", nameof(encryptedData));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedData);
                byte[] decryptedBytes = rsa.Decrypt(encryptedBytes, padding);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }

        /// <summary>
        /// 使用私钥解密字节数组
        /// </summary>
        /// <param name="encryptedData">加密后的字节数组</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>解密后的字节数组</returns>
        public static byte[] DecryptWithPrivateKey(byte[] encryptedData, string privateKey, RSAEncryptionPadding padding = null)
        {
            if (encryptedData == null || encryptedData.Length == 0)
                throw new ArgumentException("加密数据不能为空", nameof(encryptedData));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                return rsa.Decrypt(encryptedData, padding);
            }
        }

        #endregion

        #region 私钥加密、公钥解密 (数字签名的变种用法)

        /// <summary>
        /// 使用私钥"加密"数据 (实际上是签名操作)
        /// 注意：这不是真正的加密，而是数字签名的一种变体用法
        /// </summary>
        /// <param name="data">待"加密"的数据</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>"加密"后的Base64字符串</returns>
        public static string EncryptWithPrivateKey(string data, string privateKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] signatureBytes = rsa.SignData(dataBytes, hashAlgorithm, padding);
                return Convert.ToBase64String(signatureBytes);
            }
        }

        /// <summary>
        /// 使用公钥"解密"数据 (实际上是验证签名)
        /// 注意：这不是真正的解密，而是验证数字签名
        /// </summary>
        /// <param name="originalData">原始数据</param>
        /// <param name="signature">"加密"后的Base64字符串(实际上是签名)</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>验证是否成功</returns>
        public static bool DecryptWithPublicKey(string originalData, string signature, string publicKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(originalData))
                throw new ArgumentException("原始数据不能为空", nameof(originalData));
            if (string.IsNullOrEmpty(signature))
                throw new ArgumentException("签名不能为空", nameof(signature));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = CreateRsaFromPemPublicKey(publicKey))
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(originalData);
                byte[] signatureBytes = Convert.FromBase64String(signature);
                return rsa.VerifyData(dataBytes, signatureBytes, hashAlgorithm, padding);
            }
        }

        #endregion

        #region 数字签名和验签

        /// <summary>
        /// 使用私钥对数据进行数字签名
        /// </summary>
        /// <param name="data">待签名的数据</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>数字签名的Base64字符串</returns>
        public static string SignData(string data, string privateKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] signatureBytes = rsa.SignData(dataBytes, hashAlgorithm, padding);
                return Convert.ToBase64String(signatureBytes);
            }
        }

        /// <summary>
        /// 使用私钥对字节数组进行数字签名
        /// </summary>
        /// <param name="data">待签名的字节数组</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>数字签名的字节数组</returns>
        public static byte[] SignData(byte[] data, string privateKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                return rsa.SignData(data, hashAlgorithm, padding);
            }
        }

        /// <summary>
        /// 使用公钥验证数字签名
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="signature">数字签名的Base64字符串</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>验证结果，true表示验证成功</returns>
        public static bool VerifyData(string data, string signature, string publicKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(signature))
                throw new ArgumentException("签名不能为空", nameof(signature));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            try
            {
                using (var rsa = CreateRsaFromPemPublicKey(publicKey))
                {
                    byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                    byte[] signatureBytes = Convert.FromBase64String(signature);
                    return rsa.VerifyData(dataBytes, signatureBytes, hashAlgorithm, padding);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 使用公钥验证字节数组的数字签名
        /// </summary>
        /// <param name="data">原始字节数组</param>
        /// <param name="signature">数字签名的字节数组</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>验证结果，true表示验证成功</returns>
        public static bool VerifyData(byte[] data, byte[] signature, string publicKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("数据不能为空", nameof(data));
            if (signature == null || signature.Length == 0)
                throw new ArgumentException("签名不能为空", nameof(signature));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            try
            {
                using (var rsa = CreateRsaFromPemPublicKey(publicKey))
                {
                    return rsa.VerifyData(data, signature, hashAlgorithm, padding);
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 哈希签名和验签

        /// <summary>
        /// 对哈希值进行签名
        /// </summary>
        /// <param name="hash">哈希值字节数组</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法名称，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>签名的Base64字符串</returns>
        public static string SignHash(byte[] hash, string privateKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (hash == null || hash.Length == 0)
                throw new ArgumentException("哈希值不能为空", nameof(hash));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                byte[] signatureBytes = rsa.SignHash(hash, hashAlgorithm, padding);
                return Convert.ToBase64String(signatureBytes);
            }
        }

        /// <summary>
        /// 验证哈希值的签名
        /// </summary>
        /// <param name="hash">哈希值字节数组</param>
        /// <param name="signature">签名的Base64字符串</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="hashAlgorithm">哈希算法名称，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>验证结果，true表示验证成功</returns>
        public static bool VerifyHash(byte[] hash, string signature, string publicKey, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (hash == null || hash.Length == 0)
                throw new ArgumentException("哈希值不能为空", nameof(hash));
            if (string.IsNullOrEmpty(signature))
                throw new ArgumentException("签名不能为空", nameof(signature));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            try
            {
                using (var rsa = CreateRsaFromPemPublicKey(publicKey))
                {
                    byte[] signatureBytes = Convert.FromBase64String(signature);
                    return rsa.VerifyHash(hash, signatureBytes, hashAlgorithm, padding);
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 分块加解密 (用于大数据)

        /// <summary>
        /// 分块加密大数据
        /// </summary>
        /// <param name="data">待加密的数据</param>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>加密后的Base64字符串</returns>
        public static string EncryptLargeData(string data, string publicKey, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(publicKey))
                throw new ArgumentException("公钥不能为空", nameof(publicKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);

            using (var rsa = CreateRsaFromPemPublicKey(publicKey))
            {
                int keySize = rsa.KeySize / 8;
                int maxBlockSize = keySize - (padding == RSAEncryptionPadding.Pkcs1 ? 11 : 42); // OAEP SHA-256 overhead is 42 bytes

                var encryptedBlocks = new System.Collections.Generic.List<byte>();
                
                for (int i = 0; i < dataBytes.Length; i += maxBlockSize)
                {
                    int blockSize = Math.Min(maxBlockSize, dataBytes.Length - i);
                    byte[] block = new byte[blockSize];
                    Array.Copy(dataBytes, i, block, 0, blockSize);
                    
                    byte[] encryptedBlock = rsa.Encrypt(block, padding);
                    encryptedBlocks.AddRange(encryptedBlock);
                }

                return Convert.ToBase64String(encryptedBlocks.ToArray());
            }
        }

        /// <summary>
        /// 分块解密大数据
        /// </summary>
        /// <param name="encryptedData">加密后的Base64字符串</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>解密后的原始数据</returns>
        public static string DecryptLargeData(string encryptedData, string privateKey, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(encryptedData))
                throw new ArgumentException("加密数据不能为空", nameof(encryptedData));
            if (string.IsNullOrEmpty(privateKey))
                throw new ArgumentException("私钥不能为空", nameof(privateKey));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;
            byte[] encryptedBytes = Convert.FromBase64String(encryptedData);

            using (var rsa = CreateRsaFromPemPrivateKey(privateKey))
            {
                int keySize = rsa.KeySize / 8;

                var decryptedBlocks = new System.Collections.Generic.List<byte>();
                
                for (int i = 0; i < encryptedBytes.Length; i += keySize)
                {
                    byte[] block = new byte[keySize];
                    Array.Copy(encryptedBytes, i, block, 0, keySize);
                    
                    byte[] decryptedBlock = rsa.Decrypt(block, padding);
                    decryptedBlocks.AddRange(decryptedBlock);
                }

                return Encoding.UTF8.GetString(decryptedBlocks.ToArray());
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 从PEM格式公钥创建RSA实例
        /// </summary>
        /// <param name="pemPublicKey">PEM格式公钥</param>
        /// <returns>RSA实例</returns>
        public static RSA CreateRsaFromPemPublicKey(string pemPublicKey)
        {
            if (string.IsNullOrEmpty(pemPublicKey))
                throw new ArgumentException("PEM公钥不能为空", nameof(pemPublicKey));

            var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(Convert.FromBase64String(pemPublicKey.Replace("-----BEGIN RSA PUBLIC KEY-----", "").Replace("-----END RSA PUBLIC KEY-----", "").Replace("\n", "").Replace("\r", "")), out _);
            return rsa;
        }

        /// <summary>
        /// 从PEM格式私钥创建RSA实例
        /// </summary>
        /// <param name="pemPrivateKey">PEM格式私钥</param>
        /// <returns>RSA实例</returns>
        public static RSA CreateRsaFromPemPrivateKey(string pemPrivateKey)
        {
            if (string.IsNullOrEmpty(pemPrivateKey))
                throw new ArgumentException("PEM私钥不能为空", nameof(pemPrivateKey));

            var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(pemPrivateKey.Replace("-----BEGIN RSA PRIVATE KEY-----", "").Replace("-----END RSA PRIVATE KEY-----", "").Replace("\n", "").Replace("\r", "")), out _);
            return rsa;
        }

        /// <summary>
        /// 获取RSA密钥大小
        /// </summary>
        /// <param name="key">RSA密钥(PEM格式)</param>
        /// <param name="isPrivateKey">是否为私钥，默认false(公钥)</param>
        /// <returns>密钥大小(位)</returns>
        public static int GetKeySize(string key, bool isPrivateKey = false)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("密钥不能为空", nameof(key));

            using (var rsa = isPrivateKey ? CreateRsaFromPemPrivateKey(key) : CreateRsaFromPemPublicKey(key))
            {
                return rsa.KeySize;
            }
        }

        #endregion

        #region 密钥验证

        /// <summary>
        /// 验证公钥和私钥是否匹配
        /// </summary>
        /// <param name="publicKey">公钥(PEM格式)</param>
        /// <param name="privateKey">私钥(PEM格式)</param>
        /// <returns>是否匹配</returns>
        public static bool ValidateKeyPair(string publicKey, string privateKey)
        {
            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
                return false;

            try
            {
                var testData = "validate_key_pair_" + Guid.NewGuid().ToString("N");
                var signature = SignData(testData, privateKey);
                return VerifyData(testData, signature, publicKey);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region XML格式兼容方法 (向后兼容)

        /// <summary>
        /// 使用公钥加密数据 (XML格式兼容)
        /// </summary>
        /// <param name="data">待加密的数据</param>
        /// <param name="publicKeyXml">公钥(XML格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>加密后的Base64字符串</returns>
        public static string EncryptWithPublicKeyXml(string data, string publicKeyXml, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(publicKeyXml))
                throw new ArgumentException("公钥不能为空", nameof(publicKeyXml));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(publicKeyXml);
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] encryptedBytes = rsa.Encrypt(dataBytes, padding);
                return Convert.ToBase64String(encryptedBytes);
            }
        }

        /// <summary>
        /// 使用私钥解密数据 (XML格式兼容)
        /// </summary>
        /// <param name="encryptedData">加密后的Base64字符串</param>
        /// <param name="privateKeyXml">私钥(XML格式)</param>
        /// <param name="padding">填充模式，默认OAEP</param>
        /// <returns>解密后的原始数据</returns>
        public static string DecryptWithPrivateKeyXml(string encryptedData, string privateKeyXml, RSAEncryptionPadding padding = null)
        {
            if (string.IsNullOrEmpty(encryptedData))
                throw new ArgumentException("加密数据不能为空", nameof(encryptedData));
            if (string.IsNullOrEmpty(privateKeyXml))
                throw new ArgumentException("私钥不能为空", nameof(privateKeyXml));

            padding = padding ?? RSAEncryptionPadding.OaepSHA256;

            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(privateKeyXml);
                byte[] encryptedBytes = Convert.FromBase64String(encryptedData);
                byte[] decryptedBytes = rsa.Decrypt(encryptedBytes, padding);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }

        /// <summary>
        /// 使用私钥对数据进行数字签名 (XML格式兼容)
        /// </summary>
        /// <param name="data">待签名的数据</param>
        /// <param name="privateKeyXml">私钥(XML格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>数字签名的Base64字符串</returns>
        public static string SignDataXml(string data, string privateKeyXml, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(privateKeyXml))
                throw new ArgumentException("私钥不能为空", nameof(privateKeyXml));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(privateKeyXml);
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] signatureBytes = rsa.SignData(dataBytes, hashAlgorithm, padding);
                return Convert.ToBase64String(signatureBytes);
            }
        }

        /// <summary>
        /// 使用公钥验证数字签名 (XML格式兼容)
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="signature">数字签名的Base64字符串</param>
        /// <param name="publicKeyXml">公钥(XML格式)</param>
        /// <param name="hashAlgorithm">哈希算法，默认SHA256</param>
        /// <param name="padding">签名填充模式，默认PSS</param>
        /// <returns>验证结果，true表示验证成功</returns>
        public static bool VerifyDataXml(string data, string signature, string publicKeyXml, HashAlgorithmName hashAlgorithm = default, RSASignaturePadding padding = null)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("数据不能为空", nameof(data));
            if (string.IsNullOrEmpty(signature))
                throw new ArgumentException("签名不能为空", nameof(signature));
            if (string.IsNullOrEmpty(publicKeyXml))
                throw new ArgumentException("公钥不能为空", nameof(publicKeyXml));

            hashAlgorithm = hashAlgorithm == default ? HashAlgorithmName.SHA256 : hashAlgorithm;
            padding = padding ?? RSASignaturePadding.Pss;

            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                    byte[] signatureBytes = Convert.FromBase64String(signature);
                    return rsa.VerifyData(dataBytes, signatureBytes, hashAlgorithm, padding);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// PEM格式转XML格式 (公钥)
        /// </summary>
        /// <param name="pemPublicKey">PEM格式公钥</param>
        /// <returns>XML格式公钥</returns>
        public static string ConvertPemToXmlPublicKey(string pemPublicKey)
        {
            using (var rsa = CreateRsaFromPemPublicKey(pemPublicKey))
            {
                return rsa.ToXmlString(false);
            }
        }

        /// <summary>
        /// PEM格式转XML格式 (私钥)
        /// </summary>
        /// <param name="pemPrivateKey">PEM格式私钥</param>
        /// <returns>XML格式私钥</returns>
        public static string ConvertPemToXmlPrivateKey(string pemPrivateKey)
        {
            using (var rsa = CreateRsaFromPemPrivateKey(pemPrivateKey))
            {
                return rsa.ToXmlString(true);
            }
        }

        /// <summary>
        /// XML格式转PEM格式 (公钥)
        /// </summary>
        /// <param name="xmlPublicKey">XML格式公钥</param>
        /// <returns>PEM格式公钥</returns>
        public static string ConvertXmlToPemPublicKey(string xmlPublicKey)
        {
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(xmlPublicKey);
                return ExportPublicKeyToPem(rsa);
            }
        }

        /// <summary>
        /// XML格式转PEM格式 (私钥)
        /// </summary>
        /// <param name="xmlPrivateKey">XML格式私钥</param>
        /// <returns>PEM格式私钥</returns>
        public static string ConvertXmlToPemPrivateKey(string xmlPrivateKey)
        {
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(xmlPrivateKey);
                return ExportPrivateKeyToPem(rsa);
            }
        }

        #endregion
    }
} 