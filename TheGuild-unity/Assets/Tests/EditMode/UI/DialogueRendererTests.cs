using System.Collections.Generic;
using NUnit.Framework;
using TheGuild.UI.Core;
using TheGuild.UI.Scene;
using UnityEngine.UIElements;

namespace Tests.EditMode.UI
{
    public sealed class DialogueRendererTests
    {
        [Test]
        public void DoD_A7_SplitDialogue_ChineseBreaksByConfiguredLength()
        {
            DialogueRenderer renderer = new DialogueRenderer();

            List<List<string>> pages = renderer.SplitDialogue("一二三四五六七八九十十一十二", 6, 3);

            Assert.AreEqual("一二三四五六", pages[0][0]);
            Assert.AreEqual("七八九十十一", pages[0][1]);
            Assert.AreEqual("十二", pages[0][2]);
        }

        [Test]
        public void DoD_A7_SplitDialogue_EnglishUsesWordBoundaries()
        {
            DialogueRenderer renderer = new DialogueRenderer();

            List<List<string>> pages = renderer.SplitDialogue("hello brave world", 12, 3);

            Assert.AreEqual("hello brave", pages[0][0]);
            Assert.AreEqual("world", pages[0][1]);
        }

        [Test]
        public void DoD_A7_SplitDialogue_MixedTextPrefersPunctuationThenWhitespace()
        {
            DialogueRenderer renderer = new DialogueRenderer();

            List<List<string>> pages = renderer.SplitDialogue("你好，abcdef ghi", 8, 3);

            Assert.AreEqual("你好，", pages[0][0]);
            Assert.AreEqual("abcdef", pages[0][1]);
        }

        [Test]
        public void EC20_RenderMissingDialogue_ShowsPlaceholder()
        {
            DialogueRenderer renderer = new DialogueRenderer();
            VisualElement root = new VisualElement();

            renderer.RenderToPanel(root, "missing.dialogue", 1, new P02UITuning());

            Label label = root.Query<Label>().First();
            Assert.IsNotNull(label);
            Assert.AreEqual("[對話缺失：missing.dialogue]", label.text);
        }
    }
}
