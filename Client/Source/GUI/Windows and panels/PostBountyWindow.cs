using System.Collections.Generic;
using System.Linq;
using PhinixClient.GUI;
using RimWorld;
using Trading;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    /// <summary>
    /// Pop-up window for putting up a new bounty: what you want on the left, what you'll pay on the right.
    /// </summary>
    public class PostBountyWindow : Window
    {
        private const float SPACING = 10f;
        private const float ROW_HEIGHT = 30f;
        private const float TITLE_HEIGHT = 35f;
        private const float POST_BUTTON_WIDTH = 140f;

        public override Vector2 InitialSize => new Vector2(900f, 680f);

        /// <summary>
        /// Left column: what the poster wants. Any item, with a minimum quality.
        /// </summary>
        private readonly ItemPicker wantedPicker = new ItemPicker(ownedOnly: false, pickMinQuality: true);

        /// <summary>
        /// Right column: the reward. Only items the colony has, capped at how many it has.
        /// </summary>
        private readonly ItemPicker rewardPicker = new ItemPicker(ownedOnly: true, pickMinQuality: false);

        private string note = "";

        public PostBountyWindow()
        {
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;   // Enter shouldn't close it while typing, same as the listing window
            draggable = true;

            // Both pickers use the same item limit the server enforces
            wantedPicker.MaxEntries = Agora.ServerAgora.MAX_ITEMS_PER_BOUNTY;
            rewardPicker.MaxEntries = Agora.ServerAgora.MAX_ITEMS_PER_BOUNTY;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, TITLE_HEIGHT), "Phinix_bounty_postTitle".Translate());
            Text.Font = GameFont.Small;

            // Bottom strip: note box on the left, Post button on the right
            Rect bottomRect = inRect.BottomPartPixels(ROW_HEIGHT);
            Rect postButtonRect = bottomRect.RightPartPixels(POST_BUTTON_WIDTH);
            Rect noteRect = new Rect(bottomRect.x, bottomRect.y, bottomRect.width - POST_BUTTON_WIDTH - SPACING, ROW_HEIGHT);
            Rect noteLabelRect = new Rect(inRect.x, bottomRect.y - ROW_HEIGHT, inRect.width, ROW_HEIGHT);

            // The two pickers share the middle, split down the centre
            float top = inRect.y + TITLE_HEIGHT + SPACING;
            float columnWidth = (inRect.width - SPACING) / 2;
            float columnHeight = noteLabelRect.y - SPACING - top;
            Rect wantedRect = new Rect(inRect.x, top, columnWidth, columnHeight);
            Rect rewardRect = new Rect(inRect.x + columnWidth + SPACING, top, columnWidth, columnHeight);

            drawColumn(wantedRect, "Phinix_bounty_wantLabel".Translate(), wantedPicker);
            drawColumn(rewardRect, "Phinix_bounty_payLabel".Translate(), rewardPicker);

            Widgets.Label(noteLabelRect, "Phinix_market_noteLabel".Translate());
            note = Widgets.TextField(noteRect, note);
            if (note.Length > Agora.ServerAgora.MAX_NOTE_LENGTH) note = note.Substring(0, Agora.ServerAgora.MAX_NOTE_LENGTH);

            // Only clickable once both sides have something in them
            bool canPost = wantedPicker.Entries.Count > 0 && rewardPicker.Entries.Count > 0;
            if (Widgets.ButtonText(postButtonRect, "Phinix_bounty_postButton".Translate(), active: canPost) && canPost)
            {
                post();
            }
        }

        /// <summary>
        /// Draws a heading with a picker under it.
        /// </summary>
        private static void drawColumn(Rect inRect, string heading, ItemPicker picker)
        {
            Widgets.Label(inRect.TopPartPixels(ROW_HEIGHT), heading);
            picker.Draw(inRect.BottomPartPixels(inRect.height - ROW_HEIGHT));
        }

        /// <summary>
        /// Takes the reward off the map, sends the bounty to the server and closes the window.
        /// </summary>
        private void post()
        {
            // The reward has to physically leave the colony now, so it can't also be spent elsewhere.
            // Any quality is fine for the reward, since the poster picked the items themselves
            IEnumerable<BountyEscrow.Request> rewardRequests = rewardPicker.Entries.Select(entry => new BountyEscrow.Request
            {
                Def = entry.Def,
                Count = entry.Count,
                MinQuality = Quality.Awful
            });

            if (!BountyEscrow.TryTake(rewardRequests, out string token, out List<ProtoThing> rewardItems))
            {
                // Something changed since the window opened (e.g. a pawn ate the reward). Nothing was taken
                Messages.Message("Phinix_bounty_notEnoughReward".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Wanted items are just a description, not real things: a def name, an amount and the lowest quality.
            // Items that can't have a quality get Quality.None, which the server treats as "doesn't matter"
            IEnumerable<ProtoThing> wantedItems = wantedPicker.Entries.Select(entry => new ProtoThing
            {
                DefName = entry.Def.defName,
                StackCount = entry.Count,
                Quality = entry.Def.HasComp(typeof(CompQuality)) ? entry.MinQuality : Quality.None
            });

            // The server answers with the same token. Client.cs passes that answer to BountyEscrow.Resolve, which
            // either destroys the held reward (posted) or puts it back (refused)
            Client.Instance.PostBounty(token, wantedItems, rewardItems, note);
            Close();
        }
    }
}
