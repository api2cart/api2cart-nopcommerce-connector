using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api2Cart.Connector.Models;

namespace Api2Cart.Connector.Helpers
{
  public static class CryptoHelper
  {
    private const string ConfigFileName = "connector.config.json";
    private const string TokenPlaceholder = "PASTE_SECURITY_TOKEN_HERE";

    private static RSAParameters _rsaParams;
    private static string _keyId = string.Empty;
    private static readonly object _initLock = new();
    private static volatile bool _initialized;

    public static string KeyId
    {
      get
      {
        EnsureInitialized();
        return _keyId;
      }
    }

    private static void EnsureInitialized()
    {
      if (_initialized) {
        return;
      }

      lock (_initLock) {
        if (_initialized) {
          return;
        }

        var (pem, keyId) = LoadFromConfig();

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        _rsaParams = rsa.ExportParameters(false);
        _keyId = keyId;
        _initialized = true;
      }
    }

    /// <summary>
    /// Load the per-build RSA public key + key id from the plugin config file
    /// shipped alongside the assembly (`connector.config.json`).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Config file missing or required fields (ConnectorPublicKey / ConnectorKeyId) absent.
    /// Each build embeds its own pair via Api2cart_PluginBuilder; there is no legacy fallback.
    /// </exception>
    private static (string Pem, string KeyId) LoadFromConfig()
    {
      var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

      if (string.IsNullOrEmpty(assemblyDir)) {
        throw new InvalidOperationException(
          "Cannot locate plugin assembly directory; encryption config unavailable."
        );
      }

      var configPath = Path.Combine(assemblyDir, ConfigFileName);

      if (!File.Exists(configPath)) {
        throw new InvalidOperationException(
          $"Encryption config missing at '{configPath}'. Rebuild plugin via the Plugin Builder."
        );
      }

      var json = File.ReadAllText(configPath);
      var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? throw new InvalidOperationException($"Encryption config '{configPath}' is not valid JSON.");

      if (
        !config.TryGetValue("ConnectorPublicKey", out var pem)
        || string.IsNullOrEmpty(pem)
        || pem == TokenPlaceholder
      ) {
        throw new InvalidOperationException(
          $"ConnectorPublicKey missing or placeholder in '{configPath}'. Rebuild plugin via the Plugin Builder."
        );
      }

      if (
        !config.TryGetValue("ConnectorKeyId", out var keyId)
        || string.IsNullOrEmpty(keyId)
      ) {
        throw new InvalidOperationException(
          $"ConnectorKeyId missing in '{configPath}'. Rebuild plugin via the Plugin Builder."
        );
      }

      return (pem, keyId);
    }

    /// <summary>
    /// Decrypt data encrypted by PHP with openssl_private_encrypt (PKCS1 type 1) + AES-256-CBC.
    /// PHP encrypts the AES key with the private key using openssl_private_encrypt,
    /// so we must use raw RSA public operation (BigInteger.ModPow) to decrypt it.
    /// </summary>
    public static string Decrypt(string encryptedJson)
    {
      EnsureInitialized();
      var payload = JsonSerializer.Deserialize<EncryptedPayload>(encryptedJson);

      if (
        payload == null
        || string.IsNullOrEmpty(payload.EncryptedKey)
        || string.IsNullOrEmpty(payload.Iv)
        || string.IsNullOrEmpty(payload.Data)
      ) {
        throw new CryptographicException("Invalid encrypted payload structure.");
      }

      byte[] encryptedKeyBytes, ivBytes, dataBytes;

      try {
        encryptedKeyBytes = Convert.FromBase64String(payload.EncryptedKey);
        ivBytes = Convert.FromBase64String(payload.Iv);
        dataBytes = Convert.FromBase64String(payload.Data);
      } catch (FormatException ex) {
        throw new CryptographicException("Invalid Base64 encoding in encrypted payload.", ex);
      }

      var aesKey = RsaPublicDecrypt(encryptedKeyBytes);

      if (aesKey.Length != 32) {
        throw new CryptographicException("Invalid AES key length after RSA decryption. Expected 32 bytes for AES-256.");
      }

      var decryptedCompressed = AesDecrypt(dataBytes, aesKey, ivBytes);
      var decompressed = ZlibDecompress(decryptedCompressed);

      return Encoding.UTF8.GetString(decompressed);
    }

    /// <summary>
    /// Encrypt data so PHP can decrypt with openssl_private_decrypt(OAEP).
    /// Uses standard RSA OAEP SHA1 encryption with the public key.
    /// </summary>
    public static string Encrypt(string data)
    {
      EnsureInitialized();
      var compressed = ZlibCompress(Encoding.UTF8.GetBytes(data));

      var aesKey = RandomNumberGenerator.GetBytes(32);
      var iv = RandomNumberGenerator.GetBytes(16);

      var encryptedData = AesEncrypt(compressed, aesKey, iv);

      using var rsa = RSA.Create();
      rsa.ImportParameters(_rsaParams);
      var encryptedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA1);

      var payload = new EncryptedPayload
      {
        EncryptedKey = Convert.ToBase64String(encryptedKey),
        Iv = Convert.ToBase64String(iv),
        Data = Convert.ToBase64String(encryptedData),
      };

      return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Raw RSA public key operation to decrypt data encrypted with openssl_private_encrypt.
    /// openssl_private_encrypt uses PKCS1 v1.5 type 1 padding (signature padding):
    ///   0x00 0x01 [0xFF padding] 0x00 [data]
    /// Standard .NET RSA doesn't support "public decrypt", so we do it manually with BigInteger.
    /// </summary>
    private static byte[] RsaPublicDecrypt(byte[] cipherBytes)
    {
      var modulus = _rsaParams.Modulus!;
      var exponent = _rsaParams.Exponent!;

      var cipherInt = new BigInteger(cipherBytes, isUnsigned: true, isBigEndian: true);
      var modulusInt = new BigInteger(modulus, isUnsigned: true, isBigEndian: true);
      var exponentInt = new BigInteger(exponent, isUnsigned: true, isBigEndian: true);

      var plainInt = BigInteger.ModPow(cipherInt, exponentInt, modulusInt);

      var plainBytes = plainInt.ToByteArray(isUnsigned: true, isBigEndian: true);

      var keySize = modulus.Length;
      var padded = new byte[keySize];

      if (plainBytes.Length < keySize) {
        Array.Copy(plainBytes, 0, padded, keySize - plainBytes.Length, plainBytes.Length);
      } else {
        Array.Copy(plainBytes, plainBytes.Length - keySize, padded, 0, keySize);
      }

      return StripPkcs1Type1Padding(padded);
    }

    /// <summary>
    /// Strip PKCS#1 v1.5 type 1 padding: 0x00 0x01 [0xFF bytes] 0x00 [data]
    /// </summary>
    private static byte[] StripPkcs1Type1Padding(byte[] data)
    {
      if (data.Length < 11 || data[0] != 0x00 || data[1] != 0x01) {
        throw new CryptographicException("Invalid PKCS#1 v1.5 type 1 padding.");
      }

      var i = 2;

      while (i < data.Length && data[i] == 0xFF) {
        i++;
      }

      if ((i - 2) < 8) {
        throw new CryptographicException("Invalid PKCS#1 v1.5 type 1 padding: insufficient padding length.");
      }

      if (i >= data.Length || data[i] != 0x00) {
        throw new CryptographicException("Invalid PKCS#1 v1.5 type 1 padding: missing separator.");
      }

      i++;

      var result = new byte[data.Length - i];
      Array.Copy(data, i, result, 0, result.Length);

      return result;
    }

    private static byte[] AesEncrypt(byte[] data, byte[] key, byte[] iv)
    {
      using var aes = Aes.Create();
      aes.Key = key;
      aes.IV = iv;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.PKCS7;

      using var encryptor = aes.CreateEncryptor();

      return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] AesDecrypt(byte[] data, byte[] key, byte[] iv)
    {
      using var aes = Aes.Create();
      aes.Key = key;
      aes.IV = iv;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.PKCS7;

      using var decryptor = aes.CreateDecryptor();

      return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>
    /// Compress using zlib (deflate with zlib header) to match PHP gzcompress().
    /// </summary>
    private static byte[] ZlibCompress(byte[] data)
    {
      using var output = new MemoryStream();
      using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
      {
        zlib.Write(data, 0, data.Length);
      }

      return output.ToArray();
    }

    /// <summary>
    /// Decompress zlib data to match PHP gzuncompress().
    /// </summary>
    private static byte[] ZlibDecompress(byte[] data)
    {
      using var input = new MemoryStream(data);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      zlib.CopyTo(output);

      return output.ToArray();
    }
  }
}
