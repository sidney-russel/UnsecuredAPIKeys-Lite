using System.Security.Cryptography;
using System.Text;

namespace UnsecuredAPIKeys.CLI.Services;

/// <summary>
/// Encrypts and decrypts sensitive data like API tokens using AES.
/// </summary>
public static class TokenEncryption
{
    private const int KeySize = 256;
    private const int BlockSize = 128;
    private const int Iterations = 100_000;
    private static readonly byte[] Salt = "UnsecuredAPIKeys.Lite.v1.Salt"u8.ToArray();

    /// <summary>
    /// Encrypts a plaintext string using AES.
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var key = Rfc2898DeriveBytes.Pbkdf2(GetMachineKey(), Salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);
        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        aes.BlockSize = BlockSize;
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts an encrypted string back to plaintext.
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            var fullCipher = Convert.FromBase64String(encryptedText);

            var key = Rfc2898DeriveBytes.Pbkdf2(GetMachineKey(), Salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Key = key;

            var iv = new byte[BlockSize / 8];
            var cipher = new byte[fullCipher.Length - iv.Length];
            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return encryptedText;
        }
    }

    /// <summary>
    /// Checks if a token is already encrypted.
    /// </summary>
    public static bool IsEncrypted(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (token.StartsWith("ghp_", StringComparison.Ordinal) ||
            token.StartsWith("github_pat_", StringComparison.Ordinal))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(token);
            return bytes.Length > 20;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] GetMachineKey()
    {
        var machineId = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineId}:{userName}:UnsecuredAPIKeys.Lite";
        return SHA256.HashData(Encoding.UTF8.GetBytes(combined));
    }
}
