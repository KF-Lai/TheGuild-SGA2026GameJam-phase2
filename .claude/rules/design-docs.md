---
paths:
  - "design/gdd/**"
---

# Design Document Rules

- Every design document MUST contain these 8 sections: Overview, Player Fantasy, Detailed Rules, Formulas, Edge Cases, Dependencies, Tuning Knobs, Acceptance Criteria
- Formulas must include variable definitions, expected value ranges, and example calculations
- Edge cases must explicitly state what happens, not just "handle gracefully"
- Dependencies must be bidirectional — if system A depends on B, B's doc must mention A
- Tuning knobs must specify safe ranges and what gameplay aspect they affect
- Acceptance criteria must be testable — a QA tester must be able to verify pass/fail
- No hand-waving: "the system should feel good" is not a valid specification
- Balance values must link to their source formula or rationale
- Design documents MUST be written incrementally: create skeleton first, then fill each section one at a time with user approval between sections

## 語言規範

- 設計文件必須以**繁體中文**撰寫
- 以下內容保持英文原文，不翻譯：
  - 專有名詞（如 MDA Framework、Bartle Taxonomy、Self-Determination Theory）
  - Unity 工具與欄位名稱（如 ScriptableObject、MonoBehaviour、SerializeField）
  - 程式碼中的識別符號（變數名、函式名、類別名）
  - 設計參數名稱（如 BASE_REWARD、COMMISSION_RATE、rankDiff）
- 程式碼註釋（comments）必須以繁體中文撰寫
- 章節標題可採用「繁體中文（English）」格式，如「核心循環（Core Loop）」
