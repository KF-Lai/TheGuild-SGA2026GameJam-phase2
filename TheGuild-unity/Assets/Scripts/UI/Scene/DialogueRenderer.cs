using System;
using System.Collections.Generic;
using System.Text;
using TheGuild.Core.Data;
using TheGuild.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TheGuild.UI.Scene
{
    public sealed class DialogueRenderer
    {
        private static readonly char[] BreakPunctuation = { '，', '。', '、', '！', '？', ',', '.', '!', '?', ';', ':' };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterTables()
        {
            DataManager.RegisterTable<DialogueRow>("DialogueTable");
        }

        public void RenderToPanel(VisualElement targetContainer, string dialogueKey, int stageID, P02UITuning tuning)
        {
            if (targetContainer == null)
            {
                return;
            }

            targetContainer.Clear();
            DialogueRow row = DataManager.Instance == null ? null : DataManager.Instance.Get<DialogueRow>(dialogueKey);
            string rawText = row == null ? $"[對話缺失：{dialogueKey}]" : row.text;

            ApplyStageStyle(targetContainer, dialogueKey, stageID, tuning);
            List<List<string>> pages = SplitDialogue(
                rawText,
                tuning == null ? 24 : tuning.DialogueLineMaxChars,
                tuning == null ? 3 : tuning.DialogueLinesPerPage);

            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                VisualElement page = new VisualElement { name = $"dialogue-page-{pageIndex}" };
                for (int lineIndex = 0; lineIndex < pages[pageIndex].Count; lineIndex++)
                {
                    page.Add(new Label(pages[pageIndex][lineIndex]));
                }
                targetContainer.Add(page);
            }
        }

        public List<List<string>> SplitDialogue(string rawText, int lineMaxChars, int linesPerPage)
        {
            List<List<string>> pages = new List<List<string>>();
            List<string> lines = SplitLines(rawText ?? string.Empty, Mathf.Max(1, lineMaxChars));
            int pageSize = Mathf.Max(1, linesPerPage);

            for (int i = 0; i < lines.Count; i += pageSize)
            {
                List<string> page = new List<string>(pageSize);
                for (int j = i; j < lines.Count && j < i + pageSize; j++)
                {
                    page.Add(lines[j]);
                }
                pages.Add(page);
            }

            if (pages.Count == 0)
            {
                pages.Add(new List<string> { string.Empty });
            }

            return pages;
        }

        private List<string> SplitLines(string text, int maxChars)
        {
            List<string> lines = new List<string>();
            StringBuilder line = new StringBuilder(maxChars + 8);
            int lastSpace = -1;
            int lastPunctuation = -1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                line.Append(c);

                if (char.IsWhiteSpace(c))
                {
                    lastSpace = line.Length - 1;
                }
                else if (IsBreakPunctuation(c))
                {
                    lastPunctuation = line.Length;
                }

                if (line.Length < maxChars)
                {
                    continue;
                }

                int breakAt = ChooseBreakIndex(line, lastPunctuation, lastSpace);
                AddLine(lines, line.ToString(0, breakAt));
                string remaining = line.ToString(breakAt, line.Length - breakAt).TrimStart();
                line.Length = 0;
                line.Append(remaining);
                lastSpace = -1;
                lastPunctuation = -1;
            }

            if (line.Length > 0)
            {
                AddLine(lines, line.ToString());
            }

            return lines;
        }

        private int ChooseBreakIndex(StringBuilder line, int lastPunctuation, int lastSpace)
        {
            if (lastPunctuation > 0)
            {
                return lastPunctuation;
            }

            if (lastSpace > 0 && !ContainsOnlyAsciiWord(line))
            {
                return lastSpace + 1;
            }

            return line.Length;
        }

        private bool ContainsOnlyAsciiWord(StringBuilder value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsLetterOrDigit(c) && c != '\'' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyStageStyle(VisualElement target, string dialogueKey, int stageID, P02UITuning tuning)
        {
            if (stageID == 5)
            {
                target.style.backgroundColor = ParseColor(tuning == null ? "#000001" : tuning.StoryNoteBlackHex, Color.black);
                target.style.color = Color.white;
                return;
            }

            if (stageID == 4 || (!string.IsNullOrEmpty(dialogueKey) && dialogueKey.Contains("stage4")))
            {
                target.style.color = ParseColor(tuning == null ? "#888888" : tuning.StoryNoteGrayHex, Color.gray);
            }
        }

        private Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : fallback;
        }

        private bool IsBreakPunctuation(char c)
        {
            for (int i = 0; i < BreakPunctuation.Length; i++)
            {
                if (BreakPunctuation[i] == c)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLine(List<string> lines, string value)
        {
            lines.Add(string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim());
        }
    }

    public sealed class DialogueRow
    {
        public string dialogueID;
        public string speakerId;
        public string context;
        public string text;
    }
}
