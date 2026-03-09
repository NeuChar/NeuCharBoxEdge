#ifndef NC_CRYPTO_H
#define NC_CRYPTO_H

#include <Arduino.h>
#include "mbedtls/pk.h"
#include "mbedtls/base64.h"
#include "mbedtls/entropy.h"
#include "mbedtls/ctr_drbg.h"
#include "mbedtls/md.h"
#include "mbedtls/rsa.h"
#include "mbedtls/error.h"

// 极致可观测性 & 自动格式修复逻辑
String sanitizeAndFixPEM(String input);

// 初始化全局RSA服务
void initRsaService(const char* pemKey);

// 同步的RSA签名函数
String rsaSign(String message, const char* pemKey);

// RSA 解密
String rsaDecrypt(String base64Input, const char* pemKey);

// 使用私钥“加密”短字符串（PKCS#1 v1.5），供服务端用公钥解密；用于 GetNCBNetInfo 等 API 的 sign 字段
String rsaEncryptWithPrivateKey(String message, const char* pemKey);

#endif
