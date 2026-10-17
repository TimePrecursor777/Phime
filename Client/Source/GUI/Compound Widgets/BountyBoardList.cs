using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Trading;
using UnityEngine;
using Utils;
using Verse;
using Bounty = Agora.Bounty;

namespace PhinixClient.GUI
{
    /// <summary>
    /// Scrollable list of every bounty, each with a Fulfil button (other people's) or a Cancel button (ours).
    /// </summary>
    public class BountyBoardList
    {
        private const float SCROLLBAR_WIDTH = 16f;
        private const float SPACING = 10f;
        private const float LINE_HEIGHT = 22f;
        private const float SMALL_LINE_HEIGHT = 18f;
        private const float ROW_HEIGHT = LINE_HEIGHT * 3 + SMALL_LINE_HEIGHT + SPACING;
        private const float BUTTON_WIDTH = 80f;
        private const float BUTTON_HEIGHT = 30f;

        /// <summary>
        /// How often to re-check which bounties we have the items for, in seconds.
        /// </summary>
        /// <remarks>
        /// Checking means counting items across the whole colony, which is too slow to do 60 times a second for
        /// every row. Once every couple of seconds is plenty, since stockpiles don't change that fast.
        /// </remarks>
        private const float FULFIL_CHECK_INTERVAL = 2f;

        private Vector2 scrollPosition = Vector2.zero;

        /// <summary>
        /// Which bounties we can fulfil right now, by bounty ID. Rebuilt every <see cref="FULFIL_CHECK_INTERVAL"/>.
        /// </summary>
        private Dictionary<string, bool> canFulfilCache = new Dictionary<string, bool>();
        private float lastFulfilCheck = -999f;

        /// <summary>
        /// Draws the board. Called every frame while the bounties tab is open.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        public void Draw(Rect inRect)
        {
            Bounty[] bounties = Client.Instance.GetBounties();

            if (bounties.Length == 0)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_bounty_noBountiesPlaceholder".Translate());
                return;
            }

            // Time.realtimeSinceStartup is seconds since the game launched, and keeps counting while paused
            if (Time.realtimeSinceStartup - lastFulfilCheck > FULFIL_CHECK_INTERVAL)
            {
                lastFulfilCheck = Time.realtimeSinceStartup;
                canFulfilCache = bounties.ToDictionary(bounty => bounty.BountyId, bounty => BountyEscrow.CanTake(RequestsFor(bounty)));
            }

            Rect viewRect = new Rect(0f, 0f, inRect.width - SCROLLBAR_WIDTH, ROW_HEIGHT * bounties.Length);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            for (int i = 0; i < bounties.Length; i++)
            {
                Rect rowRect = new Rect(0f, ROW_HEIGHT * i, viewRect.width, ROW_HEIGHT);
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);
                drawRow(rowRect, bounties[i]);
            }
            Widgets.EndScrollView();
        }

        /// <summary>
        /// Draws one bounty: who posted it, what it wants, what it pays, the note, time left and a button.
        /// </summary>
        private void drawRow(Rect rowRect, Bounty bounty)
        {
            Rect buttonRect = new Rect(rowRect.xMax - BUTTON_WIDTH, rowRect.center.y - BUTTON_HEIGHT / 2, BUTTON_WIDTH, BUTTON_HEIGHT);
            float textWidth = rowRect.width - BUTTON_WIDTH - SPACING;
            float y = rowRect.y + SPACING / 2;

            if (!Client.Instance.TryGetDisplayName(bounty.PosterUuid, out string posterName)) posterName = "???";
            posterName = TextHelper.StripRichText(posterName);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;

            // "Bounty · SkyHawk"
            Widgets.Label(new Rect(rowRect.x, y, textWidth, LINE_HEIGHT), "Phinix_bounty_titleLabel".Translate(posterName));
            y += LINE_HEIGHT;

            // "Wants: 200x Medicine, 1x Assault rifle (Good+)"
            Widgets.Label(new Rect(rowRect.x, y, textWidth, LINE_HEIGHT), "Phinix_bounty_wantsLabel".Translate(DescribeItems(bounty.WantedItems, true)));
            y += LINE_HEIGHT;

            // "Reward: 10x Gold, 1x Revolver (Excellent)"
            Widgets.Label(new Rect(rowRect.x, y, textWidth, LINE_HEIGHT), "Phinix_bounty_rewardLabel".Translate(DescribeItems(bounty.RewardItems, false)));
            y += LINE_HEIGHT;

            // Note and time left, small and grey
            Text.Font = GameFont.Tiny;
            UnityEngine.GUI.color = Color.gray;
            string note = string.IsNullOrEmpty(bounty.Note) ? "" : "\"" + bounty.Note + "\"  ";
            Widgets.Label(new Rect(rowRect.x, y, textWidth, SMALL_LINE_HEIGHT), note + timeLeft(bounty.ExpiryTime));
            UnityEngine.GUI.color = Color.white;

            Text.Font = previousFont;
            Text.Anchor = previousAnchor;

            if (bounty.PosterUuid == Client.Instance.Uuid)
            {
                // Ours: cancelling gets the reward back
                if (Widgets.ButtonText(buttonRect, "Phinix_bounty_cancelButton".Translate()))
                {
                    Client.Instance.CancelBounty(bounty.BountyId);
                }
            }
            else
            {
                // Someone else's: only clickable if we have everything it wants
                canFulfilCache.TryGetValue(bounty.BountyId, out bool canFulfil);
                if (Widgets.ButtonText(buttonRect, "Phinix_bounty_fulfilButton".Translate(), active: canFulfil) && canFulfil)
                {
                    confirmFulfil(bounty, posterName);
                }

                // Hovering a greyed-out button explains why
                if (!canFulfil) TooltipHandler.TipRegion(buttonRect, "Phinix_bounty_cantFulfilTip".Translate());
            }
        }

        /// <summary>
        /// Asks "are you sure?" before handing anything over, since the items leave the colony straight away.
        /// </summary>
        private static void confirmFulfil(Bounty bounty, string posterName)
        {
            string text = "Phinix_bounty_confirmFulfil".Translate(posterName, DescribeItems(bounty.WantedItems, true), DescribeItems(bounty.RewardItems, false));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => fulfil(bounty)));
        }

        /// <summary>
        /// Takes the wanted items off the map and hands them to the server.
        /// </summary>
        private static void fulfil(Bounty bounty)
        {
            if (!BountyEscrow.TryTake(RequestsFor(bounty), out string token, out List<ProtoThing> items))
            {
                // The items went missing between the button and the confirmation. Nothing was taken
                Messages.Message("Phinix_bounty_notEnoughItems".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            // Client.cs passes the server's answer to BountyEscrow.Resolve, which destroys the held items if it
            // worked or puts them back if it didn't (e.g. someone else got there first)
            Client.Instance.FulfilBounty(token, bounty.BountyId, items);
        }

        /// <summary>
        /// Turns a bounty's wanted items into requests for <see cref="BountyEscrow"/>.
        /// Wanted items whose def this game doesn't have (e.g. from a mod we don't have) can't be fulfilled.
        /// </summary>
        public static IEnumerable<BountyEscrow.Request> RequestsFor(Bounty bounty)
        {
            foreach (ProtoThing wanted in bounty.WantedItems)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(wanted.DefName);

                // A def of null with a count above zero can never be covered, so CanTake will say no
                yield return new BountyEscrow.Request { Def = def, Count = def == null ? int.MaxValue : wanted.StackCount, MinQuality = wanted.Quality };
            }
        }

        /// <summary>
        /// Turns items into one line like "200x Medicine, 1x Assault rifle (Good+)".
        /// </summary>
        /// <param name="items">Items to describe</param>
        /// <param name="isMinimum">True for wanted items (quality is a minimum, shown as "Good+"), false for real items</param>
        public static string DescribeItems(IEnumerable<ProtoThing> items, bool isMinimum)
        {
            return string.Join(", ", items.Select(item =>
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
                string label = item.StackCount + "x " + (def != null ? def.LabelCap.ToString() : item.DefName);

                // Wanted items only mention quality when it's above "any"; real items mention whatever they have
                if (isMinimum && item.Quality != Quality.Awful && item.Quality != Quality.None)
                {
                    label += " (" + ItemPicker.QualityLabel(item.Quality) + ")";
                }
                else if (!isMinimum && item.Quality != Quality.None)
                {
                    label += " (" + ((QualityCategory) item.Quality).GetLabel().CapitalizeFirst() + ")";
                }
                return label;
            }));
        }

        /// <summary>
        /// Turns an expiry time into text like "3 d left".
        /// </summary>
        private static string timeLeft(long expiryUnixSeconds)
        {
            TimeSpan left = DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds) - DateTimeOffset.UtcNow;

            if (left.TotalMinutes < 1) return "Phinix_bounty_expiringNow".Translate();
            if (left.TotalHours < 1) return "Phinix_bounty_minutesLeft".Translate((int) left.TotalMinutes);
            if (left.TotalDays < 1) return "Phinix_bounty_hoursLeft".Translate((int) left.TotalHours);
            return "Phinix_bounty_daysLeft".Translate((int) left.TotalDays);
        }
    }
}
