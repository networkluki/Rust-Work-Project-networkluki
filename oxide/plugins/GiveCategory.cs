using System;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("GiveCategory", "networkluki", "1.0.0")]
    [Description("Give all items from a category")]

    public class GiveCategory : RustPlugin
    {

        [ChatCommand("givecategory")]
        void GiveCategoryCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("Admin only.");
                return;
            }

            if (args.Length < 2)
            {
                player.ChatMessage("/givecategory <category> <amount>");
                return;
            }

            string categoryName = args[0];
            int amount;

            if (!int.TryParse(args[1], out amount))
            {
                player.ChatMessage("Amount must be number.");
                return;
            }

            ItemCategory category;

            if (!Enum.TryParse(categoryName, true, out category))
            {
                player.ChatMessage("Invalid category.");
                return;
            }

            var items = ItemManager.itemList.Where(x => x.category == category);

            int count = 0;

            foreach (var itemDef in items)
            {
                var item = ItemManager.Create(itemDef, amount);

                if (!player.inventory.GiveItem(item))
                {
                    item.Drop(player.transform.position, Vector3.zero);
                }

                count++;
            }

            player.ChatMessage($"Gave {amount} of {count} items from category {category}.");
        }
    }
}