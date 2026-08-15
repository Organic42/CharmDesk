using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CharmDesk.Persistence;

namespace CharmDesk.Commerce;

public enum TipTier { Small, Medium, Large }

public enum TipOutcome { Succeeded, Cancelled, Failed, NotAvailable }

public sealed record TipProduct(TipTier Tier, string? FormattedPrice);

public sealed record TipResult(TipOutcome Outcome, string? Message = null);

/// <summary>
/// Optional "tip the developer" purchases - entirely optional, nothing else in the app depends
/// on this. Handled two different ways depending on how CharmDesk is running, the same split as
/// <see cref="StartupManager"/>:
///
/// - <b>Packaged (MSIX / Microsoft Store):</b> real in-app purchases via
///   <c>Windows.Services.Store.StoreContext</c>, as three Consumable add-ons. Requires those
///   add-ons to exist in Partner Center first - see packaging/tip-jar-setup.md - and the
///   placeholder Store IDs below to be swapped for the real ones once they're created.
/// - <b>Unpackaged (plain exe, dev build, the zip shared with friends):</b> the Store APIs need
///   package identity and throw without it, so this falls back to opening a donation page in
///   the browser instead.
///
/// Every path fails soft (log and return NotAvailable/Failed) rather than interrupting anything
/// else CharmDesk does - a tip that doesn't go through should never be worse than a quiet retry.
/// </summary>
public static class TipJarManager
{
    // TODO: replace with the real Store IDs Partner Center assigns once these three Consumable
    // add-ons exist (see packaging/tip-jar-setup.md). Purchases fail gracefully until then.
    private static readonly Dictionary<TipTier, string> StoreIds = new()
    {
        [TipTier.Small] = "REPLACE_WITH_SMALL_TIP_STORE_ID",
        [TipTier.Medium] = "REPLACE_WITH_MEDIUM_TIP_STORE_ID",
        [TipTier.Large] = "REPLACE_WITH_LARGE_TIP_STORE_ID",
    };

    private static readonly Dictionary<TipTier, string> DefaultLabels = new()
    {
        [TipTier.Small] = "Small",
        [TipTier.Medium] = "Medium",
        [TipTier.Large] = "Large",
    };

    // TODO: point this at your real donation page (Ko-fi / GitHub Sponsors / PayPal.me / etc.)
    // before shipping. Only used by unpackaged builds, where Store purchases aren't available.
    private const string ExternalTipUrl = "https://example.com/replace-with-your-donation-link";

    public static readonly IReadOnlyList<TipTier> AllTiers = new[] { TipTier.Small, TipTier.Medium, TipTier.Large };

    public static string DefaultLabel(TipTier tier) => DefaultLabels[tier];

    /// <summary>True when running with package identity, so the Store purchase APIs are usable
    /// at all. Reuses the same check <see cref="StartupManager"/> already does for the same
    /// reason (StartupTask and Store purchases both require package identity).</summary>
    public static bool IsNativePurchaseAvailable => StartupManager.IsPackaged;

    /// <summary>Looks up live prices for the three tip tiers from the Store. Returns an empty
    /// list if unpackaged, offline, or the app isn't associated with a Partner Center listing
    /// (or the placeholder Store IDs haven't been replaced yet) - callers should fall back to
    /// showing the default labels without a price in all of those cases.</summary>
    public static async Task<IReadOnlyList<TipProduct>> GetTipProductsAsync()
    {
        if (!IsNativePurchaseAvailable) return Array.Empty<TipProduct>();

#if PACKAGED_BUILD
        try
        {
            var context = global::Windows.Services.Store.StoreContext.GetDefault();
            var result = await context.GetStoreProductsAsync(new[] { "Consumable" }, StoreIds.Values.ToArray());
            if (result.ExtendedError is not null)
            {
                Logger.Log("TipJarManager.GetTipProductsAsync", result.ExtendedError);
                return Array.Empty<TipProduct>();
            }

            var products = new List<TipProduct>();
            foreach (var (tier, storeId) in StoreIds)
            {
                if (result.Products.TryGetValue(storeId, out var product))
                    products.Add(new TipProduct(tier, product.Price?.FormattedPrice));
            }
            return products;
        }
        catch (Exception ex)
        {
            Logger.Log("TipJarManager.GetTipProductsAsync", ex);
            return Array.Empty<TipProduct>();
        }
#else
        await Task.CompletedTask;
        return Array.Empty<TipProduct>();
#endif
    }

    /// <summary>Opens the Store's native purchase dialog for the given tier. Microsoft handles
    /// the actual charge - CharmDesk never sees payment details, same as any Store purchase.</summary>
    public static async Task<TipResult> RequestTipAsync(TipTier tier)
    {
        if (!IsNativePurchaseAvailable)
            return new TipResult(TipOutcome.NotAvailable, "Native tipping needs the Microsoft Store version of CharmDesk.");

#if PACKAGED_BUILD
        if (!StoreIds.TryGetValue(tier, out var storeId))
            return new TipResult(TipOutcome.NotAvailable);

        try
        {
            var context = global::Windows.Services.Store.StoreContext.GetDefault();
            var purchase = await context.RequestPurchaseAsync(storeId);

            switch (purchase.Status)
            {
                case global::Windows.Services.Store.StorePurchaseStatus.Succeeded:
                    await FulfillConsumableAsync(context, storeId);
                    return new TipResult(TipOutcome.Succeeded, "Thank you for the tip!");
                case global::Windows.Services.Store.StorePurchaseStatus.NotPurchased:
                    return new TipResult(TipOutcome.Cancelled);
                case global::Windows.Services.Store.StorePurchaseStatus.NetworkError:
                    return new TipResult(TipOutcome.Failed, "No connection to the Store - try again later.");
                default:
                    return new TipResult(TipOutcome.Failed, "The purchase didn't go through.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"TipJarManager.RequestTipAsync({tier})", ex);
            return new TipResult(TipOutcome.Failed, "The purchase didn't go through.");
        }
#else
        await Task.CompletedTask;
        return new TipResult(TipOutcome.NotAvailable);
#endif
    }

#if PACKAGED_BUILD
    /// <summary>Consumables carry a Store-tracked balance from each purchase; a tip has nothing
    /// to redeem, so this immediately consumes the full balance back to zero - otherwise the
    /// same tier can't be bought again until something reports it fulfilled. Best-effort: the
    /// purchase itself already succeeded by the time this runs, so a failure here just means the
    /// tier might not be purchasable again until the balance is cleared some other way.</summary>
    private static async Task FulfillConsumableAsync(global::Windows.Services.Store.StoreContext context, string storeId)
    {
        try
        {
            var balance = await context.GetConsumableBalanceRemainingAsync(storeId);
            if (balance.ExtendedError is not null || balance.BalanceRemaining <= 0) return;

            await context.ReportConsumableFulfillmentAsync(storeId, balance.BalanceRemaining, Guid.NewGuid());
        }
        catch (Exception ex)
        {
            Logger.Log($"TipJarManager.FulfillConsumableAsync({storeId})", ex);
        }
    }
#endif

    /// <summary>Opens the donation page in the default browser. Used by unpackaged builds, since
    /// Store purchases aren't available there at all.</summary>
    public static void OpenExternalTipPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExternalTipUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Logger.Log("TipJarManager.OpenExternalTipPage", ex);
        }
    }
}
