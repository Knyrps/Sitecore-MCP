using Sitecore.Data.Items;

namespace SitecoreMcp.Server.Tools.Items
{
    public static class ItemHelper
    {
        /// <summary>
        /// Validates the item name and throws an exception if it is invalid.
        /// </summary>
        /// <param name="name">The item name</param>
        /// <param name="message">An optional message to prepend to the error message</param>
        /// <exception cref="McpToolException"></exception>
        public static void ValidateName(string name, string message = "")
		{
			if (!ItemUtil.IsItemNameValid(name))
			{
				throw new McpToolException(message + ItemUtil.GetItemNameError(name));
			}
        }
    }
}
