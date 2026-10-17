using System;
using System.Linq;
using Trading;
using UnityEngine;
using Utils;
using Verse;
using Listing = Agora.Listing;
using ListingType = Agora.ListingType;

namespace PhinixClient.GUI
{
    /// <summary>
    /// Scrollable list of every listing on the market board, each with a button to trade with the poster or take
    /// down our own listing.
    /// </summary>
    public class MarketBoardList
    {
        private const float SCROLLBAR_WIDTH = 16f;

        private const float DEFAULT_SPACING = 10f;

        private const float TITLE_LABEL_HEIGHT = 22f;
        private const float ITEMS_LABEL_HEIGHT = 22f;
        private const float NOTE_LABEL_HEIGHT = 18f;

        private const float ROW_HEIGHT = TITLE_LABEL_HEIGHT + ITEMS_LABEL_HEIGHT + NOTE_LABEL_HEIGHT + DEFAULT_SPACING;

        private const float BUTTON_WIDTH = 80f;
        private const float BUTTON_HEIGHT = 30f;

        /// <summary>
        /// How far down the list is scrolled. Unity changes this for us as the player scrolls.
        /// </summary>
        private Vector2 scrollPosition = Vector2.zero;

        /// <summary>
        /// Draws the board. Called every frame while the market tab is open.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        public void Draw(Rect inRect)
        {
            Listing[] listings = Client.Instance.GetListings();

            // Draw a placeholder if the board is empty
            if (listings.Length == 0)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_market_noListingsPlaceholder".Translate());
                return;
            }

            // The view is the full height of every row, and the scroll view shows the part of it that fits in inRect
            Rect viewRect = new Rect(0f, 0f, inRect.width - SCROLLBAR_WIDTH, ROW_HEIGHT * listings.Length);

            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            for (int i = 0; i < listings.Length; i++)
            {
                Rect rowRect = new Rect(0f, ROW_HEIGHT * i, viewRect.width, ROW_HEIGHT);

                // Shade every other row so they're easier to tell apart
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);

                drawRow(rowRect, listings[i]);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws one listing: who posted it, what's in it, their note, and a button.
        /// </summary>
        /// <param name="rowRect">Container to draw within</param>
        /// <param name="listing">Listing to draw</param>
        private void drawRow(Rect rowRect, Listing listing)
        {
            Rect buttonRect = new Rect(rowRect.xMax - BUTTON_WIDTH, rowRect.center.y - BUTTON_HEIGHT / 2, BUTTON_WIDTH, BUTTON_HEIGHT);
            float textWidth = rowRect.width - BUTTON_WIDTH - DEFAULT_SPACING;
            Rect titleRect = new Rect(rowRect.xMin, rowRect.yMin + DEFAULT_SPACING / 2, textWidth, TITLE_LABEL_HEIGHT);
            Rect itemsRect = new Rect(rowRect.xMin, titleRect.yMax, textWidth, ITEMS_LABEL_HEIGHT);
            Rect noteRect = new Rect(rowRect.xMin, itemsRect.yMax, textWidth, NOTE_LABEL_HEIGHT);

            // Look up the poster's name, falling back to "???" if we don't know them
            if (!Client.Instance.TryGetDisplayName(listing.PosterUuid, out string posterName)) posterName = "???";
            posterName = TextHelper.StripRichText(posterName);

            // Save the current text settings so we can put them back afterwards
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;

            // "Offering · Bob" or "Wanted · Bob"
            Text.Font = GameFont.Small;
            string typeLabel = (listing.Type == ListingType.Offering ? "Phinix_market_offering" : "Phinix_market_wanted").Translate();
            Widgets.Label(titleRect, typeLabel + " · " + posterName);

            // "50x Steel, 1x Assault rifle"
            Widgets.Label(itemsRect, describeItems(listing));

            // Note and how long ago it was posted, in small grey text
            Text.Font = GameFont.Tiny;
            UnityEngine.GUI.color = Color.gray;
            string note = string.IsNullOrEmpty(listing.Note) ? "" : "\"" + listing.Note + "\"  ";
            Widgets.Label(noteRect, note + timeAgo(listing.PostedTime));
            UnityEngine.GUI.color = Color.white;

            Text.Font = previousFont;
            Text.Anchor = previousAnchor;
            
            if (listing.PosterUuid == Client.Instance.Uuid)
            {
                // Our listing
                if (Widgets.ButtonText(buttonRect, "Phinix_market_removeButton".Translate()))
                {
                    Client.Instance.RemoveListing(listing.ListingId);
                }
            }
            else
            {
                // Someone else's
                if (Widgets.ButtonText(buttonRect, "Phinix_market_tradeButton".Translate()))
                {
                    Client.Instance.CreateTrade(listing.PosterUuid);
                }
            }
        }

        /// <summary>
        /// Turns a listing's items into one line of text, like "50x Steel, 1x Assault rifle".
        /// </summary>
        /// <param name="listing">Listing to describe</param>
        /// <returns>Item summary</returns>
        private static string describeItems(Listing listing)
        {
            return string.Join(", ", listing.Items.Select(item => item.StackCount + "x " + itemLabel(item)));
        }

        /// <summary>
        /// Gets the in-game name of an item, or its def name if this game doesn't have it (e.g. it's from a mod we
        /// don't have installed).
        /// </summary>
        /// <param name="item">Item to name</param>
        /// <returns>Item's name</returns>
        private static string itemLabel(ProtoThing item)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
            return def != null ? def.LabelCap.ToString() : item.DefName;
        }

        /// <summary>
        /// Turns a Unix timestamp into text like "5 minutes ago".
        /// </summary>
        /// <param name="unixSeconds">Seconds since 1970, as set by the server</param>
        /// <returns>How long ago that was</returns>
        private static string timeAgo(long unixSeconds)
        {
            TimeSpan age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

            if (age.TotalMinutes < 1) return "Phinix_market_justNow".Translate();
            if (age.TotalHours < 1) return "Phinix_market_minutesAgo".Translate((int) age.TotalMinutes);
            if (age.TotalDays < 1) return "Phinix_market_hoursAgo".Translate((int) age.TotalHours);
            return "Phinix_market_daysAgo".Translate((int) age.TotalDays);
        }
    }
}
