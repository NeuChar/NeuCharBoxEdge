#include "NcCrypto.h"

// 静态变量用于单例模式的RSA服务
static mbedtls_pk_context g_pk;
static mbedtls_entropy_context g_entropy;
static mbedtls_ctr_drbg_context g_ctr_drbg;
static bool g_rsa_service_initialized = false;
static SemaphoreHandle_t g_rsa_mutex = NULL;

String sanitizeAndFixPEM(String input) {
    Serial.println("[PEMFixer] Analyzing Key...");
    Serial.flush();
    
    // 1. 预处理：保留有效字符
    String clean;
    clean.reserve(input.length()); 
    for (size_t i = 0; i < input.length(); i++) {
        char c = input[i];
        if (isalnum(c) || c == '+' || c == '/' || c == '=' || c == '-' || c == '\n' || c == '\r' || c == ' ') {
            clean += c;
        }
    }

    // 2. 定义支持的标签
    const char* tags[][2] = {
        {"-----BEGIN RSA PRIVATE KEY-----", "-----END RSA PRIVATE KEY-----"}, // PKCS#1
        {"-----BEGIN PRIVATE KEY-----", "-----END PRIVATE KEY-----"},         // PKCS#8
        {"-----BEGIN RSA PUBLIC KEY-----", "-----END RSA PUBLIC KEY-----"},
        {"-----BEGIN PUBLIC KEY-----", "-----END PUBLIC KEY-----"}
    };

    int start = -1, end = -1;
    int tagIdx = -1;
    for (int i = 0; i < 4; i++) {
        start = clean.indexOf(tags[i][0]);
        end = clean.indexOf(tags[i][1]);
        if (start != -1 && end != -1) {
            tagIdx = i;
            break;
        }
    }

    if (tagIdx == -1) {
        Serial.println("[PEMFixer] CRITICAL: PEM Markers not found! Check if key is truncated.");
        Serial.flush();
        return input;
    }

    String header = tags[tagIdx][0];
    String footer = tags[tagIdx][1];

    // 3. 提取 Body 并在内部彻底清理
    String bodyOnly;
    bodyOnly.reserve(clean.length()); 
    String rawBody = clean.substring(start + header.length(), end);
    for (size_t i = 0; i < rawBody.length(); i++) {
        char c = rawBody[i];
        if (isalnum(c) || c == '+' || c == '/' || c == '=') bodyOnly += c;
    }

    // 4. 输出信息
    Serial.printf("[PEMFixer] Format Detected: %s\n", (tagIdx < 2 ? "Private Key" : "Public Key"));
    Serial.printf("[PEMFixer] Body Length: %d chars\n", bodyOnly.length());

    // 5. 重新拼装为 mbedtls 最兼容的标准格式
    String output;
    output.reserve(bodyOnly.length() + 128);
    output = header + "\n";
    for (size_t i = 0; i < bodyOnly.length(); i += 64) {
        output += bodyOnly.substring(i, min((int)(i + 64), (int)bodyOnly.length())) + "\n";
    }
    output += footer + "\n";
    
    return output;
}

void initRsaService(const char* pemKey) {
    if (!g_rsa_service_initialized) {
        // 初始化互斥锁
        g_rsa_mutex = xSemaphoreCreateMutex();
        if (!g_rsa_mutex) {
            Serial.println("[rsaSign] ERROR: Failed to create mutex!");
            return;
        }
        
        // 初始化密钥上下文
        mbedtls_pk_init(&g_pk);
        
        String keyStr = sanitizeAndFixPEM(String(pemKey));
        int ret = mbedtls_pk_parse_key(&g_pk, (const unsigned char*)keyStr.c_str(), keyStr.length() + 1, NULL, 0);
        if (ret != 0) {
            char err_buf[128];
            mbedtls_strerror(ret, err_buf, sizeof(err_buf));
            Serial.printf("[rsaSign] Global Key Init FAIL: -0x%04X (%s)\n", -ret, err_buf);
            vSemaphoreDelete(g_rsa_mutex);
            return;
        }
        
        // 设置填充模式
        mbedtls_rsa_set_padding(mbedtls_pk_rsa(g_pk), MBEDTLS_RSA_PKCS_V21, MBEDTLS_MD_SHA256);
        
        // 初始化熵和随机数生成器
        mbedtls_entropy_init(&g_entropy);
        mbedtls_ctr_drbg_init(&g_ctr_drbg);
        ret = mbedtls_ctr_drbg_seed(&g_ctr_drbg, mbedtls_entropy_func, &g_entropy, 
                                   (const unsigned char *)"esp32_rsa", 9);
        if (ret != 0) {
            char err_buf[128];
            mbedtls_strerror(ret, err_buf, sizeof(err_buf));
            Serial.printf("[rsaSign] CTR_DRBG Init FAIL: -0x%04X (%s)\n", -ret, err_buf);
            vSemaphoreDelete(g_rsa_mutex);
            return;
        }
        
        g_rsa_service_initialized = true;
        Serial.println("[rsaSign] Global RSA Service Initialized Successfully");
    }
}

String rsaSign(String message, const char* pemKey) {
    // 确保服务已初始化
    initRsaService(pemKey);
    
    // 尝试获取互斥锁，防止并发访问
    if (!g_rsa_mutex || !xSemaphoreTake(g_rsa_mutex, pdMS_TO_TICKS(5000))) {
        Serial.println("[rsaSign] ERROR: Could not acquire mutex!");
        return "";
    }
    
    // 检查服务是否已初始化
    if (!g_rsa_service_initialized) {
        Serial.println("[rsaSign] ERROR: RSA service not initialized!");
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    
    // 分配临时缓冲区
    unsigned char *sig_buf = (unsigned char*)malloc(MBEDTLS_MPI_MAX_SIZE);
    unsigned char *b64_buf = (unsigned char*)malloc(1024);
    unsigned char hash[32];
    
    if (!sig_buf || !b64_buf) {
        Serial.println("[rsaSign] CRITICAL: Heap Malloc Failed!");
        if (sig_buf) free(sig_buf);
        if (b64_buf) free(b64_buf);
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }

    // 计算哈希
    mbedtls_md_context_t md_ctx;
    mbedtls_md_init(&md_ctx);
    mbedtls_md_setup(&md_ctx, mbedtls_md_info_from_type(MBEDTLS_MD_SHA256), 0);
    mbedtls_md_starts(&md_ctx);
    mbedtls_md_update(&md_ctx, (const unsigned char*)message.c_str(), message.length());
    mbedtls_md_finish(&md_ctx, hash);
    mbedtls_md_free(&md_ctx);

    // 执行签名
    size_t sig_len = 0;
    int ret = mbedtls_pk_sign(&g_pk, MBEDTLS_MD_SHA256, hash, 32, sig_buf, &sig_len, 
                             mbedtls_ctr_drbg_random, &g_ctr_drbg);
    if (ret != 0) {
        char err_buf[128];
        mbedtls_strerror(ret, err_buf, sizeof(err_buf));
        Serial.printf("[rsaSign] FAIL: -0x%04X (%s)\n", -ret, err_buf);
        free(sig_buf);
        free(b64_buf);
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    
    // Base64编码
    size_t b64_len = 0;
    mbedtls_base64_encode(b64_buf, 1024, &b64_len, sig_buf, sig_len);
    b64_buf[b64_len] = '\0';
    
    // 保存结果
    String finalResult = String((char*)b64_buf);
    
    // 清理资源
    free(sig_buf);
    free(b64_buf);
    
    // 释放互斥锁
    xSemaphoreGive(g_rsa_mutex);
    
    return finalResult;
}

String rsaDecrypt(String base64Input, const char* pemKey) {
    initRsaService(pemKey);
    if (!g_rsa_mutex || !xSemaphoreTake(g_rsa_mutex, pdMS_TO_TICKS(5000))) {
        Serial.println("[rsaDecrypt] ERROR: Could not acquire mutex!");
        return "";
    }
    if (!g_rsa_service_initialized) {
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }

    // 静态缓冲区，不占栈，避免 loopTask stack overflow
    static unsigned char enc_buf[512];
    static unsigned char out_buf[512];
    size_t enc_len = 0;
    size_t out_len = 0;

    int ret = mbedtls_base64_decode(enc_buf, sizeof(enc_buf), &enc_len, (const unsigned char*)base64Input.c_str(), base64Input.length());
    if (ret != 0) {
        Serial.printf("[rsaDecrypt] Base64 decode FAIL: -0x%04X\n", -ret);
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }

    ret = mbedtls_pk_decrypt(&g_pk, enc_buf, enc_len, out_buf, &out_len, sizeof(out_buf), mbedtls_ctr_drbg_random, &g_ctr_drbg);
    xSemaphoreGive(g_rsa_mutex);

    if (ret != 0) {
        Serial.printf("[rsaDecrypt] Decrypt FAIL: -0x%04X\n", -ret);
        return "";
    }
    out_buf[out_len] = '\0';
    return String((char*)out_buf);
}

String rsaEncryptWithPrivateKey(String message, const char* pemKey) {
    initRsaService(pemKey);
    if (!g_rsa_mutex || !xSemaphoreTake(g_rsa_mutex, pdMS_TO_TICKS(5000))) {
        Serial.println("[rsaEncryptPriv] ERROR: Could not acquire mutex!");
        return "";
    }
    if (!g_rsa_service_initialized) {
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    mbedtls_rsa_context* rsa = mbedtls_pk_rsa(g_pk);
    size_t keyLen = mbedtls_rsa_get_len(rsa);
    if (keyLen < 32 || keyLen > 512) {
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    const size_t msgLen = message.length();
    if (msgLen + 11 > keyLen) {
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    static unsigned char padded[512];
    size_t padLen = keyLen - 3 - msgLen;
    padded[0] = 0x00;
    padded[1] = 0x02;  // PKCS#1 v1.5 Block Type 2 (加密用)，服务端用公钥解密校验
    int ret = mbedtls_ctr_drbg_random(&g_ctr_drbg, padded + 2, padLen);
    if (ret != 0) {
        xSemaphoreGive(g_rsa_mutex);
        return "";
    }
    for (size_t i = 2; i < 2 + padLen; i++)
        if (padded[i] == 0) padded[i] = 1;
    padded[2 + padLen] = 0x00;
    memcpy(padded + 3 + padLen, message.c_str(), msgLen);

    static unsigned char outBuf[512];
    ret = mbedtls_rsa_private(rsa, mbedtls_ctr_drbg_random, &g_ctr_drbg, padded, outBuf);
    xSemaphoreGive(g_rsa_mutex);
    if (ret != 0) {
        Serial.printf("[rsaEncryptPriv] FAIL: -0x%04X\n", -ret);
        return "";
    }
    size_t b64Len = 0;
    static unsigned char b64Buf[704];
    mbedtls_base64_encode(b64Buf, sizeof(b64Buf), &b64Len, outBuf, keyLen);
    b64Buf[b64Len] = '\0';
    return String((char*)b64Buf);
}
