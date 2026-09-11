using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The save file against a force-close. Each test builds the exact set of files an app killed at
    /// one point of <see cref="SaveService.Save"/> would leave behind, then asks what the next launch
    /// reads. The one outcome none of them may produce is "no save" while a whole one is on disk.
    /// </summary>
    public class SaveFileRecoveryTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Application.temporaryCachePath,
                                 "save-recovery-" + Guid.NewGuid().ToString("N") + ".dat");
        }

        [TearDown]
        public void TearDown()
        {
            Delete(_path);
            Delete(_path + SaveService.TempSuffix);
            Delete(_path + SaveService.BackupSuffix);
            Delete(_path + SaveService.UnreadableSuffix);
        }

        private static void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static SaveData WithGems(long gems)
        {
            var data = new SaveData();
            data.wallet.gems = gems;
            return data;
        }

        private static void Truncate(string path)
        {
            byte[] whole = File.ReadAllBytes(path);
            var half = new byte[whole.Length / 2];
            Buffer.BlockCopy(whole, 0, half, 0, half.Length);
            File.WriteAllBytes(path, half);
        }

        [Test]
        public void AFinishedWriteLeavesNoTemporaryFileAndReadsFromTheMainFile()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(7L));

            Assert.That(File.Exists(_path), Is.True);
            Assert.That(File.Exists(_path + SaveService.TempSuffix), Is.False);
            Assert.That(save.TryLoad(out SaveData loaded), Is.True);
            Assert.That(save.LastLoadSource, Is.EqualTo(SaveService.LoadSource.Main));
            Assert.That(loaded.wallet.gems, Is.EqualTo(7L));
        }

        [Test]
        public void EachWriteKeepsThePreviousSaveAsTheBackup()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(1L));
            save.Save(WithGems(2L));
            save.Save(WithGems(3L));

            var backup = new SaveService(_path + SaveService.BackupSuffix);
            Assert.That(backup.TryLoad(out SaveData previous), Is.True);
            Assert.That(previous.wallet.gems, Is.EqualTo(2L), "one generation back, not the first");
            Assert.That(save.TryLoad(out SaveData current), Is.True);
            Assert.That(current.wallet.gems, Is.EqualTo(3L));
        }

        [Test]
        public void KilledWhileWritingTheTemporaryFileTheLastWholeSaveIsRead()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(10L));
            File.WriteAllBytes(_path + SaveService.TempSuffix, new byte[] { 1, 2, 3, 4, 5 });

            Assert.That(new SaveService(_path).TryLoad(out SaveData loaded), Is.True);
            Assert.That(loaded.wallet.gems, Is.EqualTo(10L));
        }

        [Test]
        public void KilledBetweenTheTwoRenamesTheNewSaveIsRead()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(10L));
            save.Save(WithGems(11L));
            // The moment after the old main became the backup and before the new file took its name.
            File.Move(_path, _path + SaveService.TempSuffix);

            var relaunch = new SaveService(_path);
            Assert.That(relaunch.TryLoad(out SaveData loaded), Is.True);
            Assert.That(relaunch.LastLoadSource, Is.EqualTo(SaveService.LoadSource.Temporary));
            Assert.That(loaded.wallet.gems, Is.EqualTo(11L), "the newer of the two whole files");
        }

        [Test]
        public void ATornMainFileFallsBackToTheBackupInsteadOfStartingOver()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(20L));
            save.Save(WithGems(21L));
            Truncate(_path);

            var relaunch = new SaveService(_path);
            Assert.That(relaunch.TryLoad(out SaveData loaded), Is.True);
            Assert.That(relaunch.LastLoadSource, Is.EqualTo(SaveService.LoadSource.Backup));
            Assert.That(loaded.wallet.gems, Is.EqualTo(20L));
        }

        [Test]
        public void TheNextWriteAfterARecoveryMakesTheMainFileWholeAgain()
        {
            var save = new SaveService(_path);
            save.Save(WithGems(30L));
            save.Save(WithGems(31L));
            Truncate(_path);

            var relaunch = new SaveService(_path);
            Assert.That(relaunch.TryLoad(out SaveData loaded), Is.True);
            relaunch.Save(loaded);

            Assert.That(relaunch.TryLoad(out SaveData again), Is.True);
            Assert.That(relaunch.LastLoadSource, Is.EqualTo(SaveService.LoadSource.Main));
            Assert.That(again.wallet.gems, Is.EqualTo(30L));
        }

        [Test]
        public void AFileNothingCanReadIsKeptAsideRatherThanOverwritten()
        {
            byte[] garbage = { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
                               9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
                               9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
            File.WriteAllBytes(_path, garbage);

            var save = new SaveService(_path);
            Assert.That(save.TryLoad(out SaveData loaded), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(save.LastLoadSource, Is.EqualTo(SaveService.LoadSource.None));
            Assert.That(File.ReadAllBytes(_path + SaveService.UnreadableSuffix), Is.EqualTo(garbage));

            // The fresh game's first write must not be what destroys the only copy.
            save.Save(new SaveData());
            Assert.That(File.ReadAllBytes(_path + SaveService.UnreadableSuffix), Is.EqualTo(garbage));
        }

        [Test]
        public void NoFileAtAllIsAFreshGameAndKeepsNothingAside()
        {
            var save = new SaveService(_path);
            Assert.That(save.TryLoad(out _), Is.False);
            Assert.That(File.Exists(_path + SaveService.UnreadableSuffix), Is.False);
        }

        [Test]
        public void ASuspendedServiceTouchesNoFile()
        {
            var save = new SaveService(_path) { Suspended = true };
            save.Save(WithGems(5L));

            Assert.That(File.Exists(_path), Is.False);
            Assert.That(File.Exists(_path + SaveService.TempSuffix), Is.False);
            Assert.That(File.Exists(_path + SaveService.BackupSuffix), Is.False);
        }
    }
}
