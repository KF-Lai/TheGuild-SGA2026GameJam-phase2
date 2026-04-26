using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TheGuild.Core.Data;
using UnityEngine;
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
                "id,warrior,rogue\n" +
                "name,勇者,盜賊\n" +
                "cost,100,120\n" +
                "weight,0.5,0.2\n" +
                "tags,melee|physical,\"melee|stealth,unique\"\n";

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
                "id,warrior,mage\n" +
                "name,勇者,法師\n" +
                "# comment row\n" +
                "cost,100,150\n";

            var parsed = CsvParser.Parse(csv, typeof(TestTableData), "TestTable");

            Assert.AreEqual(2, parsed.Count);
            Assert.IsTrue(parsed.ContainsKey("warrior"));
            Assert.IsTrue(parsed.ContainsKey("mage"));
        }

        [Test]
        public void AC_DM_15_DuplicatePrimaryKey_LastRowOverridesAndWarns()
        {
            string csv =
                "id,warrior,warrior\n" +
                "name,勇者,新勇者\n" +
                "cost,100,200\n";

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
                "id,ruleA\n" +
                "warningDurationSec,86400\n";

            var parsed = CsvParser.Parse(csv, typeof(TestLongTableData), "LongTable");
            var row = (TestLongTableData)parsed["ruleA"];

            Assert.AreEqual(86400L, row.warningDurationSec);
        }

        [Test]
        public void Smoke_FakeTransposedTable_AllFeaturesWorkEndToEnd()
        {
            string csv =
                "fakeID,1,2,3\n" +
                "name,戰士,法師,\"含逗號, 看這裡\"\n" +
                "memberIDs,a|b|c,x|y,z\n" +
                "# 這是註解列，應被略過\n" +
                "cost,100,200,300\n" +
                "\n" +
                "weight,0.5,1.5,2.5\n";

            var parsed = CsvParser.Parse(csv, typeof(SmokeData), "FakeSmokeTable");

            Assert.AreEqual(3, parsed.Count, "應解析出 3 筆記錄");

            var rec1 = (SmokeData)parsed["1"];
            var rec2 = (SmokeData)parsed["2"];
            var rec3 = (SmokeData)parsed["3"];

            Assert.AreEqual(1, rec1.fakeID);
            Assert.AreEqual("戰士", rec1.name);
            Assert.AreEqual(3, rec1.memberIDs.Length);
            Assert.AreEqual("a", rec1.memberIDs[0]);
            Assert.AreEqual(100, rec1.cost);
            Assert.AreEqual(0.5f, rec1.weight, 0.0001f);

            Assert.AreEqual("法師", rec2.name);
            Assert.AreEqual(2, rec2.memberIDs.Length);
            Assert.AreEqual(200, rec2.cost);

            Assert.AreEqual("含逗號, 看這裡", rec3.name, "quoted comma 應原樣保留");
            Assert.AreEqual(1, rec3.memberIDs.Length);
            Assert.AreEqual("z", rec3.memberIDs[0]);
            Assert.AreEqual(300, rec3.cost);

            Debug.Log(
                $"[Smoke] Parse 通過：N={parsed.Count}  " +
                $"rec1=(id={rec1.fakeID}, name={rec1.name}, mem=[{string.Join("|", rec1.memberIDs)}], cost={rec1.cost}, weight={rec1.weight})  " +
                $"rec2=(id={rec2.fakeID}, name={rec2.name}, cost={rec2.cost})  " +
                $"rec3=(id={rec3.fakeID}, name=\"{rec3.name}\")");

            string sysCsv =
                "key,FAKE_RATE,FAKE_NUM,FAKE_NAME\n" +
                "value,0.75,42,Hello\n" +
                "description,測試比例,測試整數,字串\n";
            var consts = CsvParser.ParseSystemConstants(sysCsv, "FakeSC");

            Assert.AreEqual(3, consts.Count);
            Assert.AreEqual("0.75", consts["FAKE_RATE"]);
            Assert.AreEqual("42", consts["FAKE_NUM"]);
            Assert.AreEqual("Hello", consts["FAKE_NAME"]);

            Debug.Log($"[Smoke] ParseSystemConstants 通過：FAKE_RATE={consts["FAKE_RATE"]}, FAKE_NUM={consts["FAKE_NUM"]}, FAKE_NAME={consts["FAKE_NAME"]}");
        }

        [Test]
        public void Smoke_FakeTable_BoundaryCases_HandledAsExpected()
        {
            LogAssert.Expect(LogType.Warning, new Regex("欄位數不符"));
            string badCsv =
                "fakeID,1,2,3\n" +
                "cost,100,200\n" +
                "name,a,b,c\n";
            var bad = CsvParser.Parse(badCsv, typeof(SmokeData), "BadColCount");
            Assert.AreEqual(3, bad.Count, "PK 列定義 3 筆記錄，cost 列雖被跳過，name 列仍能綁定");
            var b1 = (SmokeData)bad["1"];
            Assert.AreEqual(0, b1.cost, "cost 列被跳過，cost 應保持預設 0");
            Assert.AreEqual("a", b1.name);

            LogAssert.Expect(LogType.Warning, new Regex("主鍵重複"));
            string dupCsv =
                "fakeID,1,1\n" +
                "name,first,second\n";
            var dup = CsvParser.Parse(dupCsv, typeof(SmokeData), "DupPk");
            Assert.AreEqual(1, dup.Count, "重複 PK 應後者覆蓋");
            Assert.AreEqual("second", ((SmokeData)dup["1"]).name);

            Debug.Log("[Smoke] 邊界處理通過：欄位數不符部分綁定 + 重複 PK 後者覆蓋");
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

        [Serializable]
        private sealed class SmokeData
        {
            public int fakeID;
            public string name;
            public string[] memberIDs = System.Array.Empty<string>();
            public int cost;
            public float weight;
        }
    }
}
