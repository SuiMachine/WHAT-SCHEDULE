# AI Usage Policy
Because it's 2026, this project now allows for AI assisted coding. With that said - what is often described as "vibe-coding" is not allowed. This document is a set of guidelines on how you may utilize artificial intelligence (AI) tools to further expend this project and what to avoid when contributing to this repository. The project is licensed under the GNU General Public License v3.0 (see [LICENSE.md](LICENSE.md)), which is built on the principles of transparency, attribution, and user freedom. This AI policy extends those same principles to the use of AI tooling.

By contributing to this repository, you agree to comply with this policy.

---

## 1. Guiding Principles

All use of AI in this project must be:

- **Moral** — AI must never be used to deceive, mislead, or misattribute.
- **Transparent** — Contributors must disclose when and how AI was used.
- **Accountable** — The human contributor is fully responsible for everything they submit, whether written by hand or with AI assistance. "The AI wrote it" is never a valid excuse for defects, license violations.

---

## 2. Permitted Uses of AI

AI tools **may** be used for following scenarios, provided their usage is disclosed.

- Writing, editing, refactoring, and debugging **source code**, but you need to know what the committed code does.
- Generating tests, provided the contributor has verified that the tests are correct and meaningful.
- Assisting with code review and analysis (the final judgment always rests with a human).
- Answering questions, providing examples, and general learning support.

## 3. Prohibited Uses of AI

The following are **strictly prohibited**:

### 3.1 No AI-Generated Images or Media

- **No AI-generated images, artwork, illustrations, icons, logos, or other visual media may be added to this repository.**
- Any assets committed to this repository must have been created by a human, with a clearly documented origin and license permitting their use in this project. **You are not allowed to use AI to edit those assets unless the license attached to the asset says otherwise.**

### 3.2 Merge Requests Must Be Written by Hand

- **Merge request (pull request) titles and descriptions/comments must be written by a human, using your own words. YOU must be able to explain what the change does and fully understand what the code does.**
- AI may not be used to draft, rephrase, or "polish" merge request titles, descriptions, or review comments. These are the human voice of the contributor and must remain so.
- Rationale: a merge request is a communication between humans. Its author must understand and be able to defend every claim in it. Commit messages are expected to follow the same spirit.

### 3.3 No AI Rewriting to Evade Attribution or Licensing

- **It is prohibited to take code from another repository, project, or source and have AI rewrite it in order to disguise its origin or avoid crediting the original author(s).**
- This includes (but is not limited to):
  - Copying code from another project and asking AI to "clean it up," "restructure it," or "make it different" so the original author need not be credited.
  - Using AI to launder code from a project whose license would otherwise require attribution or impose copyleft obligations (such as the GPL) in order to avoid those obligations.
  - Using AI to obfuscate or strip copyright notices, license headers, or author attribution from third-party code.
- **These practices are immoral, dishonest, and may constitute copyright infringement.** They are a violation of this project's values and potentially of the law.

### 3.4 Other Prohibitions

- Do not submit AI-generated code that you do not understand and cannot explain.
- Do not use AI to generate commit messages, changelogs, or release notes that misrepresent the nature of a change.
- Do not use AI to automate commits, reviews, or merges without explicit human involvement and oversight.
- Readme and documentation has to be written by hand.

---

## 4. Third-Party Code and Licensing

- If you borrow, adapt, or are inspired by code from another project, you **must**:
  1. Identify the source project and its authors.
  2. Verify that its license permits use in this project (this project is GPL-3.0 — compatible licenses must be respected accordingly).
  3. Credit the source explicitly in the commit, the merge request, and where appropriate in the code itself (e.g., a comment with a link to the original).
- When in doubt: disclose. If you are unsure whether AI assistance crossed a line, assume worst scenario and write it by hand.
- AI must never be used as a mechanism to circumvent these requirements. The obligation to credit and comply with licenses lies with the human contributor, always.

---

## 5. Disclosure Requirements

- If AI meaningfully contributed to a contribution, note this in the merge request description. A brief note is sufficient, e.g.: *"Part of this code was written with the assistance of [tool]; I have reviewed and take full responsibility for it."*
- You are not required to disclose incidental AI use (e.g., autocomplete, spelling suggestions, or asking a question about an error message).
- **The contributor must always be able to explain what their AI-assisted code does and why.**

---

## 6. Enforcement

Violations of this policy — particularly attempts to disguise third-party code, evade attribution, or mislead maintainers — will be treated seriously. Maintainers may reject contributions, request rewrites, or revert changes that violate this policy. Repeated or deliberate violations may result in contributions being rejected entirely.

If you notice a contribution that appears to violate this policy, raise it with a maintainer privately.

---

## 7. Summary

| Topic | Rule |
|---|---|
| AI-generated images | **Prohibited.** Only human-created assets only due to legal swamp this area is. |
| Merge request titles/comments | **Must be hand-written** by a human. |
| AI rewriting third-party code to avoid credit | **Prohibited.** Always moral and transparent. |
| Third-party code use | Credit the source, respect its license. |
| General AI use in code | Permitted with disclosure, understanding, and accountability. |

---

*This policy may change in the future to address any possible abusive behavior as practices around AI usage evolve.*
