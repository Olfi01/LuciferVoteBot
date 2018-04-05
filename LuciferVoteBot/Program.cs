using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineKeyboardButtons;
using Telegram.Bot.Types.ReplyMarkups;

namespace LuciferVoteBot
{
    public static class Program
    {
        private const string channelName = "@eowcinfo";
        private static long adminGroupId = -1001162835309;
        private static readonly List<string> languages = new List<string>()
        { "Normal", "Amnesia", "Pokémon", "Emoji", "SuperAmnesia", "Schreibfehler" };
        private static readonly List<string> modes = new List<string>()
        { "Nichts", "Secret lynch", "Kein Verraten der Rollen nach dem Tod", "Beides", "Random Mode" };
        private static TelegramBotClient client;
        private static string langMsgText = "";
        private static string modeMsgText = "";
        private static Dictionary<long, string> langVotes = new Dictionary<long, string>();
        private static Dictionary<long, string> modeVotes = new Dictionary<long, string>();
        private static int langMsgId = 0;
        private static int modeMsgId = 0;

        public static void Main(string[] args)
        {
            client = new TelegramBotClient(args[0]);
            client.OnUpdate += Client_OnUpdate;
            client.OnCallbackQuery += Client_OnCallbackQuery;
            client.StartReceiving();
            bool running = true;
            while (running)
            {
                var cmd = Console.ReadLine();
                switch (cmd)
                {
                    case "stop":
                    case "stopbot":
                        running = false;
                        client.StopReceiving();
                        break;
                    case "start":
                        var t = new Thread(() => client.StartReceiving());
                        t.Start();
                        break;
                }
            }
        }

        private static void Client_OnCallbackQuery(object sender, CallbackQueryEventArgs e)
        {
            var data = e.CallbackQuery.Data;
            if (data == "today" || data == "tomorrow") return;
            if (languages.Contains(data))
            {
                if (langVotes.ContainsKey(e.CallbackQuery.From.Id))
                {
                    if (data == langVotes[e.CallbackQuery.From.Id])
                    {
                        langVotes.Remove(e.CallbackQuery.From.Id);
                        client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Du hast deine Stimme zurückgezogen.");
                        RefreshLangMsg();
                        return;
                    }
                    langVotes[e.CallbackQuery.From.Id] = data;
                    client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Du hast für {data} abgestimmt.");
                    RefreshLangMsg();
                    return;
                }
                langVotes.Add(e.CallbackQuery.From.Id, data);
                client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Du hast für {data} abgestimmt.");
                RefreshLangMsg();
                return;
            }
            if (modeVotes.ContainsKey(e.CallbackQuery.From.Id))
            {
                if (data == modeVotes[e.CallbackQuery.From.Id])
                {
                    modeVotes.Remove(e.CallbackQuery.From.Id);
                    client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, "Du hast deine Stimme zurückgezogen.");
                    RefreshModeMsg();
                    return;
                }
                modeVotes[e.CallbackQuery.From.Id] = data;
                client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Du hast für {data} abgestimmt.");
                RefreshModeMsg();
                return;
            }
            modeVotes.Add(e.CallbackQuery.From.Id, data);
            client.AnswerCallbackQueryAsync(e.CallbackQuery.Id, $"Du hast für {data} abgestimmt.");
            RefreshModeMsg();
            return;
        }

        private static void Client_OnUpdate(object sender, UpdateEventArgs e)
        {
            if (e.Update.Type != UpdateType.MessageUpdate || e.Update.Message.Type != MessageType.TextMessage
                || e.Update.Message.Chat.Id != adminGroupId) return;
            var msg = e.Update.Message;
            if (msg.Entities.Count < 1 || msg.Entities[0].Type != MessageEntityType.BotCommand || msg.Entities[0].Offset != 0) return;
            var cmd = msg.EntityValues[0];
            if (cmd.Contains("@")) cmd = cmd.Remove(cmd.IndexOf("@"));
            switch (cmd)
            {
                case "/sendpoll":
                    var t = new Thread(() => SendPoll(msg));
                    t.Start();
                    break;
                case "/closepoll":
                    client.SendTextMessageAsync(msg.Chat.Id,
                        "Abstimmung geschlossen. Ergebnisse: \n\n\n" + GetCurrentLangPoll() + "\n\n\n" + GetCurrentModePoll());
                    ClosePoll();
                    langVotes.Clear();
                    modeVotes.Clear();
                    break;
            }
        }

        private static void SendPoll(Telegram.Bot.Types.Message msg)
        {
            ManualResetEvent mre = new ManualResetEvent(false);
            var todayTomorrow = new List<string>() { "today", "tomorrow" };
            bool today = false;
            EventHandler<CallbackQueryEventArgs> cHandler = (sender2, e2) =>
            {
                if (!todayTomorrow.Contains(e2.CallbackQuery.Data)) return;
                if (e2.CallbackQuery.Data == "today") today = true;
                mre.Set();
            };
            var t = client.SendTextMessageAsync(msg.Chat.Id, "Die Abstimmung für heute oder morgen?",
                replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[]
                {
                            new InlineKeyboardCallbackButton("Heute", "today"),
                            new InlineKeyboardCallbackButton("Morgen", "tomorrow")
                }));
            t.Wait();
            var sent = t.Result;
            try
            {
                client.OnCallbackQuery += cHandler;
                mre.WaitOne();
            }
            finally
            {
                client.OnCallbackQuery -= cHandler;
            }
            SendPoll(today ? DateTime.Today : DateTime.Today.AddDays(1));
            client.EditMessageTextAsync(sent.Chat.Id, sent.MessageId, "Abstimmung wurde gesendet.");
        }

        private static void RefreshLangMsg()
        {
            client.EditMessageTextAsync(channelName, langMsgId, langMsgText + "\n" + GetCurrentLangPoll(),
                replyMarkup: GetLangReplyMarkup(), parseMode: ParseMode.Markdown);
        }

        private static void RefreshModeMsg()
        {
            client.EditMessageTextAsync(channelName, modeMsgId, modeMsgText + "\n" + GetCurrentModePoll(),
                replyMarkup: GetModeReplyMarkup(), parseMode: ParseMode.Markdown);
        }

        private static void DeletePoll()
        {
            client.DeleteMessageAsync(channelName, langMsgId);
            client.DeleteMessageAsync(channelName, modeMsgId);
        }

        private static void ClosePoll()
        {
            client.EditMessageReplyMarkupAsync(channelName, langMsgId);
            client.EditMessageReplyMarkupAsync(channelName, modeMsgId);
        }

        private static void SendPoll(DateTime targetDate)
        {
            DeletePoll();
            var culture = new CultureInfo("de-DE");
            var day = culture.DateTimeFormat.GetDayName(targetDate.DayOfWeek);
            langMsgText = $"*Große Runde für {day}, den {targetDate.ToShortDateString()} (Sprache):*";
            var t = client.SendTextMessageAsync(channelName, langMsgText + "\n" + GetCurrentLangPoll(),
                replyMarkup: GetLangReplyMarkup(), parseMode: ParseMode.Markdown);
            t.Wait();
            langMsgId = t.Result.MessageId;
            modeMsgText = $"*Große Runde für {day}, den {targetDate.ToShortDateString()} (Modus):*";
            t = client.SendTextMessageAsync(channelName, modeMsgText + "\n" + GetCurrentModePoll(),
                replyMarkup: GetModeReplyMarkup(), parseMode: ParseMode.Markdown);
            t.Wait();
            modeMsgId = t.Result.MessageId;
        }

        private static InlineKeyboardMarkup GetModeReplyMarkup()
        {
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var mode in modes)
            {
                rows.Add(new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton($"{mode} - {modeVotes.Count(x => x.Value == mode)}", mode)
                });
            }
            return new InlineKeyboardMarkup(rows.ToArray());
        }

        private static string GetCurrentModePoll()
        {
            return string.Join("\n\n", modes.OrderBy(x => -modeVotes.Count(y => y.Value == x)).Select(x =>
            {
                var c = modeVotes.Count(y => y.Value == x);
                var t = modeVotes.Count;
                float perc = 0;
                if (t != 0)
                    perc = (float)c / t;
                perc = perc * 100;
                var s = $"{x} - {c}\n";
                for (int i = 0; i < perc / 10; i++)
                {
                    s += "👍";
                }
                s += $" {perc}%";
                return s;
            }));
        }

        private static InlineKeyboardMarkup GetLangReplyMarkup()
        {
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var lang in languages)
            {
                rows.Add(new InlineKeyboardButton[]
                {
                    new InlineKeyboardCallbackButton($"{lang} - {langVotes.Count(x => x.Value == lang)}", lang)
                });
            }
            return new InlineKeyboardMarkup(rows.ToArray());
        }

        private static string GetCurrentLangPoll()
        {
            return string.Join("\n\n", languages.OrderBy(x => -langVotes.Count(y => y.Value == x)).Select(x =>
            {
                var c = langVotes.Count(y => y.Value == x);
                var t = langVotes.Count;
                float perc = 0;
                if (t != 0)
                    perc = (float)c / t;
                perc = perc * 100;
                var s = $"{x} - {c}\n";
                for (int i = 0; i < perc / 10; i++)
                {
                    s += "👍";
                }
                s += $" {perc}%";
                return s;
            }));
        }
    }
}