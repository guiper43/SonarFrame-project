---
name: novidades
description: Generate the bilingual (PT-BR + EN) marketing text the user posts in his WhatsApp/Telegram groups to announce AcessGallery's new features, and save it to mk.txt at the repo root. Use when the user runs /novidades with a list of what's new (in any format — a dash-bulleted release-notes paragraph, free text, or a reference to specific features), or otherwise asks to write/update this kind of group-broadcast marketing text for a new version.
---

# Novidades (group marketing text)

Produces the recurring PT-BR + EN marketing message the user sends to his groups whenever he releases a new AcessGallery version, written to `mk.txt` at the repo root.

## Input

`args` is the user's raw description of what's new — often shaped like the app's own release notes (one or more `- ` bulleted items, e.g. the style in `distribution/whatsnew/whatsnew-pt-BR`), but may also be loose free text or just feature names. Treat it as the list of topics to cover, not as finished prose to paste in.

## The one rule that matters: write NEW text, never reuse old prose

The user has corrected this twice already (see memory `feedback_marketing_text.md`): do **not** copy-paste wording from a previous `mk.txt`, from an example he pastes as a style reference, or from the `distribution/whatsnew/*` files. Those are inputs for *what* to cover and *how it's formatted*, never text to recycle. Every run of this skill must produce original sentences describing the given novelties — even if a past message already covered a similar-sounding feature.

## Ground each item in the real feature before writing marketing copy

Before writing a line about a feature, verify what it actually does and how it's gated, so the copy is accurate — don't market from the bullet text alone:

- Search `tools/i18n/strings.tsv` for the feature's `Help_Feature_*_Title`/`_Description` entries (the in-app Ajuda tab descriptions) and its settings-screen strings (e.g. `AiSettings_*`, `GeneralSettings_*`) — these give the precise, already-reviewed wording of what the feature does and any constraints.
- Check whether the feature is gated (Pro subscription, Plano Facilitador, or free) by grepping the relevant ViewModel/service rather than assuming — e.g. `Subscription_Upsell_*` strings or an `ISubscriptionService` check nearby. Only tag `(Pro)` / `(Plano Facilitador)` / `(Pro ou Facilitador)` when the code actually confirms the gate; leave it untagged if the feature is free for everyone.
- If the version these novidades belong to isn't obvious from `args`, don't silently assume the repo's current `ApplicationDisplayVersion` in the `.csproj` is already public — that field can already point at a version still sitting on Google Play's **internal testing track**. Ask the user which version this campaign is for if it matters, rather than guessing.

## Output format

Write to `mk.txt` at the repo root (overwrite it), with this shape — it mirrors the structure the user has used historically, but every sentence should be freshly written for this run:

```
===========================
PT-BR
===========================

📢 <attention-grabbing header for this batch of novidades> 🎉

<one short intro line tying the items together, optional>

<emoji> <Feature name>
<1-3 original sentences: what it does, in plain terms, and why it helps a blind/low-vision user. Add a "(Pro)" / "(Plano Facilitador)" / "(Pro ou Facilitador)" tag only if confirmed gated.>

<repeat the emoji+feature block for each item in args>

🛠️ Melhorias de estabilidade, como sempre.   <- only if the input mentions stability/bug fixes

📲 Atualize o AcessGallery e aproveite as novidades:

https://play.google.com/store/apps/details?id=com.RafaelFernandes.acessgallery


===========================
EN
===========================

<same structure, translated — not a literal word-for-word translation, write it as natural English marketing copy conveying the same facts>
```

Notes on format:
- Pick a different emoji per feature based on what it actually is (🔎 for something visual/exploratory, 🎭 for characters, 🔊 for audio/volume, ✅ for a simple capability, etc.) — don't reuse the same emoji for everything.
- The Play Store link is the one constant that's always reused verbatim: `https://play.google.com/store/apps/details?id=com.RafaelFernandes.acessgallery`.
- Keep the PT-BR audience in mind as primary (this is the app's main user base — comments and most strings are Portuguese); EN is a faithful second version, not an afterthought, but doesn't need to be longer or more detailed than the PT-BR one.
- The app is for blind and low-vision users — accessibility/screen-reader framing (how a feature helps someone who can't see the screen) is usually the actual selling point, more than general appeal.

## After writing

Tell the user, briefly, which features you covered and where you grounded each one (the strings.tsv keys you checked), so they can spot anything you got wrong about gating or behavior before sending it to the groups.
