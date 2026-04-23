using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using UnityEngine.TestTools;

namespace Tests.EditMode.Core.Data
{
    public sealed class CsvParserTests
    {
        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AC_DM_13_MultiValueField_QuotedCommaParsedCorrectly()
        {
            string csv =
                "id,name,cost,weight,tags\n" +
                "warrior,勇者,100,0.5,melee|physical\n" +
                "rogue,盜賊,120,0.2,\"melee|stealth,unique\"\n";

            var parsed = CsvParser.Parse(csv, typeof(TestTableData), "TestTable");
            var rogue = (TestTableData)parsed["rogue"];

            Assert.AreEqual(2, rogue.tags.Length);
            Assert.AreEqual("melee", rogue.tags[0]);
            Assert.AreEqual("stealth,unique", rogue.tags[1]);
        }

        [Test]
        public void AC_DM_14_CommentRowsAreIgnored()
        {
            string csv =
                "id,name,cost\n" +
                "warrior,勇者,100\n" +
                "# comment row\n" +
                "mage,法師,150\n";

            var parsed = CsvParser.Parse(csv, typeof(TestTableData), "TestTable");

            Assert.AreEqual(2, parsed.Count);
            Assert.IsTrue(parsed.ContainsKey("warrior"));
            Assert.IsTrue(parsed.ContainsKey("mage"));
        }

        [Test]
        public void AC_DM_15_DuplicatePrimaryKey_LastRowOverridesAndWarns()
        {
            string csv =
                "id,name,cost\n" +
                "warrior,勇者,100\n" +
                "warrior,新勇者,200\n";

            LogAssert.Expect(LogType.Warning, new Regex("主鍵重複，後者覆蓋前者"));
            var parsed = CsvParser.Parse(csv, typeof(TestTableData), "TestTable");

            var warrior = (TestTableData)parsed["warrior"];
            Assert.AreEqual("新勇者", warrior.name);
            Assert.AreEqual(200, warrior.cost);
        }

        [Test]
        public void AC_DM_16_LongField_ParsesCorrectly()
        {
            string csv =
                "id,warningDurationSec\n" +
                "ruleA,86400\n";

            var parsed = CsvParser.Parse(csv, typeof(TestLongTableData), "LongTable");
            var row = (TestLongTableData)parsed["ruleA"];

            Assert.AreEqual(86400L, row.warningDurationSec);
        }

        [Serializable]
        private sealed class TestTableData
        {
            public string id;
            public string name;
            public int cost;
            public float weight;
            public string[] tags;
        }

        [Serializable]
        private sealed class TestLongTableData
        {
            public string id;
            public long warningDurationSec;
        }
    }
}
