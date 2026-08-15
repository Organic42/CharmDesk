# Tip jar setup

CharmDesk has an optional "tip the developer" feature (Settings, and a link from About). It's
two code paths, matching the same packaged/unpackaged split as `StartupManager`:

- **Store version:** real in-app purchases via `Windows.Services.Store`, as three Consumable
  add-ons ("Small", "Medium", "Large"). Implemented in
  [`src/CharmDesk/Commerce/TipJarManager.cs`](../src/CharmDesk/Commerce/TipJarManager.cs).
- **Unpackaged / shared zip:** Store purchases aren't possible without package identity, so this
  opens a donation page in the browser instead.

Two things need to be filled in before either path works for real. Until then everything fails
soft - the buttons just don't do anything useful, nothing crashes.

## 1. The three Consumable add-ons (for the Store path)

In [Partner Center](https://partner.microsoft.com/dashboard) → your CharmDesk app → **Add-ons** →
**New add-on**, create three, each as a **Consumable** product:

| Suggested Product ID | Suggested title | Suggested price |
|---|---|---|
| `tip_small`  | Small tip  | ~$1  |
| `tip_medium` | Medium tip | ~$3  |
| `tip_large`  | Large tip  | ~$5  |

Product ID, price, and title are entirely up to you - nothing in the code depends on the exact
values, only on where the resulting **Store ID** ends up (next step). Each add-on needs its own
store listing (title + description) same as the app itself, and needs to be submitted/published
before purchases against it will work for real users.

Once each add-on exists, Partner Center shows its **Store ID** (a generated string, distinct
from the Product ID you typed in) on the add-on's overview page. Copy the three Store IDs into
`TipJarManager.cs`, replacing the placeholders:

```csharp
private static readonly Dictionary<TipTier, string> StoreIds = new()
{
    [TipTier.Small] = "REPLACE_WITH_SMALL_TIP_STORE_ID",
    [TipTier.Medium] = "REPLACE_WITH_MEDIUM_TIP_STORE_ID",
    [TipTier.Large] = "REPLACE_WITH_LARGE_TIP_STORE_ID",
};
```

**Testing without spending real money:** if you're signed into Windows with the same Microsoft
account used to submit the app in Partner Center, Store purchases against your own app run in a
built-in test mode automatically - the purchase dialog appears and completes normally, but you're
never actually charged. No separate sandbox/simulator setup needed.

## 2. The donation link (for the unpackaged path)

In the same file, replace the placeholder URL with your real one (Ko-fi, GitHub Sponsors,
PayPal.me, Buy Me a Coffee, whatever you'd rather use):

```csharp
private const string ExternalTipUrl = "https://example.com/replace-with-your-donation-link";
```

This is also what the `build-share-zip.ps1` build hands to friends, so it's worth setting even
if you don't touch the Store add-ons right away.

## Notes

- Both code paths are entirely optional - nothing else in CharmDesk depends on this working, and
  a failed/cancelled purchase never blocks or interrupts anything.
- Consumables carry a Store-tracked balance per purchase. `TipJarManager` reports each
  successful tip as fully consumed right away (there's nothing to redeem), so the same tier can
  be bought again without Partner Center thinking it's still "owed" to the user.
- This hasn't been exercised against a live Partner Center listing yet - both build
  configurations compile cleanly against the real `Windows.Services.Store` WinRT projection, but
  the actual purchase flow should get a real test pass once the add-ons above exist.
