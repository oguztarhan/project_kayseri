using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Encrypted, integrity-checked save/load. Serializes <see cref="SaveData"/> to JSON,
    /// encrypts with AES-256-CBC (random IV) and authenticates with HMAC-SHA256, so a
    /// hand-edited save file is rejected on load.
    /// NOTE: the key lives in the client binary, so this is obfuscation + tamper detection,
    /// not server-grade anti-cheat. Trusted-time validation for offline earnings lands in M3.
    /// </summary>
    public sealed class SaveService
    {
        private const string Passphrase = "OreEmpire.v1.salt.9c3f"; // client-side obfuscation key
        private const int IvSize = 16;
        private const int MacSize = 32;

        private readonly byte[] _aesKey;
        private readonly byte[] _macKey;
        private readonly string _path;

        public SaveService(string fileName = "save.dat")
        {
            using (var sha = SHA256.Create())
            {
                _aesKey = sha.ComputeHash(Encoding.UTF8.GetBytes(Passphrase));
                _macKey = sha.ComputeHash(Encoding.UTF8.GetBytes(Passphrase + "|mac"));
            }
            _path = Path.Combine(Application.persistentDataPath, fileName);
        }

        public string SavePath => _path;

        /// <summary>While true, <see cref="Save"/> is a no-op. Test mode sets this (sticky for the whole
        /// session) so test purchases never reach disk — the next launch loads the real save untouched.</summary>
        public bool Suspended;

        public void Save(SaveData data)
        {
            if (Suspended) return;
            data.savedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.WriteAllBytes(_path, Encrypt(data));
        }

        public bool TryLoad(out SaveData data)
        {
            data = null;
            if (!File.Exists(_path)) return false;
            try
            {
                data = Decrypt(File.ReadAllBytes(_path), out bool tampered);
                return !tampered && data != null;
            }
            catch
            {
                return false;
            }
        }

        // ---- pure, testable core (no disk) ----

        public byte[] Encrypt(SaveData data)
        {
            byte[] plain = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));

            using (var aes = Aes.Create())
            {
                aes.Key = _aesKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                byte[] iv = aes.IV;

                byte[] cipher;
                using (var enc = aes.CreateEncryptor())
                {
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                }

                byte[] mac = ComputeMac(iv, cipher);
                byte[] blob = new byte[IvSize + MacSize + cipher.Length];
                Buffer.BlockCopy(iv, 0, blob, 0, IvSize);
                Buffer.BlockCopy(mac, 0, blob, IvSize, MacSize);
                Buffer.BlockCopy(cipher, 0, blob, IvSize + MacSize, cipher.Length);
                return blob;
            }
        }

        public SaveData Decrypt(byte[] blob, out bool tampered)
        {
            tampered = false;
            if (blob == null || blob.Length < IvSize + MacSize)
            {
                tampered = true;
                return null;
            }

            byte[] iv = new byte[IvSize];
            byte[] mac = new byte[MacSize];
            int cipherLen = blob.Length - IvSize - MacSize;
            byte[] cipher = new byte[cipherLen];
            Buffer.BlockCopy(blob, 0, iv, 0, IvSize);
            Buffer.BlockCopy(blob, IvSize, mac, 0, MacSize);
            Buffer.BlockCopy(blob, IvSize + MacSize, cipher, 0, cipherLen);

            if (!ConstantTimeEquals(mac, ComputeMac(iv, cipher)))
            {
                tampered = true;
                return null;
            }

            using (var aes = Aes.Create())
            {
                aes.Key = _aesKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                {
                    byte[] plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                    return JsonUtility.FromJson<SaveData>(Encoding.UTF8.GetString(plain));
                }
            }
        }

        private byte[] ComputeMac(byte[] iv, byte[] cipher)
        {
            using (var hmac = new HMACSHA256(_macKey))
            {
                byte[] buf = new byte[iv.Length + cipher.Length];
                Buffer.BlockCopy(iv, 0, buf, 0, iv.Length);
                Buffer.BlockCopy(cipher, 0, buf, iv.Length, cipher.Length);
                return hmac.ComputeHash(buf);
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
