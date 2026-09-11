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
    ///
    /// A WRITE NEVER TOUCHES THE LIVE FILE UNTIL THE NEW ONE IS WHOLE. The blob goes to a temporary
    /// file first, is flushed to the device, and only then do the files change places, the previous
    /// save becoming the backup. An app killed mid-write therefore leaves either the old save or the
    /// new one, never half of one — and a half file used to fail the MAC, read as "no save", and be
    /// overwritten by a fresh game on the first autosave. A load that cannot read the main file
    /// falls back to the temporary file and then the backup, and keeps a copy of a file nothing
    /// could read rather than letting the next write destroy it.
    /// </summary>
    public sealed class SaveService
    {
        private const string Passphrase = "OreEmpire.v1.salt.9c3f"; // client-side obfuscation key
        private const int IvSize = 16;
        private const int MacSize = 32;

        public const string TempSuffix = ".tmp";
        public const string BackupSuffix = ".bak";
        public const string UnreadableSuffix = ".unreadable";

        /// <summary>Which file the last <see cref="TryLoad"/> read.</summary>
        public enum LoadSource { None, Main, Temporary, Backup }

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

        /// <summary>Which file the last <see cref="TryLoad"/> read. Anything but
        /// <see cref="LoadSource.Main"/> after a successful load means the main file was missing or
        /// unreadable and the progress came from a fallback.</summary>
        public LoadSource LastLoadSource { get; private set; }

        public void Save(SaveData data)
        {
            if (Suspended) return;
            data.savedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteReplacing(Encrypt(data));
        }

        /// <summary>
        /// Temporary file, flushed, then two renames. Between the renames the main file is briefly
        /// absent, which is why <see cref="TryLoad"/> reads the temporary file before the backup: by
        /// then it is complete, and it is newer.
        /// </summary>
        private void WriteReplacing(byte[] blob)
        {
            string temp = _path + TempSuffix;
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(blob, 0, blob.Length);
                stream.Flush(true);
            }

            if (File.Exists(_path))
            {
                string backup = _path + BackupSuffix;
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_path, backup);
            }
            File.Move(temp, _path);
        }

        public bool TryLoad(out SaveData data)
        {
            LastLoadSource = LoadSource.None;
            if (TryRead(_path, out data)) { LastLoadSource = LoadSource.Main; return true; }
            if (TryRead(_path + TempSuffix, out data)) { LastLoadSource = LoadSource.Temporary; return true; }
            if (TryRead(_path + BackupSuffix, out data)) { LastLoadSource = LoadSource.Backup; return true; }

            // Nothing could be read. The game will start over and its first write would bury this
            // file under the backup and then delete it, so a copy is kept for support to look at.
            KeepUnreadable();
            return false;
        }

        private bool TryRead(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path)) return false;
            try
            {
                data = Decrypt(File.ReadAllBytes(path), out bool tampered);
                if (tampered) data = null;
                return data != null;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        private void KeepUnreadable()
        {
            if (!File.Exists(_path)) return;
            try
            {
                File.Copy(_path, _path + UnreadableSuffix, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Best effort: failing to keep a copy must not stop the game from starting.
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
