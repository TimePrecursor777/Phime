using System.Collections.Generic;
using System.Linq;
using Trading;
using Utils;
using PhinixClient.GUI;
using RimWorld;
using UnityEngine;
using Verse;
using static PhinixClient.Client;

namespace PhinixClient
{
    public class ServerTab : MainTabWindow
    {
        private const float DEFAULT_SPACING = 10f;
        private const float COLUMN_SPACING = 20f;
        private const float SCROLLBAR_WIDTH = 16f;

        private const float CHAT_TEXTBOX_HEIGHT = 30f;

        private const float CHAT_SEND_BUTTON_HEIGHT = 30f;
        private const float CHAT_SEND_BUTTON_WIDTH = 80f;

        private const float SETTINGS_BUTTON_HEIGHT = 30f;
        private const float SETTINGS_BUTTON_WIDTH = 210f;

        private const float USER_SEARCH_HEIGHT = 30f;
        private const float USER_SEARCH_WIDTH = 210f;

        private const float RIGHT_COLUMN_WIDTH = 210f;

        private const float POST_LISTING_BUTTON_HEIGHT = 30f;
        private const float POST_LISTING_BUTTON_WIDTH = 160f;

        // TODO: Add some kind of option to resize chat tab. Maybe a draggable corner?
        public override Vector2 InitialSize => new Vector2(1000f, 680f);

        private static string message = "";
        private static string userSearch = "";

        private readonly List<TabRecord> tabList = new List<TabRecord>();
        private int activeTab = 0;

        private readonly ChatMessageList chatMessageList = new ChatMessageList();
        private readonly UserList userList = new UserList();
        private readonly TradeList tradeList = new TradeList();
        private readonly MarketBoardList marketBoardList = new MarketBoardList();
        private readonly BountyBoardList bountyBoardList = new BountyBoardList();

        private bool chatMessageFieldFocused = false;

        public ServerTab()
        {
            // Populate the tab list
            tabList.Add(new TabRecord("Phinix_tabs_chat".Translate(), () => activeTab = 0, () => activeTab == 0));
            tabList.Add(new TabRecord("Phinix_tabs_trades".Translate(), () => activeTab = 1, () => activeTab == 1));
            tabList.Add(new TabRecord("Phinix_tabs_market".Translate(), () => activeTab = 2, () => activeTab == 2));
            tabList.Add(new TabRecord("Phinix_tabs_bounties".Translate(), () => activeTab = 3, () => activeTab == 3));
        }

        ///<inheritdoc/>
        /// <summary>
        /// Overrides the default accept key behaviour and instead sends a message.
        /// </summary>
        public override void OnAcceptKeyPressed()
        {
            sendChatMessage();
        }

        /// <summary>
        /// Extends the default window drawing behaviour by drawing the Phinix chat interface.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        public override void DoWindowContents(Rect inRect)
        {
            Rect usableRect = inRect.BottomPartPixels(inRect.height - TabDrawer.TabHeight);
            Rect rightColumnRect = usableRect.RightPartPixels(RIGHT_COLUMN_WIDTH);
            Rect chatRect = usableRect.LeftPartPixels(usableRect.width - (rightColumnRect.width + DEFAULT_SPACING));

            // Tabs, with a "New" count on any that have something the player hasn't seen
            tabList[0].label = tabLabel("Phinix_tabs_chat", activeTab == 0 ? 0 : Instance.NewChatCount);
            tabList[1].label = tabLabel("Phinix_tabs_trades", activeTab == 1 ? 0 : Instance.NewTradeCount);
            tabList[2].label = tabLabel("Phinix_tabs_market", activeTab == 2 ? 0 : Instance.NewListingCount);
            tabList[3].label = tabLabel("Phinix_tabs_bounties", activeTab == 3 ? 0 : Instance.NewBountyCount);
            TabDrawer.DrawTabs(usableRect, tabList, 200f);

            switch (activeTab)
            {
                case 0: // Chat tab
                    GenerateChat(chatRect); // Chat
                    GenerateRightColumn(rightColumnRect); // Right column
                    break;
                case 1: // Trades tab
                    GenerateTrades(usableRect); // Trades
                    if (Instance.Online) Instance.MarkTradesSeen();
                    break;
                case 2: // Market tab
                    GenerateMarket(usableRect); // Market board
                    if (Instance.Online) Instance.MarkListingsSeen();
                    break;
                case 3: // Bounties tab
                    GenerateBounties(usableRect); // Bounty board
                    if (Instance.Online) Instance.MarkBountiesSeen();
                    break;
                default:
                    Widgets.DrawMenuSection(usableRect); // Placeholder
                    break;
            }
        }

        /// <summary>
        /// Builds a tab's label, adding "New (n)" when there's something unseen. Counts above 99 show as "99+".
        /// </summary>
        /// <param name="key">Translation key of the tab's name</param>
        /// <param name="newCount">Number of unseen things</param>
        /// <returns>Label to show on the tab</returns>
        private static string tabLabel(string key, int newCount)
        {
            if (newCount <= 0 || !Instance.Online) return key.Translate();

            string countText = newCount > 99 ? "99+" : newCount.ToString();
            return "Phinix_tabs_newCount".Translate(key.Translate(), countText);
        }

        /// <summary>
        /// Draws a column containing a settings button, user search box, and a user list.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void GenerateRightColumn(Rect inRect)
        {
            Rect settingsButtonRect = inRect.TopPartPixels(SETTINGS_BUTTON_HEIGHT);
            Rect userSearchRect = new Rect(inRect.x, settingsButtonRect.yMax + DEFAULT_SPACING, inRect.width, USER_SEARCH_HEIGHT);
            Rect userListRect = new Rect(inRect.x, userSearchRect.yMax + DEFAULT_SPACING, inRect.width, inRect.yMax - (userSearchRect.yMax + DEFAULT_SPACING));

            // Settings button
            if (Widgets.ButtonText(settingsButtonRect, "Phinix_chat_settingsButton".Translate()))
            {
                Find.WindowStack.Add(new SettingsWindow());
            }

            // User search box
            string userSearchOld = userSearch;
            userSearch = Widgets.TextField(userSearchRect, userSearch);
            if (!userSearch.Equals(userSearchOld))
            {
                userList.Filter(userSearch);
            }

            // User list
            if (Instance.Online)
            {
                userList.Draw(userListRect);
            }
            else
            {
                Widgets.DrawMenuSection(userListRect);
            }
        }

        /// <summary>
        /// Generates the chat window consisting of a message list, message entry box, and a send button.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void GenerateChat(Rect inRect)
        {
            Rect sendButtonRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).RightPartPixels(CHAT_SEND_BUTTON_WIDTH);
            Rect messageBoxRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).LeftPartPixels(inRect.width - (CHAT_SEND_BUTTON_WIDTH + DEFAULT_SPACING));
            Rect chatRect = inRect.TopPartPixels(inRect.height - (messageBoxRect.height + DEFAULT_SPACING));

            // Chat message area
            if (Instance.Online)
            {
                chatMessageList.Draw(chatRect);
            }
            else
            {
                Widgets.DrawMenuSection(chatRect);
                Widgets.NoneLabelCenteredVertically(chatRect, "Phinix_chat_pleaseLogInPlaceholder".Translate());
            }

            // Message entry field
            UnityEngine.GUI.SetNextControlName("Phinix_chatMessageField");
            message = Widgets.TextField(messageBoxRect, message);

            if (Client.Instance.Settings.ForceMessageFieldFocus)
            {
                // Aggressively hold the focus on the chat field until clicked out of
                if (Input.GetMouseButtonDown(0))
                {
                    chatMessageFieldFocused = Mouse.IsOver(messageBoxRect);
                }
                if (chatMessageFieldFocused) UnityEngine.GUI.FocusControl("Phinix_chatMessageField");
            }

            // Send button
            if (Widgets.ButtonText(sendButtonRect, "Phinix_chat_sendButton".Translate()))
            {
                sendChatMessage();
            }
        }

        /// <summary>
        /// Generates the active trade list.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void GenerateTrades(Rect inRect)
        {
            if (Instance.Online)
            {
                tradeList.Draw(inRect);
            }
            else
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_chat_pleaseLogInPlaceholder".Translate());
            }
        }

        /// <summary>
        /// Generates the market board, with a button above it for posting a new listing.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void GenerateMarket(Rect inRect)
        {
            if (!Instance.Online)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_chat_pleaseLogInPlaceholder".Translate());
                return;
            }

            Rect postButtonRect = inRect.TopPartPixels(POST_LISTING_BUTTON_HEIGHT).RightPartPixels(POST_LISTING_BUTTON_WIDTH);
            Rect boardRect = inRect.BottomPartPixels(inRect.height - (POST_LISTING_BUTTON_HEIGHT + DEFAULT_SPACING));

            // Post listing button
            if (Widgets.ButtonText(postButtonRect, "Phinix_market_newListingButton".Translate()))
            {
                Find.WindowStack.Add(new PostListingWindow());
            }

            // Open trades button, just left of the post button. Greyed out when there aren't any
            Rect tradesButtonRect = new Rect(postButtonRect.xMin - DEFAULT_SPACING - POST_LISTING_BUTTON_WIDTH, postButtonRect.yMin, POST_LISTING_BUTTON_WIDTH, POST_LISTING_BUTTON_HEIGHT);
            ImmutableTrade[] trades = Instance.Settings.ShowBlockedTrades ? Instance.GetTrades() : Instance.GetTradesExceptWith(Instance.Settings.BlockedUsers);
            if (Widgets.ButtonText(tradesButtonRect, "Phinix_market_openTradesButton".Translate(trades.Length), active: trades.Length > 0) && trades.Length > 0)
            {
                openTradeOrPick(trades);
            }

            // Board
            marketBoardList.Draw(boardRect);
        }

        /// <summary>
        /// Generates the bounty board, with a button above it for posting a new bounty.
        /// </summary>
        /// <param name="inRect">Container to draw within</param>
        private void GenerateBounties(Rect inRect)
        {
            if (!Instance.Online)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_chat_pleaseLogInPlaceholder".Translate());
                return;
            }

            Rect postButtonRect = inRect.TopPartPixels(POST_LISTING_BUTTON_HEIGHT).RightPartPixels(POST_LISTING_BUTTON_WIDTH);
            Rect boardRect = inRect.BottomPartPixels(inRect.height - (POST_LISTING_BUTTON_HEIGHT + DEFAULT_SPACING));

            if (Widgets.ButtonText(postButtonRect, "Phinix_bounty_newBountyButton".Translate()))
            {
                Find.WindowStack.Add(new PostBountyWindow());
            }

            bountyBoardList.Draw(boardRect);
        }

        /// <summary>
        /// Opens the trade window straight away if there's only one trade, otherwise shows a menu to pick which one.
        /// </summary>
        /// <param name="trades">Active trades</param>
        private void openTradeOrPick(ImmutableTrade[] trades)
        {
            if (trades.Length == 1)
            {
                Find.WindowStack.Add(new TradeWindow(trades[0]));
                return;
            }

            // A FloatMenu is the little pop-up list RimWorld shows when you right-click something
            List<FloatMenuOption> options = trades
                .Select(trade => new FloatMenuOption(
                    "Phinix_trade_activeTrade_tradeWithLabel".Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName)),
                    () => Find.WindowStack.Add(new TradeWindow(trade))
                ))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>
        /// Sends <see cref="message"/> to the server.
        /// </summary>
        private void sendChatMessage()
        {
            if (!string.IsNullOrEmpty(message) && Instance.Online)
            {
                // TODO: Make chat message 'sent' callback to remove message, preventing removal of lengthy messages for nothing and causing frustration
                Instance.SendMessage(message);
                chatMessageList.ScrollToBottom();

                message = "";
            }
        }
    }
}
