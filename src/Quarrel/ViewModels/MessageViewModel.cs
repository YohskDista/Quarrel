﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Quarrel.Messages.Navigation;
using Quarrel.Models.Bindables;
using UICompositionAnimations.Helpers;
using System.Collections;
using DiscordAPI.Models;
using GalaSoft.MvvmLight.Command;
using Quarrel.Messages.Gateway;
using Quarrel.Messages.Posts.Requests;
using Quarrel.Services;
using Windows.Web.Syndication;
using Microsoft.Toolkit.Uwp.UI.Controls.TextToolbarSymbols;

namespace Quarrel.ViewModels
{
    public class MessageViewModel : ViewModelBase
    {
        public MessageViewModel()
        {
            Messenger.Default.Register<ChannelNavigateMessage>(this, async m =>
            {
                Channel = m.Channel;

                using (await SourceMutex.LockAsync())
                {
                    NewItemsLoading = true;
                    IEnumerable<Message> itemList = null;
                    //if (Channel.ReadState == null)
                    //    itemList = await ServicesManager.Discord.ChannelService.GetChannelMessages(m.Channel.Model.Id);
                    //else
                    //    itemList = await ServicesManager.Discord.ChannelService.GetChannelMessagesAround(m.Channel.Model.Id, Channel.ReadState.LastMessageId, 50);
                    itemList = await ServicesManager.Discord.ChannelService.GetChannelMessages(m.Channel.Model.Id, 50);

                    await DispatcherHelper.RunAsync(() =>
                    {
                        Source.Clear();

                        Message lastItem = null;

                        KeyValuePair<string, BindableMessage>? scrollItem = null;

                        foreach (Message item in itemList.Reverse())
                        {
                            Source.Add(item.Id, new BindableMessage(item, guildId, lastItem, lastItem != null && m.Channel.ReadState != null && lastItem.Id == m.Channel.ReadState.LastMessageId));

                            if (lastItem != null && m.Channel.ReadState != null && lastItem.Id == m.Channel.ReadState.LastMessageId)
                            {
                                scrollItem = Source.LastOrDefault();
                            }

                            lastItem = item;
                        }

                        if (scrollItem.HasValue)
                            ScrollTo?.Invoke(this, scrollItem.Value);
                        else
                            ScrollTo?.Invoke(this, Source.LastOrDefault());
                    });
                    NewItemsLoading = false;
                }
            });

            Messenger.Default.Register<GatewayMessageRecievedMessage>(this, async m =>
            {
                if (Channel != null && Channel.Model.Id == m.Message.ChannelId)
                    await DispatcherHelper.RunAsync(() =>
                    {
                        Source.Add(m.Message.Id, new BindableMessage(m.Message, guildId, Source.LastOrDefault().Value.Model));
                    });
            });

            Messenger.Default.Register<GatewayMessageDeletedMessage>(this, async m => 
            {
                if (Channel != null && Channel.Model.Id == m.ChannelId)
                {
                    await DispatcherHelper.RunAsync(() =>
                    {
                        Source.Remove(m.MessageId);
                    });
                }
            });

            Messenger.Default.Register<GatewayMessageUpdatedMessage>(this, async m =>
            {
                if (Channel != null && Channel.Model.Id == m.Message.ChannelId)
                {
                    await DispatcherHelper.RunAsync(() =>
                    {
                        if (Source.ContainsKey(m.Message.Id))
                        {
                            Source[m.Message.Id].Update(m.Message);
                        }
                    });
                }
            });
        }

        public event EventHandler<KeyValuePair<string, BindableMessage>> ScrollTo;

        private string guildId
        {
            get => Channel.Model is GuildChannel gChn ? gChn.GuildId : "DM";
        }

        private BindableChannel _Channel;

        public BindableChannel Channel
        {
            get => _Channel;
            set => Set(ref _Channel, value);
        }

        private string _MessageText = "";

        public string MessageText
        {
            get => _MessageText;
            set => Set(ref _MessageText, value);
        }

        private protected AsyncMutex SourceMutex { get; } = new AsyncMutex();

        public bool NewItemsLoading;

        public bool OldItemsLoading;

        private bool ItemsLoading => NewItemsLoading || OldItemsLoading;

        public ObservableHashedCollection<string, BindableMessage> Source { get; private set; } = new ObservableHashedCollection<string, BindableMessage>(new List<KeyValuePair<string, BindableMessage>>());

        private RelayCommand sendMessageCommand;

        public RelayCommand SendMessageCommand => sendMessageCommand ?? (sendMessageCommand = new RelayCommand(() =>
        {
            string text = MessageText;
            var mentions = FindMentions(text);
            foreach (var mention in mentions)
            {
                if (mention[0] == '@')
                {
                    int discIndex = mention.IndexOf('#');
                    string username = mention.Substring(1, discIndex - 1);
                    string disc = mention.Substring(1 + discIndex);
                    User user;
                    var userList = Messenger.Default.Request<CurrentMemberListRequestMessage, List<BindableUser>>(new CurrentMemberListRequestMessage());

                    user = userList.FirstOrDefault(x => x.Model.User.Username == username && x.Model.User.Discriminator == disc).Model.User;

                    if (user != null)
                    {
                        text = text.Replace("@" + user.Username + "#" + user.Discriminator, "<@!" + user.Id + ">");
                    }
                }
                else if (mention[0] == '#')
                {
                    var guild = Messenger.Default.Request<CurrentGuildRequestMessage, BindableGuild>(new CurrentGuildRequestMessage());
                    if (!guild.IsDM)
                    {
                        var channel = guild.Channels.FirstOrDefault(x => x.Model.Type != 4 && x.Model.Name == mention.Substring(1)).Model;
                        text = text.Replace("#" + channel.Name, "<#" + channel.Id + ">");
                    }
                }
            }

            ServicesManager.Discord.ChannelService.CreateMessage(Channel.Model.Id, new DiscordAPI.API.Channel.Models.MessageUpsert() { Content = text });
            MessageText = "";
        }));


        private RelayCommand newLineCommand;

        public RelayCommand NewLineCommand =>
            newLineCommand ?? (newLineCommand = new RelayCommand(() =>
            {
                string text = MessageText;
                int selectionstart = SelectionStart;

                if (SelectionLength > 0)
                {
                    // Remove selected text first
                    text = text.Remove(selectionstart, SelectionLength);
                }

                text = text.Insert(selectionstart, Environment.NewLine + Environment.NewLine); //Not sure why two lines breaks are needed but it doesn't work otherwise
                MessageText = text;
                SelectionStart = selectionstart + 1;
            }));
        private List<string> FindMentions(string message)
        {
            List<string> mentions = new List<string>();
            bool inMention = false;
            bool inDesc = false;
            bool inChannel = false;
            string cache = "";
            string descCache = "";
            string chnCache = "";
            foreach (char c in message)
            {
                if (inMention)
                {
                    if (c == '#' && !inDesc)
                    {
                        inDesc = true;
                    }
                    else if (c == '@')
                    {
                        inDesc = false;
                        cache = "";
                        descCache = "";
                    }
                    else if (inDesc)
                    {
                        if (char.IsDigit(c))
                        {
                            descCache += c;
                        }
                        else
                        {
                            inMention = false;
                            inDesc = false;
                            cache = "";
                            descCache = "";
                        }
                        if (descCache.Length == 4)
                        {
                            User mention;
                            if (Channel.Model is DirectMessageChannel dmChn)
                            {
                                mention = dmChn.Users.FirstOrDefault(x => x.Username == cache && x.Discriminator == descCache);
                            }
                            else
                            {
                                GuildMember member = Messenger.Default
                                           .Request<CurrentMemberListRequestMessage, List<BindableUser>>(new CurrentMemberListRequestMessage())
                                           .FirstOrDefault(x => x.Model.User.Username == cache && x.Model.User.Discriminator == descCache).Model;
                                mention = member.User;
                            }
                            if (mention != null)
                            {
                                mentions.Add("@" + cache + "#" + descCache);
                            }
                            inMention = false;
                            inDesc = false;
                            cache = "";
                            descCache = "";
                        }
                    }
                    else
                    {
                        cache += c;
                    }
                }
                else if (inChannel)
                {
                    if (c == ' ')
                    {
                        inChannel = false;
                        chnCache = "";
                    }
                    else
                    {
                        chnCache += c;
                        if (Channel.Model is GuildChannel)
                        {
                            var guild = Messenger.Default.Request<CurrentGuildRequestMessage, BindableGuild>(new CurrentGuildRequestMessage());
                            if (!guild.IsDM)
                            {
                                mentions.Add("#" + chnCache);
                            }
                        }
                    }
                }
                else if (c == '@')
                {
                    inMention = true;
                }
                else if (c == '#')
                {
                    inChannel = true;
                }
            }
            return mentions;
        }


        private int _SelectionStart;

        public int SelectionStart
        {
            get => _SelectionStart;
            set => Set(ref _SelectionStart, value);
        }

        private int _SelectionLength;

        public int SelectionLength
        {
            get => _SelectionLength;
            set => Set(ref _SelectionLength, value);
        }

        public async void LoadOlderMessages()
        {
            if (ItemsLoading) return;
            using (await SourceMutex.LockAsync())
            {
                OldItemsLoading = true;
                IEnumerable<Message> itemList = await ServicesManager.Discord.ChannelService.GetChannelMessagesBefore(Channel.Model.Id, Source.FirstOrDefault().Value.Model.Id);

                await DispatcherHelper.RunAsync(() =>
                {
                    Message lastItem = null;
                    foreach (var item in itemList)
                    {
                        // Can't be last read item
                        Source.Insert(0, item.Id, new BindableMessage(item, guildId, lastItem));
                        lastItem = item;
                    }
                });
                OldItemsLoading = false;
            }
        }

        public async void LoadNewerMessages()
        {
            if (ItemsLoading) return;
            using (await SourceMutex.LockAsync())
            {
                NewItemsLoading = true;
                if (Source.ContainsKey(Channel.Model.LastMessageId))
                {
                    IEnumerable<Message> itemList = await ServicesManager.Discord.ChannelService.GetChannelMessagesAfter(Channel.Model.Id, Source.LastOrDefault().Value.Model.Id);

                    await DispatcherHelper.RunAsync(() =>
                    {
                        Message lastItem = null;
                        foreach (var item in itemList)
                        {
                            // Can't be last read item
                            Source.Add(item.Id, new BindableMessage(item, guildId, lastItem));
                            lastItem = item;
                        }
                    });
                }
                else if (Channel.Model.LastMessageId != Channel.ReadState.LastMessageId)
                {
                    await ServicesManager.Discord.ChannelService.AckMessage(Channel.Model.Id, Source.LastOrDefault().Value.Model.Id);
                }
                NewItemsLoading = false;
            }
        }
    }
}