using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools.Items
{
    /// <summary>
    /// A fingerprint of an item as it is now - identity, location and revision - that a confirmation
    /// token is bound to, so an item edited, moved or renamed after its preview invalidates the token.
    /// </summary>
    public static class ItemFingerprint
    {
        /// <summary>Returns the fingerprint of the item's current version.</summary>
        public static string Of(Item item) =>
            item == null
                ? string.Empty
                : string.Join("|",
                    item.Database.Name,
                    item.ID.ToString(),
                    item.Paths.FullPath,
                    item.Language.Name,
                    item.Version.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Statistics.Revision);
    }
}
