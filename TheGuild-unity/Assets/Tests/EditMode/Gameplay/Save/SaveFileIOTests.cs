using System;
using System.IO;
using NUnit.Framework;
using TheGuild.Gameplay.Save;

namespace Tests.EditMode.Gameplay.Save
{
    public sealed class SaveFileIOTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ft10-save-fileio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void HasAnySaveFile_ReturnsTrue_WhenTerminalExists()
        {
            File.WriteAllText(Path.Combine(_tempDir, "save.json"), "{}");
            Assert.IsTrue(SaveFileIO.HasAnySaveFile(_tempDir, "save.json", "save.backup", 3));
        }

        [Test]
        public void HasAnySaveFile_ReturnsTrue_WhenBackupExists()
        {
            File.WriteAllText(Path.Combine(_tempDir, "save.backup2.json"), "{}");
            Assert.IsTrue(SaveFileIO.HasAnySaveFile(_tempDir, "save.json", "save.backup", 3));
        }

        [Test]
        public void WriteSaveWithRotation_CreatesTerminalFile()
        {
            bool ok = SaveFileIO.WriteSaveWithRotation(_tempDir, "save.json", "save.backup", 3, "{\"v\":1}", out Exception ex);
            Assert.IsTrue(ok);
            Assert.IsNull(ex);
            Assert.AreEqual("{\"v\":1}", File.ReadAllText(Path.Combine(_tempDir, "save.json")));
        }

        [Test]
        public void DeleteAllSaveFiles_RemovesTerminalAndBackups()
        {
            File.WriteAllText(Path.Combine(_tempDir, "save.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDir, "save.backup1.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDir, "save.backup2.json"), "{}");

            bool any = SaveFileIO.DeleteAllSaveFiles(_tempDir, "save.json", "save.backup", 3);

            Assert.IsTrue(any);
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "save.json")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "save.backup1.json")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "save.backup2.json")));
        }
    }
}
