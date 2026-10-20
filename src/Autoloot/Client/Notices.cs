// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using System.Text;
using Autoloot.Config;
using Autoloot.Net;
using Progenitor.Ui;

namespace Autoloot.Client
{
    /// <summary>
    /// The pickup banner: "Copper Ore +20 (140) · Bone +12 (88) · +2 more", each item with how many
    /// were picked up and, in brackets, how many are now in the bags. Ground bags get their own
    /// part: "Death bag emptied (37 items)", on the same line.
    /// </summary>
    internal static class Notices
    {
        const int ItemsShown = 3;
        const double Seconds = 4;

        static bool _loggedFirst;

        internal static void Show(AutolootContext ctx, PlayerConfig player, PickedMsg m)
        {
            if (!_loggedFirst) { _loggedFirst = true; ctx.Log.LogInfo("Autoloot: first pickup notice received (" + m.Items.Count + " items)."); }
            // The server sends notices only while the player's "Pickup notices" is on.
            var text = Text(m, item => ctx.Items.CatalogEntry(item)?.DisplayName);
            if (text.Length > 0) ctx.Hud.Notify(text, kind: UiNoticeKind.Info, seconds: Seconds);
        }

        internal static string Text(PickedMsg m, System.Func<int, string?> nameOf)
        {
            var lines = new List<string>();
            var n = System.Math.Min(m.Items.Count, System.Math.Min(m.Amounts.Count, m.Totals.Count));
            if (n > 0)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < n && i < ItemsShown; i++)
                {
                    if (i > 0) sb.Append(" · ");
                    var name = nameOf(m.Items[i]) is { Length: > 0 } s ? s : "Item " + m.Items[i];
                    sb.Append(name).Append(" +").Append(m.Amounts[i].ToString("#,0")).Append(" (").Append(m.Totals[i].ToString("#,0")).Append(')');
                }
                if (n > ItemsShown) sb.Append(" · +").Append(n - ItemsShown).Append(" more");
                lines.Add(sb.ToString());
            }
            var bags = m.DeathBags + m.DroppedBags;
            if (bags > 0)
            {
                var what = m.DroppedBags == 0 ? (m.DeathBags == 1 ? "Death bag" : $"{m.DeathBags} death bags")
                    : m.DeathBags == 0 ? (m.DroppedBags == 1 ? "Dropped bag" : $"{m.DroppedBags} dropped bags")
                    : $"{bags} bags";
                lines.Add($"{what} emptied ({m.BagItems:#,0} items)");
            }
            return string.Join(" · ", lines);
        }
    }
}
