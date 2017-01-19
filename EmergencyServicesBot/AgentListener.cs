namespace EmergencyServicesBot
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Microsoft.Bot.Connector;

    public class AgentListener
    {
        // Note: Of course you don't want these here. Eventually you will need to save these in some table
        // Having them here as static variables means we can only remember one user :)
        public static string FromId;
        public static string FromName;
        public static string ToId;
        public static string ToName;
        public static string ServiceUrl;
        public static string ChannelId;
        public static string ConversationId;

        // This will send an adhoc message to the user
        public static async Task Resume(string msg)
        {
            try
            {
                var userAccount = new ChannelAccount(ToId, ToName);
                var botAccount = new ChannelAccount(FromId, FromName);
                var connector = new ConnectorClient(new Uri(ServiceUrl));

                IMessageActivity message = Activity.CreateMessageActivity();
                if (!string.IsNullOrEmpty(ConversationId) && !string.IsNullOrEmpty(ChannelId))
                {
                    message.ChannelId = ChannelId;
                }
                else
                {
                    ConversationId = (await connector.Conversations.CreateDirectConversationAsync(botAccount, userAccount)).Id;
                }

                message.From = botAccount;
                message.Recipient = userAccount;
                message.Conversation = new ConversationAccount(id: ConversationId);
                message.Text = msg;
                message.Locale = "en-Us";
                await connector.Conversations.SendToConversationAsync((Activity)message);
            }
            catch (Exception exp)
            {
                Debug.WriteLine(exp);
            }
        }
    }
}