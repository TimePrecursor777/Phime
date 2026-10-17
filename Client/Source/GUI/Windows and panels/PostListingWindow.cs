using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Trading;
using UnityEngine;
using Verse;
using ListingType = Agora.ListingType;

namespace PhinixClient
{
    /// <summary>
    /// Pop-up window for putting a new listing on the market board.
    /// The player picks Offering or Wanted, searches for items to add, sets how many of each, and writes a note.
    /// </summary>
    public class PostListingWindow : Window
    {
        private const float DEFAULT_SPACING = 10f;
        private const float ROW_HEIGHT = 30f;
        private const float SCROLLBAR_WIDTH = 16f;

        private const float COUNT_FIELD_WIDTH = 70f;
        private const float SMALL_BUTTON_WIDTH = 60f;

        private const float SEARCH_RESULTS_HEIGHT = 180f;

        /// <summary>
        /// Most items a listing can hold, to keep the board readable.
        /// </summary>
        private const int MAX_ITEMS = 10;

        /// <summary>
        /// Most search results to show at once.
        /// </summary>
        private const int MAX_SEARCH_RESULTS = 50;

        public override Vector2 InitialSize => new Vector2(500f, 640f);

        /// <summary>
        /// One item the player has added, with how many they want.
        /// </summary>
        /// <remarks>
        /// A small class used like a Python dataclass. <c>countBuffer</c> holds the text in the number box while
        /// the player is typing, which RimWorld's number field needs.
        /// </remarks>
        private class ChosenItem
        {
            public ThingDef Def;
            public int Count = 1;
            public string CountBuffer = "1";
        }

        /// <summary>
        /// Every item def that can be put on the board, sorted by name. Built once, the first time it's needed.
        /// </summary>
        private static List<ThingDef> allItemDefs;

        /// <summary>
        /// How many of each item the colony has, used to limit what can be offered.
        /// Counted once when the window opens.
        /// </summary>
        /// <remarks>
        /// A Dictionary is C#'s dict. This one maps an item def to a count, like <c>{steel: 450, medicine: 12}</c>.
        /// </remarks>
        private readonly Dictionary<ThingDef, int> ownedCounts = new Dictionary<ThingDef, int>();

        private ListingType type = ListingType.Offering;
        private string note = "";
        private string search = "";
        private readonly List<ChosenItem> chosenItems = new List<ChosenItem>();

        private Vector2 searchScrollPosition = Vector2.zero;
        private Vector2 chosenScrollPosition = Vector2.zero;

        public PostListingWindow()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            // Enter shouldn't close the window, since people press it while typing in the boxes.
            // Escape or the X still close it, and the Post button sends it
            closeOnAccept = false;

            // Let the player drag the window around by its edges, like RimWorld's own pop-ups
            draggable = true;

            if (allItemDefs == null)
            {
                // Items you can carry and trade. Corpses, unfinished items, and things like minified buildings are left out
                allItemDefs = DefDatabase<ThingDef>.AllDefs
                    .Where(def => def.category == ThingCategory.Item)
                    .Where(def => def.tradeability != Tradeability.None)
                    .Where(def => !def.IsCorpse && !def.isUnfinishedThing && def.minifiedDef == null)
                    .OrderBy(def => def.label)
                    .ToList();
            }

            countOwnedItems();
        }

        /// <summary>
        /// Fills <see cref="ownedCounts"/> with what the colony has, looking in the same places as the trade window
        /// so you can't advertise anything you couldn't actually trade.
        /// </summary>
        private void countOwnedItems()
        {
            IEnumerable<Map> homeMaps = Find.Maps.Where(map => map.IsPlayerHome);
            IEnumerable<Thing> things;
            if (Client.Instance.Settings.AllItemsTradable)
            {
                things = homeMaps.SelectMany(map => map.listerThings.AllThings);
            }
            else
            {
                // Only things sitting in stockpiles and shelves
                things = homeMaps
                    .SelectMany(map => map.haulDestinationManager.AllGroups)
                    .SelectMany(group => group.HeldThings);
            }

            foreach (Thing thing in things)
            {
                if (thing.def.category != ThingCategory.Item) continue;

                // Like Python's counts[def] = counts.get(def, 0) + n
                ownedCounts.TryGetValue(thing.def, out int count);
                ownedCounts[thing.def] = count + thing.stackCount;
            }
        }

        /// <summary>
        /// Most of an item that can go in this listing: what we own when offering, or anything when asking.
        /// </summary>
        /// <param name="def">Item to check</param>
        /// <returns>Largest allowed count</returns>
        private int maxCount(ThingDef def)
        {
            if (type == ListingType.Wanted) return 99999;
            return ownedCounts.TryGetValue(def, out int owned) ? owned : 0;
        }

        /// <summary>
        /// Switches between Offering and Wanted. Switching to Offering drops anything we don't own and lowers counts
        /// to what we have.
        /// </summary>
        /// <param name="newType">Type to switch to</param>
        private void setType(ListingType newType)
        {
            type = newType;
            if (type != ListingType.Offering) return;

            chosenItems.RemoveAll(chosen => maxCount(chosen.Def) == 0);
            foreach (ChosenItem chosen in chosenItems)
            {
                if (chosen.Count > maxCount(chosen.Def))
                {
                    chosen.Count = maxCount(chosen.Def);
                    chosen.CountBuffer = chosen.Count.ToString();
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Lay the window out from top to bottom, moving "y" down after each part
            float y = inRect.yMin;

            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.xMin, y, inRect.width, 35f), "Phinix_market_postTitle".Translate());
            Text.Font = GameFont.Small;
            y += 35f + DEFAULT_SPACING;

            // Offering / Wanted choice
            float halfWidth = (inRect.width - DEFAULT_SPACING) / 2;
            if (Widgets.RadioButtonLabeled(new Rect(inRect.xMin, y, halfWidth, ROW_HEIGHT), "Phinix_market_offering".Translate(), type == ListingType.Offering))
            {
                setType(ListingType.Offering);
            }
            if (Widgets.RadioButtonLabeled(new Rect(inRect.xMin + halfWidth + DEFAULT_SPACING, y, halfWidth, ROW_HEIGHT), "Phinix_market_wanted".Translate(), type == ListingType.Wanted))
            {
                setType(ListingType.Wanted);
            }
            y += ROW_HEIGHT + DEFAULT_SPACING;

            // Search box and results
            Widgets.Label(new Rect(inRect.xMin, y, inRect.width, ROW_HEIGHT), "Phinix_market_searchLabel".Translate());
            y += ROW_HEIGHT;
            search = Widgets.TextField(new Rect(inRect.xMin, y, inRect.width, ROW_HEIGHT), search);
            y += ROW_HEIGHT + DEFAULT_SPACING / 2;
            drawSearchResults(new Rect(inRect.xMin, y, inRect.width, SEARCH_RESULTS_HEIGHT));
            y += SEARCH_RESULTS_HEIGHT + DEFAULT_SPACING;

            // Note box, pinned just above the bottom buttons
            Rect postButtonRect = new Rect(inRect.xMax - 120f, inRect.yMax - ROW_HEIGHT, 120f, ROW_HEIGHT);
            Rect noteRect = new Rect(inRect.xMin, postButtonRect.yMin - DEFAULT_SPACING - ROW_HEIGHT, inRect.width, ROW_HEIGHT);
            Rect noteLabelRect = new Rect(inRect.xMin, noteRect.yMin - ROW_HEIGHT, inRect.width, ROW_HEIGHT);
            Widgets.Label(noteLabelRect, "Phinix_market_noteLabel".Translate());
            note = Widgets.TextField(noteRect, note);
            if (note.Length > Agora.ServerAgora.MAX_NOTE_LENGTH) note = note.Substring(0, Agora.ServerAgora.MAX_NOTE_LENGTH);

            // Chosen items fill whatever space is left in the middle
            Rect chosenRect = new Rect(inRect.xMin, y, inRect.width, noteLabelRect.yMin - DEFAULT_SPACING - y);
            drawChosenItems(chosenRect);

            // Post button, only clickable once something has been added
            bool canPost = chosenItems.Count > 0;
            if (Widgets.ButtonText(postButtonRect, "Phinix_market_postButton".Translate(), active: canPost) && canPost)
            {
                post();
            }
        }

        /// <summary>
        /// Draws the list of items matching the search, each with an Add button.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void drawSearchResults(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);

            // Don't list hundreds of items before the player has typed anything
            if (search.Length < 2)
            {
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_market_typeToSearch".Translate());
                return;
            }

            // When offering, only show things the colony actually has
            List<ThingDef> results = allItemDefs
                .Where(def => type == ListingType.Wanted || maxCount(def) > 0)
                .Where(def => def.label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(MAX_SEARCH_RESULTS)
                .ToList();

            Rect viewRect = new Rect(0f, 0f, inRect.width - SCROLLBAR_WIDTH, ROW_HEIGHT * results.Count);
            Widgets.BeginScrollView(inRect, ref searchScrollPosition, viewRect);
            for (int i = 0; i < results.Count; i++)
            {
                ThingDef def = results[i];
                Rect rowRect = new Rect(0f, ROW_HEIGHT * i, viewRect.width, ROW_HEIGHT);
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);

                // Icon, name, and Add button
                Widgets.ThingIcon(rowRect.LeftPartPixels(ROW_HEIGHT).ContractedBy(2f), def);
                Rect labelRect = new Rect(rowRect.xMin + ROW_HEIGHT + 5f, rowRect.yMin, rowRect.width - ROW_HEIGHT - SMALL_BUTTON_WIDTH - 10f, ROW_HEIGHT);
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = def.LabelCap;
                if (type == ListingType.Offering) label += " (" + "Phinix_market_owned".Translate(maxCount(def)) + ")";
                Widgets.Label(labelRect, label);
                Text.Anchor = TextAnchor.UpperLeft;

                bool canAdd = chosenItems.Count < MAX_ITEMS && chosenItems.All(chosen => chosen.Def != def);
                if (Widgets.ButtonText(rowRect.RightPartPixels(SMALL_BUTTON_WIDTH), "Phinix_market_addButton".Translate(), active: canAdd) && canAdd)
                {
                    chosenItems.Add(new ChosenItem { Def = def });
                }
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws the items the player has added, each with a number box and a remove button.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void drawChosenItems(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);

            if (chosenItems.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_market_nothingAdded".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, inRect.width - SCROLLBAR_WIDTH, ROW_HEIGHT * chosenItems.Count);
            Widgets.BeginScrollView(inRect, ref chosenScrollPosition, viewRect);

            // Remember which one to remove and do it after the loop, since changing a list while looping over it breaks
            ChosenItem toRemove = null;
            for (int i = 0; i < chosenItems.Count; i++)
            {
                ChosenItem chosen = chosenItems[i];
                Rect rowRect = new Rect(0f, ROW_HEIGHT * i, viewRect.width, ROW_HEIGHT);
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);

                Rect removeRect = rowRect.RightPartPixels(ROW_HEIGHT).ContractedBy(4f);
                Rect countRect = new Rect(removeRect.xMin - DEFAULT_SPACING - COUNT_FIELD_WIDTH, rowRect.yMin + 2f, COUNT_FIELD_WIDTH, ROW_HEIGHT - 4f);
                Rect labelRect = new Rect(rowRect.xMin + ROW_HEIGHT + 5f, rowRect.yMin, countRect.xMin - rowRect.xMin - ROW_HEIGHT - 10f, ROW_HEIGHT);

                Widgets.ThingIcon(rowRect.LeftPartPixels(ROW_HEIGHT).ContractedBy(2f), chosen.Def);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, chosen.Def.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft;

                // "ref" lets the number box change Count and CountBuffer directly, a bit like passing a mutable box in Python
                Widgets.TextFieldNumeric(countRect, ref chosen.Count, ref chosen.CountBuffer, 1, maxCount(chosen.Def));

                if (Widgets.ButtonImage(removeRect, TexButton.Delete)) toRemove = chosen;
            }
            Widgets.EndScrollView();

            if (toRemove != null) chosenItems.Remove(toRemove);
        }

        /// <summary>
        /// Sends the listing to the server and closes the window.
        /// </summary>
        private void post()
        {
            // Turn each chosen item into the ProtoThing the server understands
            IEnumerable<ProtoThing> items = chosenItems.Select(chosen => new ProtoThing
            {
                DefName = chosen.Def.defName,
                StackCount = chosen.Count
            });

            Client.Instance.PostListing(type, items, note);
            Close();
        }
    }
}
